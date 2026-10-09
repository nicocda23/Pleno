using Casino.Modules.Games.Fairness;

namespace Casino.Games.Tests;

public class FairnessAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed record Pair(Guid PairId, string ServerSeed, string Commitment);

    private static Pair NewPair()
    {
        var seed = ProvablyFair.GenerateServerSeed();
        return new Pair(Guid.NewGuid(), seed, ProvablyFair.Commitment(seed));
    }

    private static (FairnessAccount Account, Pair First) Started(string clientSeed = "mi-semilla")
    {
        var first = NewPair();
        var account = FairnessAccount.Start(Guid.NewGuid(), first.PairId, first.Commitment, "cifrado", clientSeed, Now);
        return (account, first);
    }

    private static FairnessError ErrorOf(Action action) => Assert.Throws<FairnessDomainException>(action).Error;

    [Fact]
    public void A_new_account_starts_with_one_active_pair_at_nonce_zero()
    {
        var (account, first) = Started();

        Assert.Equal(first.Commitment, account.Active.Commitment);
        Assert.Equal("mi-semilla", account.Active.ClientSeed);
        Assert.Equal(0, account.Active.NextNonce);
        Assert.Empty(account.Retired);
    }

    [Fact]
    public void The_server_assigns_consecutive_nonces()
    {
        var (account, _) = Started();

        var nonces = Enumerable.Range(0, 5).Select(_ => account.AllocateNonce(Guid.NewGuid(), Now).Nonce).ToList();

        Assert.Equal([0L, 1, 2, 3, 4], nonces);
        Assert.Equal(5, account.Active.NextNonce);
    }

    [Fact]
    public void Retrying_the_same_bet_returns_the_same_nonce_without_consuming_another()
    {
        var (account, _) = Started();
        var bet = Guid.NewGuid();
        var first = account.AllocateNonce(bet, Now);
        account.AllocateNonce(Guid.NewGuid(), Now);

        var retry = account.AllocateNonce(bet, Now);

        Assert.False(first.AlreadyAllocated);
        Assert.True(retry.AlreadyAllocated);
        Assert.Equal(first.Nonce, retry.Nonce);
        Assert.Equal(2, account.Active.NextNonce);
    }

    [Fact]
    public void Completing_a_bet_is_idempotent_and_unknown_bets_are_rejected()
    {
        var (account, _) = Started();
        var bet = Guid.NewGuid();
        account.AllocateNonce(bet, Now);

        account.CompleteBet(bet, Now);
        var versionAfterFirst = account.Version;
        account.CompleteBet(bet, Now);

        Assert.Equal(versionAfterFirst, account.Version);
        Assert.Equal(0, account.PendingBets);
        Assert.Equal(FairnessError.BetNotFound, ErrorOf(() => account.CompleteBet(Guid.NewGuid(), Now)));
    }

    [Fact]
    public void Rotation_is_blocked_while_a_bet_is_pending()
    {
        var (account, first) = Started();
        account.AllocateNonce(Guid.NewGuid(), Now);
        var next = NewPair();

        var error = ErrorOf(() => account.Rotate(first.ServerSeed, next.PairId, next.Commitment, "cifrado-2", "otra", Now));

        Assert.Equal(FairnessError.PendingBets, error);
        Assert.Equal(first.PairId, account.Active.PairId);
    }

    [Fact]
    public void Rotation_reveals_the_old_seed_and_restarts_the_nonce()
    {
        var (account, first) = Started();
        var bet = Guid.NewGuid();
        account.AllocateNonce(bet, Now);
        account.CompleteBet(bet, Now);
        var next = NewPair();

        account.Rotate(first.ServerSeed, next.PairId, next.Commitment, "cifrado-2", "otra-semilla", Now);

        var retired = Assert.Single(account.Retired);
        Assert.Equal(first.ServerSeed, retired.RevealedServerSeed);
        Assert.Equal(first.Commitment, retired.Commitment);
        Assert.Equal(1, retired.BetsPlayed);
        Assert.Equal(next.PairId, account.Active.PairId);
        Assert.Equal(0, account.Active.NextNonce);
        Assert.Equal("otra-semilla", account.Active.ClientSeed);
        Assert.NotEqual(first.Commitment, account.Active.Commitment);
        Assert.Equal(0, account.AllocateNonce(Guid.NewGuid(), Now).Nonce);
    }


    [Fact]
    public void Rotation_rejects_a_seed_that_does_not_match_the_commitment()
    {
        var (account, _) = Started();
        var next = NewPair();

        var error = ErrorOf(() => account.Rotate(ProvablyFair.GenerateServerSeed(), next.PairId, next.Commitment, "c", "otra", Now));

        Assert.Equal(FairnessError.SeedDoesNotMatchCommitment, error);
    }

    [Fact]
    public void A_bet_allocated_before_rotation_keeps_its_original_pair_and_revealed_seed()
    {
        var (account, first) = Started();
        var bet = Guid.NewGuid();
        var original = account.AllocateNonce(bet, Now);
        account.CompleteBet(bet, Now);
        var next = NewPair();
        account.Rotate(first.ServerSeed, next.PairId, next.Commitment, "cifrado-2", "otra", Now);

        var replay = account.AllocateNonce(bet, Now);
        var context = account.GetDrawContext(bet);

        Assert.True(replay.AlreadyAllocated);
        Assert.Equal(first.PairId, replay.PairId);
        Assert.Equal(original.Nonce, replay.Nonce);
        Assert.Equal(first.ServerSeed, context.RevealedServerSeed);
        Assert.Null(context.EncryptedServerSeed);
        Assert.Equal("mi-semilla", context.ClientSeed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tdentro")]
    public void Invalid_client_seeds_are_rejected_when_starting(string clientSeed)
    {
        var first = NewPair();

        Assert.Equal(
            FairnessError.InvalidClientSeed,
            ErrorOf(() => FairnessAccount.Start(Guid.NewGuid(), first.PairId, first.Commitment, "c", clientSeed, Now)));
    }

    [Fact]
    public void Client_seed_longer_than_64_chars_and_malformed_commitments_are_rejected()
    {
        var first = NewPair();

        Assert.Equal(
            FairnessError.InvalidClientSeed,
            ErrorOf(() => FairnessAccount.Start(Guid.NewGuid(), first.PairId, first.Commitment, "c", new string('x', 65), Now)));
        Assert.Equal(
            FairnessError.InvalidCommitment,
            ErrorOf(() => FairnessAccount.Start(Guid.NewGuid(), first.PairId, "corto", "c", "ok", Now)));
        Assert.Equal(
            FairnessError.InvalidCommitment,
            ErrorOf(() => FairnessAccount.Start(Guid.NewGuid(), first.PairId, new string('G', 64), "c", "ok", Now)));
    }

    [Fact]
    public void State_is_rebuilt_from_events()
    {
        var (account, first) = Started();
        var bet = Guid.NewGuid();
        account.AllocateNonce(bet, Now);
        account.AllocateNonce(Guid.NewGuid(), Now);
        account.CompleteBet(bet, Now);

        var rebuilt = FairnessAccount.Rehydrate(account.UncommittedEvents);

        Assert.Equal(account.Version, rebuilt.Version);
        Assert.Equal(account.Active, rebuilt.Active);
        Assert.Equal(account.PendingBets, rebuilt.PendingBets);
        Assert.Equal(first.Commitment, rebuilt.Active.Commitment);
        Assert.True(rebuilt.AllocateNonce(bet, Now).AlreadyAllocated);
    }
}
