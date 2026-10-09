namespace Casino.Modules.Games.Fairness;

public sealed record ActiveSeedPair(Guid PairId, string Commitment, string EncryptedServerSeed, string ClientSeed, long NextNonce);

public sealed record RetiredSeedPair(Guid PairId, string Commitment, string RevealedServerSeed, string ClientSeed, long BetsPlayed);

/// <summary>Nonce asignado a una apuesta. <see cref="AlreadyAllocated"/> indica que era un reintento de la misma apuesta.</summary>
public sealed record NonceAllocation(Guid PairId, long Nonce, string Commitment, string ClientSeed, bool AlreadyAllocated);

/// <summary>Con que seeds y nonce se sortea una apuesta. Solo uno de los dos campos de seed viene informado.</summary>
public sealed record DrawContext(Guid PairId, long Nonce, string ClientSeed, string? EncryptedServerSeed, string? RevealedServerSeed);

/// <summary>
/// Par de seeds de un usuario (un stream por usuario). El servidor asigna los nonces; el cliente no puede forzar ningun valor:
/// ni el nonce, ni cambiar la client seed a mitad de camino (solo al rotar, que revela la server seed anterior).
/// </summary>
public sealed class FairnessAccount
{
    private const int MaxClientSeedLength = 64;

    private readonly List<FairnessEvent> _uncommitted = [];
    private readonly Dictionary<Guid, (Guid PairId, long Nonce)> _allocations = [];
    private readonly HashSet<Guid> _pending = [];
    private readonly List<RetiredSeedPair> _retired = [];
    private ActiveSeedPair? _active;

    private FairnessAccount()
    {
    }

    public Guid UserId { get; private set; }

    public long Version { get; private set; }

    public ActiveSeedPair Active => _active ?? throw new InvalidOperationException("La cuenta no esta iniciada.");

    public IReadOnlyCollection<RetiredSeedPair> Retired => _retired;

    public int PendingBets => _pending.Count;

    public IReadOnlyCollection<FairnessEvent> UncommittedEvents => _uncommitted;

    public static FairnessAccount Start(Guid userId, Guid pairId, string commitment, string encryptedServerSeed, string clientSeed, DateTimeOffset now)
    {
        RequireClientSeed(clientSeed);
        RequireCommitment(commitment);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedServerSeed);

