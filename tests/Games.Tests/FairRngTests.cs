using Casino.Modules.Games.Fairness;

namespace Casino.Games.Tests;

public class FairRngTests
{
    // Vectores generados con una implementacion independiente en Node.js (ver docs/provably-fair.md).
    private const string ServerSeed = "9f2c4a7e1b3d58606a1f0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ff";
    private const string ClientSeed = "pleno-lab";
    private const string Commitment = "52ca56a3d81d3be381d594a5bca342bb63f6a3597776fed89138c9244fcbd2c8";

    private static int[] Draw(long nonce, int max, int count, string clientSeed = ClientSeed)
    {
        var stream = new FairRng(ServerSeed, clientSeed, nonce);
        return [.. Enumerable.Range(0, count).Select(_ => stream.NextInt(max))];
    }

    private static int[] Parse(string csv) => [.. csv.Split(',').Select(int.Parse)];

    [Fact]
    public void Commitment_matches_the_independent_implementation()
    {
        Assert.Equal(Commitment, ProvablyFair.Commitment(ServerSeed));
    }

    [Fact]
    public void Revealed_seed_is_checked_against_the_commitment()
    {
        Assert.True(ProvablyFair.MatchesCommitment(ServerSeed, Commitment));
        Assert.True(ProvablyFair.MatchesCommitment(ServerSeed, Commitment.ToUpperInvariant()));
        Assert.False(ProvablyFair.MatchesCommitment(ServerSeed + "0", Commitment));
        Assert.False(ProvablyFair.MatchesCommitment(ServerSeed, new string('0', 64)));
    }

    [Theory]
    [InlineData(0, "26,13,21,22,9,30,11,7,5,26,27,4")]
    [InlineData(1, "32,26,19,14,34,27,23,28,23,1,27,19")]
    [InlineData(2, "23,20,24,21,5,16,14,13,7,29,29,24")]
    public void Roulette_draws_match_the_independent_implementation(long nonce, string expected)
    {
        Assert.Equal(Parse(expected), Draw(nonce, 37, 12));
    }

    [Fact]
    public void Draws_that_span_several_hmac_blocks_match_the_independent_implementation()
    {
        Assert.Equal(Parse("13,31,31,23,14,15,18,0,25,18,3,23,24,3,23,28,1,30,5,29"), Draw(9, 37, 20));
    }

    [Fact]
    public void Large_ranges_match_the_independent_implementation()
    {
        Assert.Equal(Parse("544190317,69811089,683837891,298158533"), Draw(7, 1_500_000_000, 4));
    }

    [Fact]
    public void Same_inputs_always_give_the_same_draws()
    {
        Assert.Equal(Draw(5, 37, 50), Draw(5, 37, 50));
    }

    [Fact]
    public void Changing_nonce_or_client_seed_changes_the_draws()
    {
        var baseline = Draw(5, 1_000_000, 10);

        Assert.NotEqual(baseline, Draw(6, 1_000_000, 10));
        Assert.NotEqual(baseline, Draw(5, 1_000_000, 10, clientSeed: "otra-seed"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(37)]
    [InlineData(1_000)]
    [InlineData(int.MaxValue)]
    public void Draws_stay_inside_the_requested_range(int max)
    {
        var stream = new FairRng(ServerSeed, ClientSeed, 0);

        for (var i = 0; i < 2_000; i++)
        {
            var value = stream.NextInt(max);
            Assert.InRange(value, 0, max - 1);
        }
    }

    [Fact]
    public void Roulette_pockets_are_uniformly_distributed()
    {
        const int nonces = 3_000;
        const int drawsPerNonce = 37;
        var counts = new int[37];
        for (long nonce = 0; nonce < nonces; nonce++)
        {
            foreach (var value in Draw(nonce, 37, drawsPerNonce))
            {
                counts[value]++;
            }
        }

        var expected = nonces * drawsPerNonce / 37.0;
        var chiSquare = counts.Sum(observed => Math.Pow(observed - expected, 2) / expected);

        // Valor critico de chi-cuadrado con 36 grados de libertad y p = 0,001.
        Assert.True(chiSquare < 67.99, $"chi-cuadrado = {chiSquare:F2}");
    }

    [Fact]
    public void Large_ranges_have_no_modulo_bias()
    {
        // Con un simple "resto", los valores bajos aparecerian ~90% de las veces en vez de ~86%.
        const int max = 1_500_000_000;
        const long lowThreshold = (1L << 32) % max;
        var stream = new FairRng(ServerSeed, ClientSeed, 1);
        const int samples = 200_000;

        var low = Enumerable.Range(0, samples).Count(_ => stream.NextInt(max) < lowThreshold);

        var unbiased = (double)lowThreshold / max;
        Assert.InRange((double)low / samples, unbiased - 0.01, unbiased + 0.01);
    }

    [Fact]
    public void Generated_server_seeds_are_64_hex_chars_and_unique()
    {
        var seeds = Enumerable.Range(0, 100).Select(_ => ProvablyFair.GenerateServerSeed()).ToList();

        Assert.All(seeds, seed => Assert.Matches("^[0-9a-f]{64}$", seed));
        Assert.Equal(100, seeds.Distinct().Count());
    }

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FairRng("", ClientSeed, 0));
        Assert.Throws<ArgumentException>(() => new FairRng(ServerSeed, " ", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FairRng(ServerSeed, new string('x', 65), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FairRng(ServerSeed, ClientSeed, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FairRng(ServerSeed, ClientSeed, 0).NextInt(0));
    }
}
