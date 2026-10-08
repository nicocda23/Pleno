using System.Collections.Concurrent;
using Casino.BuildingBlocks;
using Casino.Contracts;
using Casino.Modules.Users.Domain;
using JasperFx;
using Marten;
using Marten.Exceptions;

namespace Casino.Modules.Users.Application;

public sealed class UserService(IDocumentStore store, IOutboxFactory outbox, TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, UserProfile> _known = new();

    /// <summary>
    /// Alta automatica al primer ingreso. Si el usuario es nuevo, en UNA transaccion se guarda su perfil y se encola
    /// <see cref="UserRegistered"/> (la Wallet le abre la cuenta y le acredita las fichas de bienvenida).
    /// Es idempotente y tolera dos primeras peticiones simultaneas: una sola crea el perfil y publica el hecho.
    /// </summary>
    public async Task<UserProfile> EnsureProvisionedAsync(Guid userId, CancellationToken ct = default)
    {
        if (_known.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        await using (var read = store.QuerySession())
        {
            if (await read.LoadAsync<UserProfile>(userId, ct) is { } existing)
            {
                return _known[userId] = existing;
            }
        }

        var profile = new UserProfile { Id = userId, RegisteredAt = clock.GetUtcNow() };
        try
        {
            await using var session = store.LightweightSession();
            session.Insert(profile);
            await using var outboxSession = outbox.Enroll(session);
            await outboxSession.PublishAsync(new UserRegistered(userId));
            await session.SaveChangesAsync(ct);
            return _known[userId] = profile;
        }
        catch (Exception ex) when (ex is DocumentAlreadyExistsException || ex.InnerException is DocumentAlreadyExistsException)
        {
            // Otra peticion lo dio de alta en el mismo instante: ya esta, y fue ella quien publico el hecho.
            await using var read = store.QuerySession();
            var existing = await read.LoadAsync<UserProfile>(userId, ct)
                ?? throw new InvalidOperationException($"El perfil {userId} deberia existir tras un conflicto de alta.");
            return _known[userId] = existing;
        }
    }
}
