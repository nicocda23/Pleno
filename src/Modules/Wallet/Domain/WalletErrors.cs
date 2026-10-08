namespace Casino.Modules.Wallet.Domain;

public enum WalletError
{
    InvalidAmount,
    InvalidIdempotencyKey,
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
