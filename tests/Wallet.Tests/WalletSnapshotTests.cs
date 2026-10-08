using Casino.Modules.Wallet.Domain;

namespace Casino.Wallet.Tests;

public class WalletSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Restored_account_behaves_like_the_original()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        var credit = account.Credit("credit", 1_000, Now);
        var reservationId = Guid.NewGuid();
        account.Reserve("bet", reservationId, 100, Now);
        var snapshot = account.ToSnapshot();

        var restored = WalletAccount.Restore(snapshot, []);

        Assert.Equal(account.Version, restored.Version);
        Assert.Equal(account.Available, restored.Available);
        Assert.Equal(account.Reserved, restored.Reserved);
        Assert.True(restored.Credit("credit", 1_000, Now).IsDuplicate);
        Assert.Equal(credit.TransactionId, restored.Credit("credit", 1_000, Now).TransactionId);
        Assert.Contains(reservationId, restored.OpenReservations.Keys);
        restored.Settle("settle", reservationId, 0, Now);
        Assert.Equal(900, restored.Available);
    }

    [Fact]
    public void Restore_applies_only_the_events_after_the_snapshot()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        account.Credit("credit", 500, Now);
        var snapshot = account.ToSnapshot();
        account.Credit("credit-2", 300, Now);
        var tail = account.UncommittedEvents.Skip((int)snapshot.Version);

        var restored = WalletAccount.Restore(snapshot, tail);

        Assert.Equal(800, restored.Available);
        Assert.Equal(account.Version, restored.Version);
    }

    [Fact]
    public void Same_key_with_different_content_is_rejected()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        account.Credit("k", 100, Now);

        var error = Assert.Throws<WalletDomainException>(() => account.Credit("k", 101, Now));

        Assert.Equal(WalletError.IdempotencyKeyReused, error.Error);
        Assert.Equal(100, account.Available);
    }

    [Fact]
    public void Same_key_on_a_different_operation_is_rejected()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        account.Credit("k", 100, Now);

        var error = Assert.Throws<WalletDomainException>(() => account.Reserve("k", Guid.NewGuid(), 100, Now));

        Assert.Equal(WalletError.IdempotencyKeyReused, error.Error);
    }
}
