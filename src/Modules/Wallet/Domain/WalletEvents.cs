namespace Casino.Modules.Wallet.Domain;

public abstract record WalletEvent(DateTimeOffset OccurredAt);

public sealed record AccountOpened(Guid AccountId, Guid UserId, DateTimeOffset OccurredAt)
    : WalletEvent(OccurredAt);

/// <summary>Evento base de toda operacion que mueve fichas. Un evento por operacion, con todos sus asientos.</summary>
public abstract record LedgerEvent(
    Guid TransactionId,
    string IdempotencyKey,
    IReadOnlyList<Entry> Entries,
    DateTimeOffset OccurredAt) : WalletEvent(OccurredAt);

public sealed record ChipsCredited(
    Guid TransactionId,
    string IdempotencyKey,
    IReadOnlyList<Entry> Entries,
    DateTimeOffset OccurredAt,
    long Amount) : LedgerEvent(TransactionId, IdempotencyKey, Entries, OccurredAt);

public sealed record BetReserved(
    Guid TransactionId,
    string IdempotencyKey,
    IReadOnlyList<Entry> Entries,
    DateTimeOffset OccurredAt,
    Guid ReservationId,
    long Stake) : LedgerEvent(TransactionId, IdempotencyKey, Entries, OccurredAt);

public sealed record BetSettled(
    Guid TransactionId,
    string IdempotencyKey,
    IReadOnlyList<Entry> Entries,
    DateTimeOffset OccurredAt,
    Guid ReservationId,
    long Stake,
    long Payout) : LedgerEvent(TransactionId, IdempotencyKey, Entries, OccurredAt);

public sealed record ReservationReleased(
    Guid TransactionId,
    string IdempotencyKey,
    IReadOnlyList<Entry> Entries,
    DateTimeOffset OccurredAt,
    Guid ReservationId,
    long Stake) : LedgerEvent(TransactionId, IdempotencyKey, Entries, OccurredAt);

public sealed record OperationReversed(
    Guid TransactionId,
    string IdempotencyKey,
    IReadOnlyList<Entry> Entries,
    DateTimeOffset OccurredAt,
    Guid ReversedTransactionId) : LedgerEvent(TransactionId, IdempotencyKey, Entries, OccurredAt);
