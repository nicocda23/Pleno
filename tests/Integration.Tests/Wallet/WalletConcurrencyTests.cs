using System.Diagnostics;
using Casino.Modules.Wallet.Domain;
using Xunit.Abstractions;

namespace Casino.Integration.Tests.Wallet;

[Collection(WalletDbDefinition.Name)]
public class WalletConcurrencyTests(PostgresFixture db, ITestOutputHelper output)
{
    private const int Accounts = 20;
    private const int BetsPerAccount = 50;
    private const long InitialChips = 10_000;
    private const long Stake = 10;
    private const long WinPayout = 30;

    private async Task<Guid> NewFundedAccountAsync(long chips)
    {
        var accountId = await db.Wallet.OpenAccountAsync(Guid.NewGuid());
        await db.Wallet.CreditAsync(accountId, "initial-credit", chips);
        return accountId;
    }

    /// <summary>Apuesta completa: reservar y liquidar. Las apuestas pares ganan, las impares pierden.</summary>
    private async Task PlayBetAsync(Guid accountId, int bet)
    {
        var reservationId = Guid.NewGuid();
        await db.Wallet.ReserveAsync(accountId, $"bet-{bet}", reservationId, Stake);
        await db.Wallet.SettleAsync(accountId, $"settle-{bet}", reservationId, bet % 2 == 0 ? WinPayout : 0);
    }

    [Fact]
    public async Task One_thousand_bets_in_parallel_close_the_balance_exactly()
    {
        var accountIds = await Task.WhenAll(Enumerable.Range(0, Accounts).Select(_ => NewFundedAccountAsync(InitialChips)));

        var clock = Stopwatch.StartNew();
        await Task.WhenAll(accountIds.SelectMany(id =>
            Enumerable.Range(0, BetsPerAccount).Select(bet => Task.Run(() => PlayBetAsync(id, bet)))));
        clock.Stop();

        var wins = BetsPerAccount / 2;
        var expectedAvailable = InitialChips - (BetsPerAccount * Stake) + (wins * WinPayout);
        foreach (var id in accountIds)
        {
            var account = await db.Wallet.GetAsync(id);
            Assert.Equal(expectedAvailable, account.Available);
            Assert.Equal(0, account.Reserved);
            Assert.Empty(account.OpenReservations);
            Assert.Equal(2 + (BetsPerAccount * 2), account.Version);
        }

        var totalBets = Accounts * BetsPerAccount;
        output.WriteLine($"{totalBets} apuestas (reservar + liquidar) en {clock.ElapsedMilliseconds} ms ({totalBets * 1000 / Math.Max(1, clock.ElapsedMilliseconds)} apuestas/s)");
    }

    [Fact]
    public async Task Parallel_reserves_on_one_account_never_overdraw_it()
    {
        var accountId = await NewFundedAccountAsync(500);

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            try
            {
                await db.Wallet.ReserveAsync(accountId, $"bet-{i}", Guid.NewGuid(), Stake);
                return (WalletError?)null;
            }
            catch (WalletDomainException ex)
            {
                return ex.Error;
            }
        })));

        Assert.Equal(50, results.Count(r => r is null));
        Assert.Equal(50, results.Count(r => r == WalletError.InsufficientFunds));

        var account = await db.Wallet.GetAsync(accountId);
        Assert.Equal(0, account.Available);
        Assert.Equal(500, account.Reserved);
    }

    [Fact]
    public async Task Same_idempotency_key_sent_in_parallel_is_applied_once()
    {
        var accountId = await NewFundedAccountAsync(1_000);
        var reservationId = Guid.NewGuid();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            db.Wallet.ReserveAsync(accountId, "double-click-bet", reservationId, 100))));

        Assert.Single(outcomes.Select(o => o.TransactionId).Distinct());
        Assert.Equal(1, outcomes.Count(o => !o.IsDuplicate));

        var account = await db.Wallet.GetAsync(accountId);
        Assert.Equal(900, account.Available);
        Assert.Equal(100, account.Reserved);
        Assert.Equal(3, account.Version);
    }

    [Fact]
    public async Task Ledger_of_every_account_sums_to_zero_after_load()
    {
        var accountId = await NewFundedAccountAsync(InitialChips);

        await Task.WhenAll(Enumerable.Range(0, BetsPerAccount).Select(bet => Task.Run(() => PlayBetAsync(accountId, bet))));

        await using var session = db.Store.QuerySession();
        var stream = await session.Events.FetchStreamAsync(accountId);
        var ledger = stream.Select(e => e.Data).OfType<LedgerEvent>().ToList();

        Assert.All(ledger, e => Assert.Equal(0, e.Entries.Sum(entry => entry.Amount)));
        var account = await db.Wallet.GetAsync(accountId);
        var houseNet = ledger.SelectMany(e => e.Entries).Where(e => e.Account.Kind == LedgerAccountKind.House).Sum(e => e.Amount);
        Assert.Equal(-(account.Available + account.Reserved), houseNet);
    }
}
