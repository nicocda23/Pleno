using Casino.Modules.Games.Fairness;
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
    }
}
