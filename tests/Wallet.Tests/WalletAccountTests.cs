using Casino.Modules.Wallet.Domain;

namespace Casino.Wallet.Tests;

public class WalletAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static WalletAccount NewAccount() => WalletAccount.Open(Guid.NewGuid(), Guid.NewGuid(), Now);

    private static WalletAccount FundedAccount(long chips)
    {
        var account = NewAccount();
        account.Credit("fund", chips, Now);
        return account;
    }

    private static WalletError ErrorOf(Action action) =>
        Assert.Throws<WalletDomainException>(action).Error;

    // --- Credit

    [Fact]
    public void Credit_increases_available_balance()
    {
        var account = NewAccount();

        account.Credit("k1", 500, Now);

        Assert.Equal(500, account.Available);
        Assert.Equal(0, account.Reserved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Credit_rejects_non_positive_amounts(long amount)
    {
        var account = NewAccount();

        Assert.Equal(WalletError.InvalidAmount, ErrorOf(() => account.Credit("k1", amount, Now)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Operations_require_an_idempotency_key(string key)
    {
        var account = NewAccount();

        Assert.Equal(WalletError.InvalidIdempotencyKey, ErrorOf(() => account.Credit(key, 10, Now)));
    }

    // --- Reserve

    [Fact]
    public void Reserve_moves_chips_from_available_to_reserved()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();

        account.Reserve("bet-1", reservationId, 100, Now);

        Assert.Equal(900, account.Available);
        Assert.Equal(100, account.Reserved);
        Assert.Equal(100, account.OpenReservations[reservationId]);
    }

    [Fact]
    public void Reserve_fails_when_balance_is_insufficient()
    {
        var account = FundedAccount(50);

        Assert.Equal(WalletError.InsufficientFunds, ErrorOf(() => account.Reserve("bet-1", Guid.NewGuid(), 51, Now)));
        Assert.Equal(50, account.Available);
    }

    [Fact]
    public void Reserve_rejects_reusing_a_reservation_id()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet-1", reservationId, 100, Now);

        Assert.Equal(WalletError.ReservationAlreadyExists, ErrorOf(() => account.Reserve("bet-2", reservationId, 100, Now)));
    }

    // --- Settle

    [Fact]
    public void Settle_with_win_pays_the_prize_to_the_player()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet-1", reservationId, 100, Now);

        account.Settle("settle-1", reservationId, 200, Now);

        Assert.Equal(1100, account.Available);
        Assert.Equal(0, account.Reserved);
        Assert.Empty(account.OpenReservations);
    }

    [Fact]
    public void Settle_with_loss_keeps_the_stake_in_the_house()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet-1", reservationId, 100, Now);

        account.Settle("settle-1", reservationId, 0, Now);

        Assert.Equal(900, account.Available);
        Assert.Equal(0, account.Reserved);
    }

    [Fact]
    public void Settle_cannot_close_a_reservation_twice()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet-1", reservationId, 100, Now);
        account.Settle("settle-1", reservationId, 0, Now);

        Assert.Equal(WalletError.ReservationNotOpen, ErrorOf(() => account.Settle("settle-2", reservationId, 0, Now)));
    }

    [Fact]
    public void Settle_fails_for_unknown_reservation()
    {
        var account = FundedAccount(1000);

        Assert.Equal(WalletError.ReservationNotFound, ErrorOf(() => account.Settle("s", Guid.NewGuid(), 0, Now)));
    }

    // --- Release (compensacion de la saga)

    [Fact]
    public void Release_returns_the_stake_to_the_player()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet-1", reservationId, 300, Now);

        account.Release("release-1", reservationId, Now);

        Assert.Equal(1000, account.Available);
        Assert.Equal(0, account.Reserved);
    }

    // --- Idempotencia

    [Fact]
    public void Repeated_idempotency_key_returns_the_original_result_without_new_events()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        var first = account.Reserve("bet-1", reservationId, 100, Now);
        var eventsBefore = account.UncommittedEvents.Count;

        var second = account.Reserve("bet-1", reservationId, 100, Now);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.TransactionId, second.TransactionId);
        Assert.Equal(eventsBefore, account.UncommittedEvents.Count);
        Assert.Equal(900, account.Available);
    }

    // --- Reverse

    [Fact]
    public void Reverse_of_a_credit_removes_the_chips()
    {
        var account = NewAccount();
        var credit = account.Credit("c1", 500, Now);

        account.Reverse("rev-1", credit.TransactionId, Now);

        Assert.Equal(0, account.Available);
    }

    [Fact]
    public void Reverse_cannot_overdraw_the_account()
    {
        var account = NewAccount();
        var credit = account.Credit("c1", 500, Now);
        account.Reserve("bet-1", Guid.NewGuid(), 400, Now);

        Assert.Equal(WalletError.ReversalWouldOverdraw, ErrorOf(() => account.Reverse("rev-1", credit.TransactionId, Now)));
        Assert.Equal(100, account.Available);
    }

    [Fact]
    public void Reverse_cannot_be_applied_twice_or_to_a_reversal()
    {
        var account = NewAccount();
        var credit = account.Credit("c1", 500, Now);
        var reversal = account.Reverse("rev-1", credit.TransactionId, Now);

        Assert.Equal(WalletError.AlreadyReversed, ErrorOf(() => account.Reverse("rev-2", credit.TransactionId, Now)));
        Assert.Equal(WalletError.ReversalNotAllowed, ErrorOf(() => account.Reverse("rev-3", reversal.TransactionId, Now)));
    }

    [Fact]
    public void Reverse_of_a_settlement_reopens_the_reservation_and_takes_back_the_prize()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet-1", reservationId, 100, Now);
        var settle = account.Settle("settle-1", reservationId, 250, Now);

        account.Reverse("rev-1", settle.TransactionId, Now);

        Assert.Equal(900, account.Available);
        Assert.Equal(100, account.Reserved);
        Assert.Contains(reservationId, account.OpenReservations.Keys);
    }

    [Fact]
    public void Reverse_of_a_reservation_requires_it_to_be_still_open()
    {
        var account = FundedAccount(1000);
        var reservationId = Guid.NewGuid();
        var reserve = account.Reserve("bet-1", reservationId, 100, Now);
        account.Settle("settle-1", reservationId, 0, Now);

        Assert.Equal(WalletError.ReversalNotAllowed, ErrorOf(() => account.Reverse("rev-1", reserve.TransactionId, Now)));
    }

    [Fact]
    public void Reverse_fails_for_unknown_transaction()
    {
        var account = NewAccount();

        Assert.Equal(WalletError.TransactionNotFound, ErrorOf(() => account.Reverse("rev-1", Guid.NewGuid(), Now)));
    }

    // --- Event sourcing e invariantes

    [Fact]
    public void Account_state_is_rebuilt_from_its_events()
    {
        var account = FundedAccount(1000);
        var open = Guid.NewGuid();
        var closed = Guid.NewGuid();
        account.Reserve("b1", open, 100, Now);
        account.Reserve("b2", closed, 200, Now);
        account.Settle("s2", closed, 500, Now);

        var rebuilt = WalletAccount.Rehydrate(account.UncommittedEvents);

        Assert.Equal(account.Id, rebuilt.Id);
        Assert.Equal(account.Available, rebuilt.Available);
        Assert.Equal(account.Reserved, rebuilt.Reserved);
        Assert.Equal(account.Version, rebuilt.Version);
        Assert.Equal(account.OpenReservations, rebuilt.OpenReservations);
        Assert.True(rebuilt.Reserve("b1", open, 100, Now).IsDuplicate);
    }

    [Fact]
    public void Every_transaction_sums_to_zero_after_random_operations()
    {
        var random = new Random(12345);
        var account = FundedAccount(10_000);
        var openReservations = new List<Guid>();

        for (var i = 0; i < 2000; i++)
        {
            try
            {
                switch (random.Next(5))
                {
                    case 0:
                        account.Credit($"credit-{i}", random.Next(1, 500), Now);
                        break;
                    case 1:
                        var id = Guid.NewGuid();
                        account.Reserve($"reserve-{i}", id, random.Next(1, 800), Now);
                        openReservations.Add(id);
                        break;
                    case 2 when openReservations.Count > 0:
                        var toSettle = openReservations[random.Next(openReservations.Count)];
                        account.Settle($"settle-{i}", toSettle, random.Next(0, 1500), Now);
                        openReservations.Remove(toSettle);
                        break;
                    case 3 when openReservations.Count > 0:
                        var toRelease = openReservations[random.Next(openReservations.Count)];
                        account.Release($"release-{i}", toRelease, Now);
                        openReservations.Remove(toRelease);
                        break;
                    case 4:
                        var ledger = account.UncommittedEvents.OfType<LedgerEvent>().ToList();
                        account.Reverse($"reverse-{i}", ledger[random.Next(ledger.Count)].TransactionId, Now);
                        break;
                }
            }
            catch (WalletDomainException)
            {
                // Rechazos validos del dominio: el estado no debe haber cambiado.
            }
        }

        var ledgerEvents = account.UncommittedEvents.OfType<LedgerEvent>().ToList();
        Assert.All(ledgerEvents, e => Assert.Equal(0, e.Entries.Sum(entry => entry.Amount)));
        Assert.True(account.Available >= 0);
        Assert.True(account.Reserved >= 0);
        Assert.Equal(account.OpenReservations.Values.Sum(), account.Reserved);

        var houseNet = ledgerEvents.SelectMany(e => e.Entries)
            .Where(entry => entry.Account.Kind == LedgerAccountKind.House)
            .Sum(entry => entry.Amount);
        Assert.Equal(-(account.Available + account.Reserved), houseNet);

        var rebuilt = WalletAccount.Rehydrate(account.UncommittedEvents);
        Assert.Equal(account.Available, rebuilt.Available);
        Assert.Equal(account.Reserved, rebuilt.Reserved);
    }
}
