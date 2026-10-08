namespace Casino.Modules.Wallet.Domain;

/// <summary>Tipo de cuenta contable. Todas las cuentas guardan fichas (enteros), sin unidades.</summary>
public enum LedgerAccountKind
{
    /// <summary>Fichas disponibles del jugador.</summary>
    Player = 1,

    /// <summary>Fichas del jugador apartadas mientras se juega una apuesta.</summary>
    Reserve = 2,

    /// <summary>Contraparte del casino. Su saldo se deriva en una proyeccion, no es un stream.</summary>
    House = 3,
}

public readonly record struct LedgerAccountRef(LedgerAccountKind Kind, Guid OwnerId)
{
    public static readonly LedgerAccountRef House = new(LedgerAccountKind.House, Guid.Empty);

    public static LedgerAccountRef Player(Guid userId) => new(LedgerAccountKind.Player, userId);

    public static LedgerAccountRef Reserve(Guid userId) => new(LedgerAccountKind.Reserve, userId);
}

/// <summary>Asiento contable: monto con signo en fichas. Positivo suma, negativo resta.</summary>
public readonly record struct Entry(LedgerAccountRef Account, long Amount);

public static class LedgerEntries
{
    /// <summary>Valida la invariante de la partida doble: asientos distintos de cero que suman exactamente cero.</summary>
    public static IReadOnlyList<Entry> Balanced(params Entry[] entries)
    {
        if (entries.Length < 2)
        {
            throw new InvalidOperationException("Una transaccion necesita al menos dos asientos.");
        }

        long sum = 0;
        foreach (var entry in entries)
        {
            if (entry.Amount == 0)
            {
                throw new InvalidOperationException("Un asiento no puede ser cero.");
            }

            sum = checked(sum + entry.Amount);
        }

        if (sum != 0)
        {
            throw new InvalidOperationException($"Los asientos no suman cero (suman {sum}).");
        }

        return entries;
    }

    public static IReadOnlyList<Entry> Negate(IReadOnlyList<Entry> entries) =>
        Balanced([.. entries.Select(e => e with { Amount = checked(-e.Amount) })]);
}
