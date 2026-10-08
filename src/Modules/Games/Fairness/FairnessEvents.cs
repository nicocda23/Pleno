namespace Casino.Modules.Games.Fairness;

public abstract record FairnessEvent(DateTimeOffset OccurredAt);

/// <summary>
/// Empieza un par de seeds. La server seed viaja SOLO cifrada: aca se guarda el compromiso (publico)
/// y el texto cifrado (que solo el servidor puede abrir).
/// </summary>
public sealed record SeedPairStarted(
    Guid UserId,
    Guid PairId,
    string Commitment,
    string EncryptedServerSeed,
    string ClientSeed,
    DateTimeOffset OccurredAt) : FairnessEvent(OccurredAt);

/// <summary>El servidor asigna el nonce a una apuesta. Queda registrado quien uso cual: auditable.</summary>
public sealed record NonceAllocated(Guid PairId, Guid BetId, long Nonce, DateTimeOffset OccurredAt) : FairnessEvent(OccurredAt);

/// <summary>La apuesta ya se resolvio (o se anulo): su resultado deja de ser secreto.</summary>
public sealed record BetCompleted(Guid PairId, Guid BetId, DateTimeOffset OccurredAt) : FairnessEvent(OccurredAt);

/// <summary>Cierra el par activo, revela su server seed y empieza uno nuevo con nonce 0.</summary>
public sealed record SeedPairRotated(
    Guid RetiredPairId,
    string RevealedServerSeed,
    Guid NewPairId,
    string NewCommitment,
    string NewEncryptedServerSeed,
    string NewClientSeed,
    DateTimeOffset OccurredAt) : FairnessEvent(OccurredAt);
