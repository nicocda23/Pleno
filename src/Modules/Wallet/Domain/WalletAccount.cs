namespace Casino.Modules.Wallet.Domain;

/// <summary>Resultado de una operacion. Si <see cref="IsDuplicate"/>, la IdempotencyKey ya se habia procesado y no se emitio nada nuevo.</summary>
public readonly record struct OperationOutcome(Guid TransactionId, bool IsDuplicate);

/// <summary>
/// Cuenta de fichas de un usuario (una por usuario), modelada como stream de eventos.
/// El saldo nunca se edita: se deriva de los asientos de los eventos.
/// </summary>
public sealed class WalletAccount
{
    private const int MaxIdempotencyKeyLength = 128;

    private readonly List<WalletEvent> _uncommitted = [];
    private readonly Dictionary<string, ProcessedKey> _processedKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, LedgerRecord> _ledger = [];
    private readonly Dictionary<Guid, long> _openReservations = [];
    private readonly HashSet<Guid> _knownReservations = [];
    private readonly HashSet<Guid> _reversed = [];

    private WalletAccount()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Fichas disponibles para apostar.</summary>
    public long Available { get; private set; }

    /// <summary>Fichas apartadas en apuestas abiertas.</summary>
    public long Reserved { get; private set; }

    /// <summary>Cantidad de eventos aplicados. Base de la concurrencia optimista.</summary>
    public long Version { get; private set; }

    public IReadOnlyCollection<WalletEvent> UncommittedEvents => _uncommitted;

    public IReadOnlyDictionary<Guid, long> OpenReservations => _openReservations;

    public static WalletAccount Open(Guid accountId, Guid userId, DateTimeOffset now)
    {
        var account = new WalletAccount();
        account.Emit(new AccountOpened(accountId, userId, now));
        return account;
    }

    public static WalletAccount Rehydrate(IEnumerable<WalletEvent> history)
    {
        var account = new WalletAccount();
        foreach (var @event in history)
        {
            account.Apply(@event);
        }

        return account;
    }

    /// <summary>Reconstruye la cuenta desde un snapshot y los eventos posteriores a su version.</summary>
    public static WalletAccount Restore(WalletSnapshot snapshot, IEnumerable<WalletEvent> eventsAfterSnapshot)
    {
        var account = new WalletAccount
        {
            Id = snapshot.Id,
            UserId = snapshot.UserId,
            Available = snapshot.Available,
            Reserved = snapshot.Reserved,
            Version = snapshot.Version,
        };

        foreach (var (key, processed) in snapshot.ProcessedKeys)
        {
            account._processedKeys[key] = processed;
        }

        foreach (var record in snapshot.Ledger)
        {
            account._ledger[record.TransactionId] = record;
        }

        foreach (var (reservationId, stake) in snapshot.OpenReservations)
        {
            account._openReservations[reservationId] = stake;
        }

        account._knownReservations.UnionWith(snapshot.KnownReservations);
        account._reversed.UnionWith(snapshot.Reversed);

        foreach (var @event in eventsAfterSnapshot)
        {
            account.Apply(@event);
        }

        return account;
    }

    public WalletSnapshot ToSnapshot() => new()
    {
        Id = Id,
        UserId = UserId,
        Available = Available,
        Reserved = Reserved,
        Version = Version,
        ProcessedKeys = new Dictionary<string, ProcessedKey>(_processedKeys, StringComparer.Ordinal),
        Ledger = [.. _ledger.Values],
        OpenReservations = new Dictionary<Guid, long>(_openReservations),
        KnownReservations = [.. _knownReservations],
        Reversed = [.. _reversed],
    };

    public void MarkCommitted() => _uncommitted.Clear();

    public OperationOutcome Credit(string idempotencyKey, long amount, DateTimeOffset now)
    {
        var fingerprint = $"credit|{amount}";
        if (TryReplay(idempotencyKey, fingerprint, out var replay))
        {
            return replay;
        }

        RequirePositive(amount);
        var transactionId = NewTransactionId();
        var entries = LedgerEntries.Balanced(
            new Entry(LedgerAccountRef.House, -amount),
            new Entry(LedgerAccountRef.Player(UserId), amount));

        Emit(new ChipsCredited(transactionId, idempotencyKey, fingerprint, entries, now, amount));
        return new OperationOutcome(transactionId, false);
    }