        var account = new FairnessAccount();
        account.Emit(new SeedPairStarted(userId, pairId, commitment, encryptedServerSeed, clientSeed, now));
        return account;
    }

    public static FairnessAccount Rehydrate(IEnumerable<FairnessEvent> history)
    {
        var account = new FairnessAccount();
        foreach (var @event in history)
        {
            account.Apply(@event);
        }

        return account;
    }

    /// <summary>Asigna el siguiente nonce a la apuesta. Es idempotente por apuesta: un reintento devuelve el mismo nonce.</summary>
    public NonceAllocation AllocateNonce(Guid betId, DateTimeOffset now)
    {
        if (_allocations.TryGetValue(betId, out var existing))
        {
            var (commitment, clientSeed) = PairInfo(existing.PairId);
            return new NonceAllocation(existing.PairId, existing.Nonce, commitment, clientSeed, AlreadyAllocated: true);
        }

        var active = Active;
        Emit(new NonceAllocated(active.PairId, betId, active.NextNonce, now));
        return new NonceAllocation(active.PairId, active.NextNonce, active.Commitment, active.ClientSeed, AlreadyAllocated: false);
    }

    /// <summary>Marca la apuesta como resuelta. Idempotente: si ya estaba cerrada no hace nada.</summary>
    public void CompleteBet(Guid betId, DateTimeOffset now)
    {
        if (!_allocations.TryGetValue(betId, out var allocation))
        {
            throw new FairnessDomainException(FairnessError.BetNotFound, $"No hay un nonce asignado a la apuesta {betId}.");
        }

        if (_pending.Contains(betId))
        {
            Emit(new BetCompleted(allocation.PairId, betId, now));
        }
    }

    /// <summary>Datos para sortear una apuesta ya asignada: su nonce y la seed del par al que pertenece.</summary>
    public DrawContext GetDrawContext(Guid betId)
    {
        if (!_allocations.TryGetValue(betId, out var allocation))
        {
            throw new FairnessDomainException(FairnessError.BetNotFound, $"No hay un nonce asignado a la apuesta {betId}.");
        }

        if (_active is not null && _active.PairId == allocation.PairId)
        {
            return new DrawContext(allocation.PairId, allocation.Nonce, _active.ClientSeed, _active.EncryptedServerSeed, null);
        }

        var retired = _retired.Single(p => p.PairId == allocation.PairId);
        return new DrawContext(allocation.PairId, allocation.Nonce, retired.ClientSeed, null, retired.RevealedServerSeed);
    }

    /// <summary>
    /// Rota el par: revela la server seed activa (la que el servicio descifro) y empieza uno nuevo.
    /// No se permite con apuestas pendientes: revelar la seed expondria el resultado de una jugada en curso.
    /// </summary>
    public void Rotate(
        string revealedServerSeed,
        Guid newPairId,
        string newCommitment,
        string newEncryptedServerSeed,
        string newClientSeed,
        DateTimeOffset now)
    {
        if (_pending.Count > 0)
        {
            throw new FairnessDomainException(FairnessError.PendingBets, $"Hay {_pending.Count} apuesta(s) sin resolver: no se puede rotar.");
        }

        var active = Active;
        if (!ProvablyFair.MatchesCommitment(revealedServerSeed, active.Commitment))
        {
            throw new FairnessDomainException(FairnessError.SeedDoesNotMatchCommitment, "La seed a revelar no coincide con el compromiso publicado.");
        }

        RequireClientSeed(newClientSeed);
        RequireCommitment(newCommitment);
        ArgumentException.ThrowIfNullOrWhiteSpace(newEncryptedServerSeed);

        Emit(new SeedPairRotated(active.PairId, revealedServerSeed, newPairId, newCommitment, newEncryptedServerSeed, newClientSeed, now));
    }

    private static void RequireClientSeed(string clientSeed)
    {
        if (string.IsNullOrWhiteSpace(clientSeed) || clientSeed.Length > MaxClientSeedLength || clientSeed.Any(char.IsControl))
        {
            throw new FairnessDomainException(
                FairnessError.InvalidClientSeed,
                $"La client seed debe tener entre 1 y {MaxClientSeedLength} caracteres, sin caracteres de control.");
        }
    }

    private static void RequireCommitment(string commitment)
    {
        if (commitment is not { Length: 64 } || !commitment.All(char.IsAsciiHexDigitLower))
        {
            throw new FairnessDomainException(FairnessError.InvalidCommitment, "El compromiso debe ser un SHA-256 en hex minuscula (64 caracteres).");
        }
    }

    private (string Commitment, string ClientSeed) PairInfo(Guid pairId)
    {
        if (_active is not null && _active.PairId == pairId)
        {
            return (_active.Commitment, _active.ClientSeed);
        }

        var retired = _retired.Single(p => p.PairId == pairId);
        return (retired.Commitment, retired.ClientSeed);
    }

    private void Emit(FairnessEvent @event)
    {
        Apply(@event);
        _uncommitted.Add(@event);
    }

    private void Apply(FairnessEvent @event)
    {
        Version++;
        switch (@event)
        {
            case SeedPairStarted started:
                UserId = started.UserId;
                _active = new ActiveSeedPair(started.PairId, started.Commitment, started.EncryptedServerSeed, started.ClientSeed, 0);
                break;

            case NonceAllocated allocated:
                _allocations[allocated.BetId] = (allocated.PairId, allocated.Nonce);
                _pending.Add(allocated.BetId);
                _active = Active with { NextNonce = allocated.Nonce + 1 };
                break;

            case BetCompleted completed:
                _pending.Remove(completed.BetId);
                break;

            case SeedPairRotated rotated:
                _retired.Add(new RetiredSeedPair(
                    rotated.RetiredPairId, Active.Commitment, rotated.RevealedServerSeed, Active.ClientSeed, Active.NextNonce));
                _active = new ActiveSeedPair(rotated.NewPairId, rotated.NewCommitment, rotated.NewEncryptedServerSeed, rotated.NewClientSeed, 0);
                break;

            default:
                throw new InvalidOperationException($"Evento desconocido: {@event.GetType().Name}");
        }
    }
}
