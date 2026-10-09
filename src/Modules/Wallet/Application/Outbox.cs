using Casino.Contracts;
using Casino.Modules.Wallet.Domain;

namespace Casino.Modules.Wallet.Application;

/// <summary>Traduce los eventos internos de la Wallet a los hechos publicos (contratos) que consumen otros modulos.</summary>
internal static class WalletIntegrationEvents
{
    public static IReadOnlyList<object> From(Guid accountId, IReadOnlyCollection<WalletEvent> events, WalletAccount account)
    {
        var messages = new List<object>();
        foreach (var @event in events)
        {
            switch (@event)
            {
                case BetReserved reserved:
                    messages.Add(new StakeReserved(reserved.ReservationId, accountId, reserved.Stake, reserved.TransactionId));
                    break;
                case BetSettled settled:
                    messages.Add(new StakeSettled(settled.ReservationId, accountId, settled.Stake, settled.Payout, settled.TransactionId));
                    break;
                case ReservationReleased released:
                    messages.Add(new StakeReleased(released.ReservationId, accountId, released.Stake, released.TransactionId));
                    break;
            }
        }

        if (events.Any(e => e is LedgerEvent))
        {
            messages.Add(new BalanceChanged(accountId, account.Available, account.Reserved, account.Version));
        }

        return messages;
    }
}
