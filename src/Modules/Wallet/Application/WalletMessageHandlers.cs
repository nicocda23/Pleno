using Casino.Contracts;
using Casino.Modules.Wallet.Domain;

namespace Casino.Modules.Wallet.Application;

// Handlers de Wolverine (los descubre por convencion: clase terminada en "Handler", metodo "Handle").
// Hay entrega "al menos una vez": cada orden es idempotente porque su IdempotencyKey se deriva de la apuesta.
// Los hechos (StakeReserved, StakeSettled...) los publica WalletService por el outbox, dentro de la transaccion.
// Aca solo se responden los rechazos de negocio, que no cambian estado y por eso no pasan por el outbox.

/// <summary>Un juego pide reservar las fichas de una apuesta.</summary>
public sealed class ReserveStakeHandler(WalletService wallet)
{
    public async Task<StakeRejected?> Handle(ReserveStake command, CancellationToken ct)
    {
        try
        {
            await wallet.ReserveAsync(command.AccountId, $"reserve:{command.BetId:N}", command.BetId, command.Stake, ct);
            return null;
        }
        catch (WalletDomainException ex)
        {
            return new StakeRejected(command.BetId, command.AccountId, ex.Error.ToString());
        }
    }
}

/// <summary>Un juego informa el resultado: la Wallet liquida con el premio total.</summary>
public sealed class RoundResolvedHandler(WalletService wallet)
{
    public async Task<StakeSettlementRejected?> Handle(RoundResolved message, CancellationToken ct)
    {
        try
        {
            await wallet.SettleAsync(message.AccountId, $"settle:{message.BetId:N}", message.BetId, message.Payout, ct);
            return null;
        }
        catch (WalletDomainException ex)
        {
            // Por ejemplo, la reserva ya vencio y se libero: el resultado llego tarde y el juego debe anular la ronda.
            return new StakeSettlementRejected(message.BetId, message.AccountId, ex.Error.ToString());
        }
    }
}
