using System.Diagnostics.Metrics;
using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Domain;
using Casino.Modules.Wallet.Infrastructure;
using JasperFx;
using Marten;
using Xunit.Abstractions;

namespace Casino.Integration.Tests.Wallet;

/// <summary>
/// Varias instancias independientes del servicio (cada una con su DocumentStore, su pool de conexiones y
/// sus propias compuertas) sobre la misma base. Es lo que pasaria con dos replicas en produccion.
/// </summary>
[Collection(WalletDbDefinition.Name)]
public sealed class WalletMultiInstanceTests(PostgresFixture db, ITestOutputHelper output) : IDisposable
{
    private readonly List<IDocumentStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }
    }

    private WalletService NewInstance(int maxAttempts = WalletService.DefaultMaxAttempts)
    {
        var store = DocumentStore.For(options =>
        {
            WalletMartenConfiguration.Configure(options, db.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.None;
        });
        _stores.Add(store);
        return new WalletService(store, TimeProvider.System, maxAttempts);
    }

    /// <summary>Cuenta los reintentos internos que reporta la metrica de la Wallet, separados por motivo.</summary>
    private sealed class RetryCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _conflicts;
        private long _transient;

        public RetryCounter()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == WalletTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key != WalletTelemetry.ReasonTag)
                    {
                        continue;
                    }

                    if (Equals(tag.Value, WalletTelemetry.ConflictReason))
                    {
                        Interlocked.Add(ref _conflicts, value);
                    }
                    else
                    {
                        Interlocked.Add(ref _transient, value);
                    }
                }
            });
            _listener.Start();
        }

        public long Conflicts => Interlocked.Read(ref _conflicts);

        public long Transient => Interlocked.Read(ref _transient);

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Cliente que reintenta con la misma clave ante errores que no son de negocio.</summary>
    private static async Task<(WalletError? Error, int ClientRetries)> AttemptAsync(Func<Task> operation)
    {
        var retries = 0;
        while (true)
        {
            try
            {
                await operation();
                return (null, retries);
            }
            catch (WalletDomainException ex)
            {
                return (ex.Error, retries);
            }
            catch (Exception) when (retries < 200)
            {
                retries++;
                await Task.Delay(10);
            }
        }
    }

    private static async Task<(int Succeeded, int Insufficient, int ClientRetries, long Conflicts, WalletAccount Account)> RunHotAccountAsync(
        params WalletService[] instances)
    {
        var accountId = await instances[0].OpenAccountAsync(Guid.NewGuid());
        await instances[0].CreditAsync(accountId, "initial-credit", 500);

        using var counter = new RetryCounter();
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var wallet = instances[i % instances.Length];
            var reservationId = Guid.NewGuid();
            return AttemptAsync(() => wallet.ReserveAsync(accountId, $"bet-{i}", reservationId, 10));
        })));

        var account = await instances[0].GetAsync(accountId);
        return (
            results.Count(r => r.Error is null),
            results.Count(r => r.Error == WalletError.InsufficientFunds),
            results.Sum(r => r.ClientRetries),
            counter.Conflicts,
            account);
    }

    [Fact]
    public async Task One_instance_serializes_a_hot_account_without_version_conflicts()
    {
        var result = await RunHotAccountAsync(NewInstance());

        output.WriteLine($"1 instancia: conflictos={result.Conflicts}, reintentos de cliente={result.ClientRetries}");
        Assert.Equal(50, result.Succeeded);
        Assert.Equal(50, result.Insufficient);
        Assert.Equal(0, result.Account.Available);
        Assert.Equal(500, result.Account.Reserved);
        Assert.Equal(0, result.Conflicts);
    }

    [Fact]
    public async Task Two_instances_race_on_a_hot_account_and_the_balance_still_closes()
    {
        var result = await RunHotAccountAsync(NewInstance(), NewInstance());

        output.WriteLine($"2 instancias: conflictos={result.Conflicts}, reintentos de cliente={result.ClientRetries}");
        Assert.Equal(50, result.Succeeded);
        Assert.Equal(50, result.Insufficient);
        Assert.Equal(0, result.Account.Available);
        Assert.Equal(500, result.Account.Reserved);
        Assert.True(result.Conflicts > 0, "Sin conflictos de version la prueba no ejercita la concurrencia optimista entre instancias.");
    }

    [Fact]
    public async Task Same_idempotency_key_sent_to_two_instances_is_applied_once()
    {
        var first = NewInstance();
        var second = NewInstance();
        var accountId = await first.OpenAccountAsync(Guid.NewGuid());
        await first.CreditAsync(accountId, "initial-credit", 1_000);
        var reservationId = Guid.NewGuid();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            (i % 2 == 0 ? first : second).ReserveAsync(accountId, "retried-by-load-balancer", reservationId, 100))));

        Assert.Single(outcomes.Select(o => o.TransactionId).Distinct());
        Assert.Equal(1, outcomes.Count(o => !o.IsDuplicate));
        var account = await first.GetAsync(accountId);
        Assert.Equal(900, account.Available);
        Assert.Equal(3, account.Version);
    }

    [Fact]
    public async Task Load_spread_over_three_instances_closes_every_balance()
    {
        const int accounts = 10;
        const int bets = 40;
        var instances = new[] { NewInstance(), NewInstance(), NewInstance() };
        var accountIds = new List<Guid>();
        for (var i = 0; i < accounts; i++)
        {
            var id = await instances[0].OpenAccountAsync(Guid.NewGuid());
            await instances[0].CreditAsync(id, "initial-credit", 5_000);
            accountIds.Add(id);
        }

        using var counter = new RetryCounter();
        var errors = await Task.WhenAll(accountIds.SelectMany(id => Enumerable.Range(0, bets).Select(bet => Task.Run(async () =>
        {
            var reservationId = Guid.NewGuid();
            var reserve = await AttemptAsync(() => instances[Random.Shared.Next(instances.Length)].ReserveAsync(id, $"bet-{bet}", reservationId, 10));
            var settle = await AttemptAsync(() => instances[Random.Shared.Next(instances.Length)].SettleAsync(id, $"settle-{bet}", reservationId, bet % 2 == 0 ? 30 : 0));
            return reserve.Error ?? settle.Error;
        }))));

        output.WriteLine($"3 instancias: conflictos={counter.Conflicts}, fallos transitorios={counter.Transient}");
        Assert.All(errors, error => Assert.Null(error));

        var expectedAvailable = 5_000 - (bets * 10) + (bets / 2 * 30);
        foreach (var id in accountIds)
        {
            var account = await instances[0].GetAsync(id);
            Assert.Equal(expectedAvailable, account.Available);
            Assert.Equal(0, account.Reserved);
            Assert.Equal(2 + (bets * 2), account.Version);
        }
    }
}
