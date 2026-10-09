using Casino.Modules.Games.Fairness;

namespace Casino.Modules.Games.Slots;

/// <summary>Resultado de un giro: el simbolo de cada rodillo, cuanto se multiplica la apuesta y cuanto se cobra.</summary>
public sealed record SlotsOutcome(IReadOnlyList<string> Reels, long Multiplier, long Payout);

public static class SlotsGame
{
    /// <summary>
    /// Resuelve un giro de forma determinista a partir de las seeds y el nonce (el mismo esquema que la ruleta).
    /// Cada rodillo consume un numero del flujo, asi que el giro se puede recalcular y verificar.
    /// </summary>
    public static SlotsOutcome Play(SlotsPaytable paytable, long stake, string serverSeed, string clientSeed, long nonce)
    {
        ArgumentNullException.ThrowIfNull(paytable);
        ArgumentOutOfRangeException.ThrowIfLessThan(stake, 1);

        var rng = new FairRng(serverSeed, clientSeed, nonce);
        var reels = new int[SlotsPaytable.ReelCount];
        for (var i = 0; i < reels.Length; i++)
        {
            reels[i] = paytable.SymbolAtStop(rng.NextInt(paytable.TotalWeight));
        }

        var multiplier = paytable.MultiplierFor(reels);
        return new SlotsOutcome([.. reels.Select(r => paytable.Symbols[r].Name)], multiplier, checked(stake * multiplier));
    }
}
