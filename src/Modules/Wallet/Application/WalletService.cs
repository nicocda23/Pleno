using System.Net.Sockets;
using Casino.Modules.Wallet.Domain;
using Casino.Modules.Wallet.Infrastructure;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace Casino.Modules.Wallet.Application;

/// <summary>Saldo reconstruido a una fecha, a partir de los eventos (nunca de snapshots).</summary>
public sealed record BalanceAtDate(
    Guid AccountId,
    DateTimeOffset AsOf,
    long Available,
    long Reserved,
    long Total,
    long EventsApplied);

public sealed class WalletService(
    IDocumentStore store,
    TimeProvider clock,
    int maxAttempts = WalletService.DefaultMaxAttempts,
    int snapshotEvery = WalletService.DefaultSnapshotEvery)
{
    public const int DefaultMaxAttempts = 20;
    public const int DefaultSnapshotEvery = 50;

    private const int GateCount = 256;

    // Compuertas por cuenta (striping): serializan las operaciones de una misma cuenta dentro de este proceso
    // para no desperdiciar reintentos. La concurrencia optimista sigue siendo la garantia entre instancias.
    private static readonly SemaphoreSlim[] Gates = [.. Enumerable.Range(0, GateCount).Select(_ => new SemaphoreSlim(1, 1))];

    public async Task<Guid> OpenAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var accountId = Guid.CreateVersion7();
        var account = WalletAccount.Open(accountId, userId, clock.GetUtcNow());

        await using var session = store.LightweightSession();
        session.Events.StartStream(accountId, account.UncommittedEvents.ToArray<object>());
        await session.SaveChangesAsync(ct);
        return accountId;
    }

    public Task<OperationOutcome> CreditAsync(Guid accountId, string key, long amount, CancellationToken ct = default) =>
        ExecuteAsync(accountId, a => a.Credit(key, amount, clock.GetUtcNow()), ct);

    public Task<OperationOutcome> ReserveAsync(Guid accountId, string key, Guid reservationId, long stake, CancellationToken ct = default) =>
        ExecuteAsync(accountId, a => a.Reserve(key, reservationId, stake, clock.GetUtcNow()), ct);

    public Task<OperationOutcome> SettleAsync(Guid accountId, string key, Guid reservationId, long payout, CancellationToken ct = default) =>
        ExecuteAsync(accountId, a => a.Settle(key, reservationId, payout, clock.GetUtcNow()), ct);

    public Task<OperationOutcome> ReleaseAsync(Guid accountId, string key, Guid reservationId, CancellationToken ct = default) =>
        ExecuteAsync(accountId, a => a.Release(key, reservationId, clock.GetUtcNow()), ct);

    public Task<OperationOutcome> ReverseAsync(Guid accountId, string key, Guid transactionId, CancellationToken ct = default) =>
        ExecuteAsync(accountId, a => a.Reverse(key, transactionId, clock.GetUtcNow()), ct);

    public async Task<WalletAccount> GetAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        return await LoadAsync(session, accountId, ct);
    }

    /// <summary>
    /// Auditoria: reconstruye el saldo aplicando solo los eventos ocurridos hasta <paramref name="asOf"/> (inclusive).
    /// Ignora los snapshots a proposito: se recalcula desde la historia. Devuelve null si la cuenta aun no existia.
    /// </summary>
    public async Task<BalanceAtDate?> GetBalanceAtAsync(Guid accountId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        var stream = await session.Events.FetchStreamAsync(accountId, token: ct);
        if (stream.Count == 0)
        {
            throw new WalletDomainException(WalletError.AccountNotFound, $"No existe la cuenta {accountId}.");
        }

        var history = stream
            .Select(e => e.Data)
            .OfType<WalletEvent>()
            .TakeWhile(e => e.OccurredAt <= asOf)
            .ToList();

        if (history.Count == 0)
        {
            return null;
        }

        var account = WalletAccount.Rehydrate(history);
        return new BalanceAtDate(accountId, asOf, account.Available, account.Reserved, account.Available + account.Reserved, history.Count);
    }

    /// <summary>
    /// Carga, decide, y guarda con concurrencia optimista. Si otro escribio primero (o la conexion fallo),
    /// recarga y reintenta con la MISMA IdempotencyKey, asi un commit incierto nunca duplica la operacion.
    /// </summary>
    private async Task<OperationOutcome> ExecuteAsync(Guid accountId, Func<WalletAccount, OperationOutcome> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan retryDelay;
            var gate = Gates[(accountId.GetHashCode() & int.MaxValue) % GateCount];
            await gate.WaitAsync(ct);
            try
            {
                await using var session = store.LightweightSession();
                var account = await LoadAsync(session, accountId, ct);
                var loadedVersion = account.Version;

                var outcome = operation(account);
                if (outcome.IsDuplicate)
                {
                    return outcome;
                }

                var newEvents = account.UncommittedEvents.ToArray<object>();
                session.Events.Append(accountId, loadedVersion + newEvents.Length, newEvents);
                foreach (var ledger in account.UncommittedEvents.OfType<LedgerEvent>())
                {
                    session.Insert(new IdempotencyRecord
                    {
                        Id = IdempotencyRecord.BuildId(accountId, ledger.IdempotencyKey),
                        AccountId = accountId,
                        Key = ledger.IdempotencyKey,
                        TransactionId = ledger.TransactionId,
                        Fingerprint = ledger.Fingerprint,
                        RecordedAt = ledger.OccurredAt,
                    });
                }

                if (snapshotEvery > 0 && account.Version / snapshotEvery > loadedVersion / snapshotEvery)
                {
                    // Mismo commit que los eventos: el snapshot nunca queda adelantado ni atrasado respecto del stream.
                    session.Store(account.ToSnapshot());
                }

                await session.SaveChangesAsync(ct);
                return outcome;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex) && !ct.IsCancellationRequested)
            {
                retryDelay = BackoffFor(attempt);
            }
            finally
            {
                gate.Release();
            }

            await Task.Delay(retryDelay, ct);
        }
    }

    private static async Task<WalletAccount> LoadAsync(IQuerySession session, Guid accountId, CancellationToken ct)
    {
        var snapshot = await session.LoadAsync<WalletSnapshot>(accountId, ct);
        var fromVersion = snapshot is null ? 0 : snapshot.Version + 1;

        var tail = await session.Events.FetchStreamAsync(accountId, fromVersion: fromVersion, token: ct);
        if (snapshot is null && tail.Count == 0)
        {
            throw new WalletDomainException(WalletError.AccountNotFound, $"No existe la cuenta {accountId}.");
        }

        var events = tail.Select(e => e.Data).OfType<WalletEvent>();
        return snapshot is null ? WalletAccount.Rehydrate(events) : WalletAccount.Restore(snapshot, events);
    }

    private static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromMilliseconds(Random.Shared.Next(5, 20) * Math.Min(attempt, 8));

    /// <summary>Conflictos de version y fallos transitorios de conexion. Los errores de negocio nunca se reintentan.</summary>
    private static bool IsRetryable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is EventStreamUnexpectedMaxEventIdException
                or ConcurrencyException
                or DocumentAlreadyExistsException
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
