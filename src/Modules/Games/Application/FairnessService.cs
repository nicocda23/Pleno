using System.Net.Sockets;
using Casino.BuildingBlocks;
using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Infrastructure;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace Casino.Modules.Games.Application;

public sealed record ActivePairInfo(Guid PairId, string Commitment, string ClientSeed, long NextNonce);

public sealed record RetiredPairInfo(Guid PairId, string Commitment, string ServerSeed, string ClientSeed, long BetsPlayed);

/// <summary>Lo que cualquiera puede ver: compromiso y client seed del par activo, y las seeds ya reveladas de los pares cerrados.</summary>
public sealed record FairnessPublicInfo(Guid UserId, ActivePairInfo Active, int PendingBets, IReadOnlyList<RetiredPairInfo> Retired);

/// <summary>Entradas para sortear una apuesta. Contiene la server seed en claro: solo para uso interno, nunca se loguea ni se expone.</summary>
public sealed class DrawInputs(Guid pairId, string serverSeed, string clientSeed, long nonce)
{
    public Guid PairId { get; } = pairId;

    public string ServerSeed { get; } = serverSeed;

    public string ClientSeed { get; } = clientSeed;

    public long Nonce { get; } = nonce;

    public override string ToString() => $"DrawInputs {{ PairId = {PairId}, Nonce = {Nonce}, ServerSeed = *** }}";
}

