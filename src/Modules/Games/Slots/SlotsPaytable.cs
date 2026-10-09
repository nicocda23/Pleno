namespace Casino.Modules.Games.Slots;

/// <summary>Un simbolo del rodillo: cuantas veces aparece en la cinta (peso) y cuanto paga por apuesta si salen tres iguales.</summary>
public sealed record SlotSymbol(string Name, int Weight, long TriplePayout);

/// <summary>Premio chico por una racha inicial: los primeros <paramref name="Count"/> rodillos (desde la izquierda) muestran <paramref name="Symbol"/>.</summary>
public sealed record LeadingPay(string Symbol, int Count, long Payout);

/// <summary>
/// Tabla de pagos de una tragamonedas de tres rodillos. Cada rodillo es independiente y elige un simbolo con probabilidad
/// proporcional a su peso. Los pagos son multiplicadores ENTEROS de la apuesta, asi que el pago siempre es un entero.
/// El retorno al jugador (RTP) se calcula exacto con enteros y NUNCA puede superar el 100%: la casa no puede perder a la larga.
/// </summary>
public sealed class SlotsPaytable
{
    public const int ReelCount = 3;
    private const int MaxSymbols = 12;
    private const long MaxMultiplier = 100_000;

    private readonly Dictionary<(string Symbol, int Count), long> _leading;

    public SlotsPaytable(IEnumerable<SlotSymbol> symbols, IEnumerable<LeadingPay> leadingPays, long maxStake)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(leadingPays);

        Symbols = [.. symbols];
        LeadingPays = [.. leadingPays];
        MaxStake = maxStake;

        if (Symbols.Count is < 2 or > MaxSymbols)
        {
            throw new ArgumentException($"La tabla necesita entre 2 y {MaxSymbols} simbolos.", nameof(symbols));
        }

        if (Symbols.Any(s => string.IsNullOrWhiteSpace(s.Name))
            || Symbols.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Symbols.Count)
        {
            throw new ArgumentException("Los simbolos necesitan un nombre y no pueden repetirse.", nameof(symbols));
        }

        if (Symbols.Any(s => s.Weight < 1 || s.TriplePayout < 0 || s.TriplePayout > MaxMultiplier))
        {
            throw new ArgumentException($"El peso es al menos 1 y los pagos van de 0 a {MaxMultiplier}.", nameof(symbols));
        }

        _leading = [];
        foreach (var pay in LeadingPays)
        {
            var index = IndexOf(pay.Symbol);
            if (pay.Count is < 1 or >= ReelCount
                || pay.Payout is < 0 or > MaxMultiplier
                || index < 0
                || !_leading.TryAdd((Symbols[index].Name, pay.Count), pay.Payout))
            {
                throw new ArgumentException($"Premio de racha invalido: {pay.Symbol} x{pay.Count}.", nameof(leadingPays));
            }
        }

        if (maxStake is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStake), "La apuesta maxima va de 1 a 1.000.000 de fichas.");
        }

        TotalWeight = Symbols.Sum(s => s.Weight);
        Denominator = (long)TotalWeight * TotalWeight * TotalWeight;
        ExpectedPayoutUnits = ComputeExpectedPayoutUnits();

        if (ExpectedPayoutUnits <= 0 || ExpectedPayoutUnits > Denominator)
        {
            throw new ArgumentException(
                $"El retorno al jugador debe ser mayor que 0 y no superar el 100% (es {ReturnToPlayerPercent:F2}%): con mas, la casa perderia a la larga.",
                nameof(symbols));
        }
    }

    public IReadOnlyList<SlotSymbol> Symbols { get; }

    public IReadOnlyList<LeadingPay> LeadingPays { get; }

    public long MinStake => 1;

    public long MaxStake { get; }

    /// <summary>Suma de los pesos de un rodillo: cuantas "paradas" tiene la cinta.</summary>
    public int TotalWeight { get; }

    /// <summary>Cantidad total de combinaciones ponderadas (paradas elevado a la cantidad de rodillos).</summary>
    public long Denominator { get; }

    /// <summary>Suma, sobre todas las combinaciones ponderadas, del multiplicador que pagan. RTP = esto / <see cref="Denominator"/>.</summary>
    public long ExpectedPayoutUnits { get; }

    /// <summary>Retorno al jugador en porcentaje (solo para mostrar: los calculos usan enteros).</summary>
    public double ReturnToPlayerPercent => 100.0 * ExpectedPayoutUnits / Denominator;

    /// <summary>Probabilidad (en porcentaje) de que una tirada pague algo.</summary>
    public double HitRatePercent => 100.0 * CountWinningUnits() / Denominator;

    /// <summary>Tabla por defecto: retorno de 96,14% y premio mayor de x100. Se puede reemplazar desde la configuracion (seccion Slots).</summary>
    public static SlotsPaytable Default { get; } = new(
        [
            new SlotSymbol("Cereza", 20, 7),
            new SlotSymbol("Limon", 16, 10),
            new SlotSymbol("Naranja", 12, 14),
            new SlotSymbol("Campana", 8, 25),
            new SlotSymbol("Bar", 5, 50),
            new SlotSymbol("Siete", 3, 100),
        ],
        [
            new LeadingPay("Cereza", 2, 3),
            new LeadingPay("Cereza", 1, 1),
        ],
        maxStake: 10_000);

    /// <summary>Indice del simbolo que cae en una parada de la cinta (0 a TotalWeight - 1).</summary>
    public int SymbolAtStop(int stop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stop);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stop, TotalWeight);

        var remaining = stop;
        for (var i = 0; i < Symbols.Count; i++)
        {
            remaining -= Symbols[i].Weight;
            if (remaining < 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Parada fuera de la cinta.");
    }

    /// <summary>Cuantas veces se multiplica la apuesta para esta combinacion de simbolos (indices por rodillo).</summary>
    public long MultiplierFor(IReadOnlyList<int> reels)
    {
        ArgumentNullException.ThrowIfNull(reels);
        if (reels.Count != ReelCount || reels.Any(r => r < 0 || r >= Symbols.Count))
        {
            throw new ArgumentException($"Se esperan {ReelCount} rodillos con simbolos validos.", nameof(reels));
        }

        var first = reels[0];
        var run = 1;
        while (run < ReelCount && reels[run] == first)
        {
            run++;
        }

        return run == ReelCount ? Symbols[first].TriplePayout : _leading.GetValueOrDefault((Symbols[first].Name, run));
    }

    public int IndexOf(string name)
    {
        for (var i = 0; i < Symbols.Count; i++)
        {
            if (string.Equals(Symbols[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private long ComputeExpectedPayoutUnits()
    {
        long total = 0;
        foreach (var (reels, weight) in Combinations())
        {
            total = checked(total + (weight * MultiplierFor(reels)));
        }

        return total;
    }

    private long CountWinningUnits()
    {
        long total = 0;
        foreach (var (reels, weight) in Combinations())
        {
            if (MultiplierFor(reels) > 0)
            {
                total += weight;
            }
        }

        return total;
    }

    private IEnumerable<(int[] Reels, long Weight)> Combinations()
    {
        for (var a = 0; a < Symbols.Count; a++)
        {
            for (var b = 0; b < Symbols.Count; b++)
            {
                for (var c = 0; c < Symbols.Count; c++)
                {
                    yield return ([a, b, c], (long)Symbols[a].Weight * Symbols[b].Weight * Symbols[c].Weight);
                }
            }
        }
    }
}
