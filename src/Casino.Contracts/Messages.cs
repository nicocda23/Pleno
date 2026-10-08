namespace Casino.Contracts;

// Contratos de mensajeria entre modulos: el unico "idioma" que comparten. Sin logica ni dependencias.
// Convencion: las ordenes (comandos) estan en imperativo; los hechos (eventos), en pasado.
// BetId identifica la apuesta de punta a punta y es tambien el id de la reserva en la Wallet.

// ---- Ordenes hacia la Wallet ----

/// <summary>Un juego pide reservar las fichas de una apuesta.</summary>
public sealed record ReserveStake(Guid BetId, Guid AccountId, long Stake);

/// <summary>Un juego informa que la ronda se resolvio. La Wallet liquida con el premio TOTAL (0 si perdio).</summary>
public sealed record RoundResolved(Guid BetId, Guid AccountId, long Payout);

// ---- Hechos que publica la Wallet ----

public sealed record StakeReserved(Guid BetId, Guid AccountId, long Stake, Guid TransactionId);

public sealed record StakeRejected(Guid BetId, Guid AccountId, string Reason);

public sealed record StakeSettled(Guid BetId, Guid AccountId, long Stake, long Payout, Guid TransactionId);

public sealed record StakeReleased(Guid BetId, Guid AccountId, long Stake, Guid TransactionId);

/// <summary>La liquidacion no se pudo aplicar (por ejemplo, la reserva ya habia vencido). El juego debe anular la ronda.</summary>
public sealed record StakeSettlementRejected(Guid BetId, Guid AccountId, string Reason);

/// <summary>Cambio de saldo de una cuenta, para quien necesite mostrarlo en vivo.</summary>
public sealed record BalanceChanged(Guid AccountId, long Available, long Reserved, long Version);

/// <summary>Mensaje programado por la Wallet para si misma: si la reserva sigue abierta al vencer, se libera.</summary>
public sealed record ExpireReservation(Guid BetId, Guid AccountId);

// ---- Usuarios ----

/// <summary>Un jugador entro por primera vez. La Wallet abre su cuenta (el id se deriva del usuario) y le acredita las fichas de bienvenida.</summary>
public sealed record UserRegistered(Guid UserId);
