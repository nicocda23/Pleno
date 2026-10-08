using Casino.Contracts;

namespace Casino.Modules.Games.Application;

// Handler de Wolverine: consume la cola games.wallet-events (los hechos de la Wallet, via exchange fanout).
// Cada metodo es idempotente: la entrega es "al menos una vez".

public sealed class WalletEventsHandler(RouletteService roulette)
{
    public Task Handle(StakeReserved message, CancellationToken ct) => roulette.OnStakeReservedAsync(message, ct);

    public Task Handle(StakeRejected message, CancellationToken ct) => roulette.OnStakeRejectedAsync(message, ct);

    public Task Handle(StakeSettled message, CancellationToken ct) => roulette.OnStakeSettledAsync(message, ct);

    public Task Handle(StakeReleased message, CancellationToken ct) => roulette.OnStakeReleasedAsync(message, ct);

    public Task Handle(StakeSettlementRejected message, CancellationToken ct) => roulette.OnSettlementRejectedAsync(message, ct);
}
