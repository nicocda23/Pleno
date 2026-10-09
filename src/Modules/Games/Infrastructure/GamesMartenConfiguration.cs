using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Roulette;
using Casino.Modules.Games.Slots;
using Marten;

namespace Casino.Modules.Games.Infrastructure;

public static class GamesMartenConfiguration
{
    /// <summary>Registra los tipos del modulo en un store de Marten que comparte el host (la conexion y el esquema los decide quien compone).</summary>
    public static void Register(StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Events.AddEventTypes(
        [
            typeof(SeedPairStarted),
            typeof(NonceAllocated),
            typeof(BetCompleted),
            typeof(SeedPairRotated),
        ]);

        // Dos manejadores que procesen el mismo mensaje a la vez no pueden pisarse: el segundo falla y reintenta.
        options.Schema.For<RouletteRound>().Identity(r => r.Id).UseOptimisticConcurrency(true).Index(r => r.UserId);
        options.Schema.For<SlotsSpin>().Identity(s => s.Id).UseOptimisticConcurrency(true).Index(s => s.UserId);
    }
}