    public OperationOutcome Reserve(string idempotencyKey, Guid reservationId, long stake, DateTimeOffset now)
    {
        var fingerprint = $"reserve|{reservationId:N}|{stake}";
        if (TryReplay(idempotencyKey, fingerprint, out var replay))
        {
            return replay;
        }

        RequirePositive(stake);
        if (_knownReservations.Contains(reservationId))
        {
            throw new WalletDomainException(WalletError.ReservationAlreadyExists, $"La reserva {reservationId} ya existe.");
        }

        if (Available < stake)
        {
            throw new WalletDomainException(WalletError.InsufficientFunds, $"Saldo insuficiente: {Available} < {stake}.");
        }

        var transactionId = NewTransactionId();
        var entries = LedgerEntries.Balanced(
            new Entry(LedgerAccountRef.Player(UserId), -stake),
            new Entry(LedgerAccountRef.Reserve(UserId), stake));

        Emit(new BetReserved(transactionId, idempotencyKey, fingerprint, entries, now, reservationId, stake));
        return new OperationOutcome(transactionId, false);
    }

    /// <summary>Liquida una apuesta abierta. <paramref name="payout"/> es el premio total (0 si pierde).</summary>
    public OperationOutcome Settle(string idempotencyKey, Guid reservationId, long payout, DateTimeOffset now)
    {
        var fingerprint = $"settle|{reservationId:N}|{payout}";
        if (TryReplay(idempotencyKey, fingerprint, out var replay))
        {
            return replay;
        }

        if (payout < 0)
        {
            throw new WalletDomainException(WalletError.InvalidAmount, "El premio no puede ser negativo.");
        }

        var stake = RequireOpenReservation(reservationId);
        var transactionId = NewTransactionId();

        var entries = payout == 0
            ? LedgerEntries.Balanced(
                new Entry(LedgerAccountRef.Reserve(UserId), -stake),
                new Entry(LedgerAccountRef.House, stake))
            : LedgerEntries.Balanced(
                new Entry(LedgerAccountRef.Reserve(UserId), -stake),
                new Entry(LedgerAccountRef.House, stake),
                new Entry(LedgerAccountRef.House, -payout),
                new Entry(LedgerAccountRef.Player(UserId), payout));

        Emit(new BetSettled(transactionId, idempotencyKey, fingerprint, entries, now, reservationId, stake, payout));
        return new OperationOutcome(transactionId, false);
    }

    /// <summary>Devuelve al jugador las fichas de una reserva abierta (la jugada se cancelo o expiro).</summary>
    public OperationOutcome Release(string idempotencyKey, Guid reservationId, DateTimeOffset now)
    {
        var fingerprint = $"release|{reservationId:N}";
        if (TryReplay(idempotencyKey, fingerprint, out var replay))
        {
            return replay;
        }

        var stake = RequireOpenReservation(reservationId);
        var transactionId = NewTransactionId();
        var entries = LedgerEntries.Balanced(
            new Entry(LedgerAccountRef.Reserve(UserId), -stake),
            new Entry(LedgerAccountRef.Player(UserId), stake));

        Emit(new ReservationReleased(transactionId, idempotencyKey, fingerprint, entries, now, reservationId, stake));
        return new OperationOutcome(transactionId, false);
    }

    /// <summary>Revierte una transaccion agregando el contraasiento. Nunca borra ni edita historia.</summary>
    public OperationOutcome Reverse(string idempotencyKey, Guid transactionIdToReverse, DateTimeOffset now)
    {
        var fingerprint = $"reverse|{transactionIdToReverse:N}";
        if (TryReplay(idempotencyKey, fingerprint, out var replay))
        {
            return replay;
        }

        if (!_ledger.TryGetValue(transactionIdToReverse, out var original))
        {
            throw new WalletDomainException(WalletError.TransactionNotFound, $"No existe la transaccion {transactionIdToReverse}.");
        }

        if (original.Kind == LedgerKind.Reversal)
        {
            throw new WalletDomainException(WalletError.ReversalNotAllowed, "No se puede revertir una reversa.");
        }

        if (_reversed.Contains(transactionIdToReverse))
        {
            throw new WalletDomainException(WalletError.AlreadyReversed, "La transaccion ya fue revertida.");
        }

        if (original.Kind == LedgerKind.Reserve && !_openReservations.ContainsKey(original.ReservationId!.Value))
        {
            throw new WalletDomainException(
                WalletError.ReversalNotAllowed,
                "La reserva ya fue cerrada: revierta primero su liquidacion o liberacion.");
        }

        var negated = LedgerEntries.Negate(original.Entries);
        var (available, reservedAfter) = Project(negated);
        if (available < 0 || reservedAfter < 0)
        {
            throw new WalletDomainException(WalletError.ReversalWouldOverdraw, "La reversa dejaria saldo negativo.");
        }

        var transactionId = NewTransactionId();
        Emit(new OperationReversed(transactionId, idempotencyKey, fingerprint, negated, now, transactionIdToReverse));
        return new OperationOutcome(transactionId, false);
    }

