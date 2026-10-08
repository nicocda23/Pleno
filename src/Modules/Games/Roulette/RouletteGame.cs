using Casino.Modules.Games.Fairness;

namespace Casino.Modules.Games.Roulette;

public sealed record RouletteOutcome(int WinningNumber, long Payout);

public static class RouletteGame
{
    /// <summary>Resuelve una apuesta de forma determinista a partir de las seeds y el nonce. No guarda ni publica nada.</summary>
    public static RouletteOutcome Play(RouletteBet bet, string serverSeed, string clientSeed, long nonce)
    {
        ArgumentNullException.ThrowIfNull(bet);

        var winningNumber = new FairRng(serverSeed, clientSeed, nonce).NextInt(RouletteBet.PocketCount);
        return new RouletteOutcome(winningNumber, bet.PayoutFor(winningNumber));
    }
}
