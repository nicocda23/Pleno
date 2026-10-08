using Marten.Schema;

namespace Casino.Modules.Games.Roulette;

public enum RoundStatus
{
    /// <summary>La apuesta se coloco: nonce asignado y orden de reserva encolada.</summary>
    Placed = 1,

    /// <summary>La Wallet reservo las fichas y el juego ya sorteo. Falta que la Wallet liquide.</summary>
    Resolved = 2,

    /// <summary>La Wallet liquido el premio. Estado final.</summary>
    Settled = 3,

    /// <summary>La Wallet rechazo la reserva (por ejemplo, saldo insuficiente). Estado final: nunca se jugo.</summary>
    Rejected = 4,

    /// <summary>La ronda se anulo y las fichas volvieron al jugador (vencio la reserva o la liquidacion fue rechazada). Estado final.</summary>
    Voided = 5,
}

/// <summary>
/// Una apuesta de ruleta de punta a punta. Cada transicion es idempotente: aplicar dos veces el mismo mensaje
/// (entrega "al menos una vez") deja la ronda igual y devuelve false.
/// </summary>
public sealed class RouletteRound
{
    /// <summary>Id de la apuesta (BetId): tambien es el id de la reserva en la Wallet.</summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    public RouletteBetType BetType { get; set; }

    public List<int> Selection { get; set; } = [];

    public long Stake { get; set; }

    /// <summary>Par de seeds y nonce asignados por el servidor al colocar la apuesta (antes de conocer el resultado).</summary>
    public Guid PairId { get; set; }

    public long Nonce { get; set; }

    public RoundStatus Status { get; set; }

    public int? WinningNumber { get; set; }

    public long? Payout { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset PlacedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Control de concurrencia optimista: dos manejadores simultaneos no pueden pisarse.</summary>
    [Version]
    public Guid Version { get; set; }

    public bool IsClosed => Status is RoundStatus.Settled or RoundStatus.Rejected or RoundStatus.Voided;

    public RouletteBet ToBet() => RouletteBet.Create(BetType, Selection, Stake);

    public bool MarkResolved(int winningNumber, long payout)
    {
        if (Status != RoundStatus.Placed)
        {
            return false;
        }

        Status = RoundStatus.Resolved;
        WinningNumber = winningNumber;
        Payout = payout;
        return true;
    }

    public bool MarkSettled(DateTimeOffset now)
    {
        if (Status != RoundStatus.Resolved)
        {
            return false;
        }

        Status = RoundStatus.Settled;
        ClosedAt = now;
        return true;
    }

    public bool MarkRejected(string reason, DateTimeOffset now)
    {
        if (Status != RoundStatus.Placed)
        {
            return false;
        }

        Status = RoundStatus.Rejected;
        FailureReason = reason;
        ClosedAt = now;
        return true;
    }

    /// <summary>Anula la ronda. El resultado sorteado (si lo hubo) se conserva para auditoria pero no paga.</summary>
    public bool MarkVoided(string reason, DateTimeOffset now)
    {
        if (Status is not (RoundStatus.Placed or RoundStatus.Resolved))
        {
            return false;
        }

        Status = RoundStatus.Voided;
        FailureReason = reason;
        ClosedAt = now;
        return true;
    }
}