public sealed class FairnessService(
    IDocumentStore store,
    SeedProtector protector,
    TimeProvider clock,
    int maxAttempts = FairnessService.DefaultMaxAttempts,
    IOutboxFactory? outbox = null)
{
    public const int DefaultMaxAttempts = 20;

    private const int GateCount = 64;
    private const int DefaultClientSeedBytes = 8;

    private readonly SemaphoreSlim[] _gates = [.. Enumerable.Range(0, GateCount).Select(_ => new SemaphoreSlim(1, 1))];

    /// <summary>Estado publico del usuario. La primera vez crea su primer par de seeds.</summary>
    public Task<FairnessPublicInfo> GetPublicInfoAsync(Guid userId, CancellationToken ct = default) =>
        ExecuteAsync(userId, ToPublicInfo, ct);

    /// <summary>
    /// Asigna el nonce a una apuesta. Atomico (la version del stream impide dos asignaciones iguales)
    /// e idempotente por apuesta. El cliente no participa: no puede elegir ni influir el nonce.
    /// <paramref name="inSameTransaction"/> se ejecuta solo cuando el nonce se asigna por primera vez y antes del commit:
    /// lo que haga con la sesion (guardar la ronda, encolar mensajes) se confirma o se revierte junto con la asignacion.
    /// </summary>
    public Task<NonceAllocation> AllocateNonceAsync(
        Guid userId,
        Guid betId,
        Func<IDocumentSession, IOutboxSession, NonceAllocation, ValueTask>? inSameTransaction = null,
        CancellationToken ct = default) =>
        ExecuteAsync(
            userId,
            a => a.AllocateNonce(betId, clock.GetUtcNow()),
            ct,
            inSameTransaction is null ? null : (session, outboxSession, allocation) => inSameTransaction(session, outboxSession!, allocation));

    /// <summary>Marca la apuesta como resuelta o anulada. Mientras haya apuestas pendientes no se puede rotar.</summary>
    public Task CompleteBetAsync(Guid userId, Guid betId, CancellationToken ct = default) =>
        ExecuteAsync(userId, a =>
        {
            a.CompleteBet(betId, clock.GetUtcNow());
            return true;
        }, ct);

    /// <summary>Revela la server seed activa y empieza un par nuevo. Si no se indica client seed, se genera una al azar.</summary>
    public Task<FairnessPublicInfo> RotateAsync(Guid userId, string? newClientSeed = null, CancellationToken ct = default) =>
        ExecuteAsync(userId, account =>
        {
            var active = account.Active;
            var revealed = protector.Unprotect(active.EncryptedServerSeed, Aad(userId, active.PairId));
            var next = NewPair(userId);

            account.Rotate(
                revealed, next.PairId, next.Commitment, next.EncryptedServerSeed,
                newClientSeed ?? NewClientSeed(), clock.GetUtcNow());
            return ToPublicInfo(account);
        }, ct);

    /// <summary>Solo para el motor de juegos: devuelve la server seed en claro de la apuesta. Nunca se expone por HTTP.</summary>
    public async Task<DrawInputs> GetDrawInputsAsync(Guid userId, Guid betId, CancellationToken ct = default)
    {
        var context = await ExecuteAsync(userId, a => a.GetDrawContext(betId), ct);
        var serverSeed = context.RevealedServerSeed
            ?? protector.Unprotect(context.EncryptedServerSeed!, Aad(userId, context.PairId));
        return new DrawInputs(context.PairId, serverSeed, context.ClientSeed, context.Nonce);
    }

    private static string Aad(Guid userId, Guid pairId) => $"seed:{userId:N}:{pairId:N}";

    private static string NewClientSeed() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(DefaultClientSeedBytes));

    private static FairnessPublicInfo ToPublicInfo(FairnessAccount account) => new(
        account.UserId,
        new ActivePairInfo(account.Active.PairId, account.Active.Commitment, account.Active.ClientSeed, account.Active.NextNonce),
        account.PendingBets,
        [.. account.Retired.Select(r => new RetiredPairInfo(r.PairId, r.Commitment, r.RevealedServerSeed, r.ClientSeed, r.BetsPlayed))]);

    private (Guid PairId, string Commitment, string EncryptedServerSeed) NewPair(Guid userId)
    {
        var pairId = Guid.CreateVersion7();
        var serverSeed = ProvablyFair.GenerateServerSeed();
        return (pairId, ProvablyFair.Commitment(serverSeed), protector.Protect(serverSeed, Aad(userId, pairId)));
    }

    private async Task<T> ExecuteAsync<T>(
        Guid userId,
        Func<FairnessAccount, T> operation,
        CancellationToken ct,
        Func<IDocumentSession, IOutboxSession?, T, ValueTask>? beforeSave = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan retryDelay;
            var gate = _gates[(userId.GetHashCode() & int.MaxValue) % GateCount];
            await gate.WaitAsync(ct);
            try
            {
                await using var session = store.LightweightSession();
                var stream = await session.Events.FetchStreamAsync(userId, token: ct);
                var loadedVersion = stream.Count;

                FairnessAccount account;
                if (loadedVersion == 0)
                {
                    var first = NewPair(userId);
                    account = FairnessAccount.Start(userId, first.PairId, first.Commitment, first.EncryptedServerSeed, NewClientSeed(), clock.GetUtcNow());
                }
                else
                {
                    account = FairnessAccount.Rehydrate(stream.Select(e => e.Data).OfType<FairnessEvent>());
                }

                var result = operation(account);

                var newEvents = account.UncommittedEvents.ToArray<object>();
                if (newEvents.Length == 0)
                {
                    return result;
                }

                if (loadedVersion == 0)
                {
                    session.Events.StartStream(userId, newEvents);
                }
                else
                {
                    session.Events.Append(userId, loadedVersion + newEvents.Length, newEvents);
                }

                // El outbox vive hasta despues del commit: lo que se encole sale con la misma transaccion.
                await using var outboxSession = beforeSave is not null ? outbox?.Enroll(session) : null;
                if (beforeSave is not null)
                {
                    await beforeSave(session, outboxSession, result);
                }

                await session.SaveChangesAsync(ct);
                return result;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex) && !ct.IsCancellationRequested)
            {
                retryDelay = TimeSpan.FromMilliseconds(Random.Shared.Next(5, 20) * Math.Min(attempt, 8));
            }
            finally
            {
                gate.Release();
            }

            await Task.Delay(retryDelay, ct);
        }
    }

    private static bool IsRetryable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is EventStreamUnexpectedMaxEventIdException
                or JasperFx.Events.ExistingStreamIdCollisionException
                or Marten.Exceptions.ExistingStreamIdCollisionException
                or ConcurrencyException
                or DocumentAlreadyExistsException
                or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                or TimeoutException
                or IOException
                or SocketException
                or NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
