using JasperFx;
using Marten;
using Marten.Exceptions;

namespace Casino.Modules.Wallet.Application;

/// <summary>
/// Registro de auditoria de las acciones del personal de backoffice sobre las cuentas: quien, a quien, cuanto y cuando.
/// Solo guarda ids (nunca nombre ni email del administrador ni del jugador). Es de solo-agregar: no se edita ni se borra.
/// </summary>
public sealed class BackofficeAudit(IDocumentStore store, TimeProvider clock)
{
    public const string ChipsCreditedAction = "ChipsCredited";

    /// <summary>
    /// Anota una carga de fichas. Es idempotente: la misma carga (misma cuenta y misma clave) se anota una sola vez, asi que se puede
    /// llamar tambien cuando la carga resulto un duplicado. Eso sirve para curar un fallo entre la carga y su anotacion: al reintentar con
    /// la misma clave, la carga se detecta como duplicada y la anotacion que faltaba se completa.
    /// </summary>
    public async Task RecordCreditAsync(
        Guid actorUserId, Guid targetUserId, Guid accountId, long amount, string idempotencyKey, Guid transactionId, CancellationToken ct = default)
    {
        var entry = new AuditEntry
        {
            Id = AuditEntry.BuildId(ChipsCreditedAction, accountId, idempotencyKey),
            Action = ChipsCreditedAction,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            AccountId = accountId,
            Amount = amount,
            IdempotencyKey = idempotencyKey,
            TransactionId = transactionId,
            OccurredAt = clock.GetUtcNow(),
        };

        try
        {
            await using var session = store.LightweightSession();
            session.Insert(entry);
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DocumentAlreadyExistsException || ex.InnerException is DocumentAlreadyExistsException)
        {
            // Ya estaba anotada: el original se conserva tal cual.
        }
    }

    /// <summary>Las anotaciones mas recientes primero.</summary>
    public async Task<IReadOnlyList<AuditEntry>> ListAsync(int limit, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        return await session.Query<AuditEntry>().OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(ct);
    }
}

public sealed class AuditEntry
{
    public required string Id { get; init; }

    public required string Action { get; init; }

    public required Guid ActorUserId { get; init; }

    public required Guid TargetUserId { get; init; }

    public required Guid AccountId { get; init; }

    public required long Amount { get; init; }

    public required string IdempotencyKey { get; init; }

    public required Guid TransactionId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public static string BuildId(string action, Guid accountId, string key) => $"{action}:{accountId:N}:{key}";
}
