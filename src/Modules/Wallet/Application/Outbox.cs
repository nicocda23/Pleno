using Casino.Contracts;
using Casino.Modules.Wallet.Domain;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Marten;

namespace Casino.Modules.Wallet.Application;

/// <summary>Un outbox enlistado en una sesion de Marten: lo que se publique se guarda en la MISMA transaccion que los eventos.</summary>
public interface IOutboxSession : IAsyncDisposable
{
    ValueTask PublishAsync(object message);
}

public interface IOutboxFactory
{
    IOutboxSession Enroll(IDocumentSession session);
}

/// <summary>Outbox transaccional de Wolverine sobre Marten. Cada intento de una operacion abre su propio scope.</summary>
public sealed class WolverineOutboxFactory(IServiceScopeFactory scopes) : IOutboxFactory
{
    public IOutboxSession Enroll(IDocumentSession session)
    {
        var scope = scopes.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMartenOutbox>();
        outbox.Enroll(session);
        return new Session(scope, outbox);
    }

    private sealed class Session(AsyncServiceScope scope, IMartenOutbox outbox) : IOutboxSession
    {
        public ValueTask PublishAsync(object message) => outbox.PublishAsync(message);

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}

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
