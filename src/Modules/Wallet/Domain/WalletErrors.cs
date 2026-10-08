namespace Casino.Modules.Wallet.Domain;

public enum WalletError
{
    AccountNotFound,
    InvalidAmount,
    InvalidIdempotencyKey,
    IdempotencyKeyReused,
    InsufficientFunds,
    ReservationAlreadyExists,
    ReservationNotFound,
    ReservationNotOpen,
    TransactionNotFound,
    AlreadyReversed,
    ReversalNotAllowed,
    ReversalWouldOverdraw,
}

public sealed class WalletDomainException(WalletError error, string message) : Exception(message)
{
    public WalletError Error { get; } = error;
}
