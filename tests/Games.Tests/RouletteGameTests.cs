using Casino.Modules.Games.Roulette;

namespace Casino.Games.Tests;

public class RouletteGameTests
{
    // Mismos vectores que docs/provably-fair.md: con nonce 0 sale el 26 (negro) y con nonce 1 el 32 (rojo).
    private const string ServerSeed = "9f2c4a7e1b3d58606a1f0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ff";
    private const string ClientSeed = "pleno-lab";

    [Fact]
    public void Play_uses_the_first_draw_of_the_fair_stream()
    {
        var black = RouletteBet.Create(RouletteBetType.Black, [], 100);
        var red = RouletteBet.Create(RouletteBetType.Red, [], 100);

        var blackOutcome = RouletteGame.Play(black, ServerSeed, ClientSeed, 0);
        var redOutcome = RouletteGame.Play(red, ServerSeed, ClientSeed, 0);

        Assert.Equal(26, blackOutcome.WinningNumber);
        Assert.Equal(200, blackOutcome.Payout);
        Assert.Equal(26, redOutcome.WinningNumber);
        Assert.Equal(0, redOutcome.Payout);
    }

    [Fact]
    public void Each_nonce_gives_its_own_verifiable_result()
    {
        var red = RouletteBet.Create(RouletteBetType.Red, [], 100);

        var second = RouletteGame.Play(red, ServerSeed, ClientSeed, 1);

        Assert.Equal(32, second.WinningNumber);
        Assert.Equal(200, second.Payout);
    }

    [Fact]
    public void Replaying_the_same_inputs_gives_the_same_outcome()
    {
        var bet = RouletteBet.Create(RouletteBetType.Straight, [7], 10);

        Assert.Equal(
            RouletteGame.Play(bet, ServerSeed, ClientSeed, 42),
            RouletteGame.Play(bet, ServerSeed, ClientSeed, 42));
    }

    [Fact]
    public void Long_run_return_to_player_is_close_to_36_over_37()
    {
        var bet = RouletteBet.Create(RouletteBetType.Red, [], 100);
        const int rounds = 100_000;

        var returned = Enumerable.Range(0, rounds).Sum(nonce => RouletteGame.Play(bet, ServerSeed, ClientSeed, nonce).Payout);

        var rtp = (double)returned / (rounds * 100L);
        Assert.InRange(rtp, (36.0 / 37) - 0.02, (36.0 / 37) + 0.02);
    }
}
