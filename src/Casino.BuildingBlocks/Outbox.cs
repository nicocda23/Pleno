using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Marten;

namespace Casino.BuildingBlocks;

/// <summary>Un outbox enlistado en una sesion de Marten: lo que se publique se guarda en la MISMA transaccion que los datos.</summary>
public interface IOutboxSession : IAsyncDisposable
{
    /// <summary>Encola un mensaje. Con <paramref name="delay"/> queda programado y se entrega recien pasado ese tiempo.</summary>
    ValueTask PublishAsync(object message, TimeSpan? delay = null);
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
        public ValueTask PublishAsync(object message, TimeSpan? delay = null) =>
            delay is { } wait
                ? outbox.PublishAsync(message, new DeliveryOptions { ScheduleDelay = wait })
                : outbox.PublishAsync(message);

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
