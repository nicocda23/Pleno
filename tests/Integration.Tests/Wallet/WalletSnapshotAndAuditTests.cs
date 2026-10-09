using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Casino.Integration.Tests.Wallet;

[Collection(WalletDbDefinition.Name)]
public class WalletSnapshotAndAuditTests(PostgresFixture db)
{
    [Fact]
    public async Task Snapshot_is_taken_at_the_interval_and_loading_from_it_matches_a_full_replay()
    {
        var wallet = new WalletService(db.Store, TimeProvider.System, snapshotEvery: 10);
        var accountId = await wallet.OpenAccountAsync(Guid.NewGuid());
        await wallet.CreditAsync(accountId, "initial-credit", 1_000);

        var reservations = new List<(Guid ReservationId, OperationOutcome Outcome)>();
        for (var i = 0; i < 45; i++)
        {
            var reservationId = Guid.NewGuid();
            reservations.Add((reservationId, await wallet.ReserveAsync(accountId, $"r-{i}", reservationId, 1)));
        }

        await using var session = db.Store.QuerySession();
        var snapshot = await session.LoadAsync<WalletSnapshot>(accountId);
        Assert.NotNull(snapshot);
        Assert.Equal(40, snapshot.Version);

        var fromSnapshot = await wallet.GetAsync(accountId);
        var fullHistory = (await session.Events.FetchStreamAsync(accountId)).Select(e => e.Data).OfType<WalletEvent>();
        var fromReplay = WalletAccount.Rehydrate(fullHistory);

        Assert.Equal(fromReplay.Version, fromSnapshot.Version);
        Assert.Equal(fromReplay.Available, fromSnapshot.Available);
        Assert.Equal(fromReplay.Reserved, fromSnapshot.Reserved);
        Assert.Equal(fromReplay.OpenReservations.OrderBy(kv => kv.Key), fromSnapshot.OpenReservations.OrderBy(kv => kv.Key));
        Assert.Equal(955, fromSnapshot.Available);
    }

    [Fact]
    public async Task Idempotency_and_reversals_keep_working_across_a_snapshot()
    {
        var wallet = new WalletService(db.Store, TimeProvider.System, snapshotEvery: 5);
        var accountId = await wallet.OpenAccountAsync(Guid.NewGuid());
        await wallet.CreditAsync(accountId, "initial-credit", 1_000);

        var firstReservation = Guid.NewGuid();
        var first = await wallet.ReserveAsync(accountId, "bet-0", firstReservation, 100);
        for (var i = 1; i < 10; i++)
        {
            await wallet.ReserveAsync(accountId, $"bet-{i}", Guid.NewGuid(), 1);
        }

        // Estas dos cosas dependen de lo que el snapshot recuerda: claves procesadas y transacciones revertibles.
        var again = await wallet.ReserveAsync(accountId, "bet-0", firstReservation, 100);
        Assert.True(again.IsDuplicate);
        Assert.Equal(first.TransactionId, again.TransactionId);

        await wallet.ReverseAsync(accountId, "undo-bet-0", first.TransactionId);
        var account = await wallet.GetAsync(accountId);
        Assert.Equal(1_000 - 9, account.Available);
        Assert.DoesNotContain(firstReservation, account.OpenReservations.Keys);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_with_different_content_is_rejected()
    {
        var accountId = await db.Wallet.OpenAccountAsync(Guid.NewGuid());
        await db.Wallet.CreditAsync(accountId, "credit-1", 100);

        var error = await Assert.ThrowsAsync<WalletDomainException>(() => db.Wallet.CreditAsync(accountId, "credit-1", 200));

        Assert.Equal(WalletError.IdempotencyKeyReused, error.Error);
        Assert.Equal(100, (await db.Wallet.GetAsync(accountId)).Available);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(2)]
    public async Task Balance_can_be_rebuilt_at_any_past_date(int snapshotEvery)
    {
        var start = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(start);
        var wallet = new WalletService(db.Store, time, snapshotEvery: snapshotEvery);
        var reservationId = Guid.NewGuid();

        var accountId = await wallet.OpenAccountAsync(Guid.NewGuid());          // 09:00
        time.Advance(TimeSpan.FromHours(1));
        await wallet.CreditAsync(accountId, "credit", 1_000);                    // 10:00
        time.Advance(TimeSpan.FromHours(1));
        await wallet.ReserveAsync(accountId, "reserve", reservationId, 100);     // 11:00
        time.Advance(TimeSpan.FromHours(1));
        await wallet.SettleAsync(accountId, "settle", reservationId, 300);       // 12:00

        Assert.Null(await wallet.GetBalanceAtAsync(accountId, start.AddMinutes(-1)));

        var opened = await wallet.GetBalanceAtAsync(accountId, start.AddMinutes(30));
        Assert.Equal((0, 0, 1), (opened!.Available, opened.Reserved, opened.EventsApplied));

        var funded = await wallet.GetBalanceAtAsync(accountId, start.AddMinutes(90));
        Assert.Equal((1_000, 0), (funded!.Available, funded.Reserved));

        var reserved = await wallet.GetBalanceAtAsync(accountId, start.AddHours(2));   // limite inclusivo
        Assert.Equal((900, 100, 1_000), (reserved!.Available, reserved.Reserved, reserved.Total));

        var settled = await wallet.GetBalanceAtAsync(accountId, start.AddHours(5));
        Assert.Equal((1_200, 0, 1_200), (settled!.Available, settled.Reserved, settled.Total));
    }

    [Fact]
    public async Task Balance_of_an_unknown_account_is_not_found()
    {
        var error = await Assert.ThrowsAsync<WalletDomainException>(() =>
            db.Wallet.GetBalanceAtAsync(Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal(WalletError.AccountNotFound, error.Error);
    }
}
