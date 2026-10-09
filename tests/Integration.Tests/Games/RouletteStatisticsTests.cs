using Casino.Integration.Tests.Wallet;
using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Roulette;
using Xunit.Abstractions;

namespace Casino.Integration.Tests.Games;

/// <summary>
/// Cordura estadistica del sistema REAL (asignacion de nonce, descifrado de seeds, rotaciones), no solo de la funcion matematica:
/// miles de tiradas como las haria un jugador que rota sus seeds, con Postgres de verdad. Existe porque un jugador noto una racha
/// de numeros repetidos y la unica respuesta honesta es medir.
/// </summary>
[Collection(WalletDbDefinition.Name)]
public sealed class RouletteStatisticsTests(PostgresFixture db, ITestOutputHelper output)
{
    private const int Blocks = 24;
    private const int DrawsPerBlock = 40;
    private const int Pockets = RouletteBet.PocketCount;

    private sealed record Draw(Guid PairId, long Nonce, int Number);

    private async Task<(List<Draw> Draws, Dictionary<Guid, (string Server, string Client)> Revealed)> PlayAsync()
    {
        var userId = Guid.NewGuid();
        var draws = new List<Draw>();
        var revealed = new Dictionary<Guid, (string Server, string Client)>();

        for (var block = 0; block < Blocks; block++)
        {
            for (var i = 0; i < DrawsPerBlock; i++)
            {
                var betId = Guid.NewGuid();
                var allocation = await db.Fairness.AllocateNonceAsync(userId, betId);
                var inputs = await db.Fairness.GetDrawInputsAsync(userId, betId);
                var number = RouletteGame.Play(RouletteBet.Create(RouletteBetType.Red, [], 1), inputs.ServerSeed, inputs.ClientSeed, inputs.Nonce).WinningNumber;
                await db.Fairness.CompleteBetAsync(userId, betId);
                draws.Add(new Draw(allocation.PairId, allocation.Nonce, number));
            }

            // El jugador rota: se revela la seed del bloque y empieza una nueva (nonce desde 0).
            var info = await db.Fairness.RotateAsync(userId);
            foreach (var retired in info.Retired)
            {
                revealed[retired.PairId] = (retired.ServerSeed, retired.ClientSeed);
            }
        }

        return (draws, revealed);
    }

    [Fact]
    public async Task Thousands_of_real_draws_are_uniform_unpredictable_and_all_verifiable()
    {
        var (draws, revealed) = await PlayAsync();
        var total = draws.Count;
        Assert.Equal(Blocks * DrawsPerBlock, total);

        // 1) Cada tirada se puede verificar con la seed revelada: recalculada da exactamente lo mismo.
        foreach (var draw in draws)
        {
            var (server, client) = revealed[draw.PairId];
            Assert.Equal(draw.Number, new FairRng(server, client, draw.Nonce).NextInt(Pockets));
        }

        // 2) Los nonces de cada par son 0, 1, 2... sin saltos ni repetidos; cada par tiene su propio compromiso.
        foreach (var group in draws.GroupBy(d => d.PairId))
        {
            Assert.Equal(Enumerable.Range(0, DrawsPerBlock).Select(n => (long)n), group.Select(d => d.Nonce).Order());
        }

        Assert.Equal(Blocks, draws.Select(d => d.PairId).Distinct().Count());
        Assert.Equal(Blocks, revealed.Values.Select(v => ProvablyFair.Commitment(v.Server)).Distinct().Count());

        // 3) Uniformidad: chi-cuadrado con 36 grados de libertad. 80 equivale a p ~ 1e-5: no da falsas alarmas y detecta sesgos reales.
        var counts = new int[Pockets];
        foreach (var draw in draws)
        {
            counts[draw.Number]++;
        }

        var expected = total / (double)Pockets;
        var chiSquare = counts.Sum(observed => Math.Pow(observed - expected, 2) / expected);
        Assert.True(chiSquare < 80, $"chi-cuadrado = {chiSquare:F1}");

        // 4) Sin memoria: la frecuencia con que sale el mismo numero dos veces seguidas es 1/37.
        var adjacentRepeats = Enumerable.Range(1, total - 1).Count(i => draws[i].Number == draws[i - 1].Number);
        var trials = total - 1;
        var mean = trials / (double)Pockets;
        var sigma = Math.Sqrt(trials * (1.0 / Pockets) * (1 - (1.0 / Pockets)));
        Assert.InRange(adjacentRepeats, mean - (6 * sigma), mean + (6 * sigma));

        // 5) Rachas: cinco iguales seguidas tiene probabilidad ~ 1e-6 por posicion; con ~1.000 tiradas es practicamente imposible.
        var longestRun = 1;
        var run = 1;
        for (var i = 1; i < total; i++)
        {
            run = draws[i].Number == draws[i - 1].Number ? run + 1 : 1;
            longestRun = Math.Max(longestRun, run);
        }

        Assert.True(longestRun <= 4, $"racha de {longestRun} iguales seguidas");

        // 6) Cada numero sale con una frecuencia razonable (ninguno "pegado" ni ausente).
        Assert.All(counts, count => Assert.InRange(count, 8, 60));

        output.WriteLine($"{total} tiradas en {Blocks} pares de seeds | chi-cuadrado {chiSquare:F1} (limite 80)");
        output.WriteLine($"repeticiones consecutivas: {adjacentRepeats} (esperadas ~{mean:F1} +/- {sigma:F1}) | racha mas larga: {longestRun}");
        output.WriteLine($"minimo/maximo por numero: {counts.Min()}/{counts.Max()} (esperado ~{expected:F0})");
    }
}
