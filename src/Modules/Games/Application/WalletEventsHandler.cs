using Casino.Contracts;

namespace Casino.Modules.Games.Application;

// Handler de Wolverine: consume la cola games.wallet-events (los hechos de la Wallet, via exchange fanout).
// Cada metodo es idempotente: la entrega es "al menos una vez".

// El mensaje solo trae el BetId: cada juego busca esa apuesta en lo suyo y, si no es suya, la ignora.
public sealed class WalletEventsHandler(RouletteService roulette, SlotsService slots)
{
    public async Task Handle(StakeReserved message, CancellationToken ct)
    {
        await roulette.OnStakeReservedAsync(message, ct);
        await slots.OnStakeReservedAsync(message, ct);
    }

    public async Task Handle(StakeRejected message, CancellationToken ct)
    {
        await roulette.OnStakeRejectedAsync(message, ct);
        await slots.OnStakeRejectedAsync(message, ct);
    }

    public async Task Handle(StakeSettled message, CancellationToken ct)
    {
        await roulette.OnStakeSettledAsync(message, ct);
        await slots.OnStakeSettledAsync(message, ct);
    }

    public async Task Handle(StakeReleased message, CancellationToken ct)
    {
        await roulette.OnStakeReleasedAsync(message, ct);
        await slots.OnStakeReleasedAsync(message, ct);
    }

    public async Task Handle(StakeSettlementRejected message, CancellationToken ct)
    {
        await roulette.OnSettlementRejectedAsync(message, ct);
        await slots.OnSettlementRejectedAsync(message, ct);
    }
}