    private static Guid NewTransactionId() => Guid.CreateVersion7();

    private static void RequirePositive(long amount)
    {
        if (amount <= 0)
        {
            throw new WalletDomainException(WalletError.InvalidAmount, "El monto debe ser un entero positivo de fichas.");
        }
    }

    private bool TryReplay(string idempotencyKey, string fingerprint, out OperationOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw new WalletDomainException(
                WalletError.InvalidIdempotencyKey,
                $"La IdempotencyKey es obligatoria y de hasta {MaxIdempotencyKeyLength} caracteres.");
        }

        if (_processedKeys.TryGetValue(idempotencyKey, out var processed))
        {
            if (!string.Equals(processed.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new WalletDomainException(
                    WalletError.IdempotencyKeyReused,
                    "La IdempotencyKey ya se uso con otro contenido.");
            }

            outcome = new OperationOutcome(processed.TransactionId, true);
            return true;
        }

        outcome = default;
        return false;
    }

    private long RequireOpenReservation(Guid reservationId)
    {
        if (_openReservations.TryGetValue(reservationId, out var stake))
        {
            return stake;
        }

        throw _knownReservations.Contains(reservationId)
            ? new WalletDomainException(WalletError.ReservationNotOpen, $"La reserva {reservationId} ya esta cerrada.")
            : new WalletDomainException(WalletError.ReservationNotFound, $"No existe la reserva {reservationId}.");
    }

    private (long Available, long Reserved) Project(IReadOnlyList<Entry> entries)
    {
        var available = Available;
        var reserved = Reserved;
        foreach (var entry in entries)
        {
            if (entry.Account == LedgerAccountRef.Player(UserId))
            {
                available = checked(available + entry.Amount);
            }
            else if (entry.Account == LedgerAccountRef.Reserve(UserId))
            {
                reserved = checked(reserved + entry.Amount);
            }
        }

        return (available, reserved);
    }

    private void Emit(WalletEvent @event)
    {
        Apply(@event);
        _uncommitted.Add(@event);
    }

    private void Apply(WalletEvent @event)
    {
        Version++;
        switch (@event)
        {
            case AccountOpened opened:
                Id = opened.AccountId;
                UserId = opened.UserId;
                break;

            case LedgerEvent ledger:
                ApplyLedger(ledger);
                break;

            default:
                throw new InvalidOperationException($"Evento desconocido: {@event.GetType().Name}");
        }
    }

    private void ApplyLedger(LedgerEvent ledger)
    {
        (Available, Reserved) = Project(ledger.Entries);
        _processedKeys[ledger.IdempotencyKey] = new ProcessedKey(ledger.TransactionId, ledger.Fingerprint);
        _ledger[ledger.TransactionId] = ToRecord(ledger);

        switch (ledger)
        {
            case BetReserved reserved:
                _knownReservations.Add(reserved.ReservationId);
                _openReservations[reserved.ReservationId] = reserved.Stake;
                break;

            case BetSettled settled:
                _openReservations.Remove(settled.ReservationId);
                break;

            case ReservationReleased released:
                _openReservations.Remove(released.ReservationId);
                break;

            case OperationReversed reversal:
                _reversed.Add(reversal.ReversedTransactionId);
                ApplyReversalEffects(_ledger[reversal.ReversedTransactionId]);
                break;
        }
    }

    private void ApplyReversalEffects(LedgerRecord original)
    {
        switch (original.Kind)
        {
            case LedgerKind.Reserve:
                _openReservations.Remove(original.ReservationId!.Value);
                break;

            case LedgerKind.Settle or LedgerKind.Release:
                _openReservations[original.ReservationId!.Value] = original.Stake;
                break;
        }
    }

    private static LedgerRecord ToRecord(LedgerEvent ledger) => ledger switch
    {
        ChipsCredited => new LedgerRecord(ledger.TransactionId, LedgerKind.Credit, ledger.Entries, null, 0),
        BetReserved r => new LedgerRecord(ledger.TransactionId, LedgerKind.Reserve, ledger.Entries, r.ReservationId, r.Stake),
        BetSettled s => new LedgerRecord(ledger.TransactionId, LedgerKind.Settle, ledger.Entries, s.ReservationId, s.Stake),
        ReservationReleased r => new LedgerRecord(ledger.TransactionId, LedgerKind.Release, ledger.Entries, r.ReservationId, r.Stake),
        OperationReversed => new LedgerRecord(ledger.TransactionId, LedgerKind.Reversal, ledger.Entries, null, 0),
        _ => throw new InvalidOperationException($"Evento de ledger desconocido: {ledger.GetType().Name}"),
    };
}
