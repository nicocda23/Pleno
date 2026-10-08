using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Domain;
using Casino.Modules.Wallet.Infrastructure;
using Marten;
using Npgsql;
using Xunit.Abstractions;

namespace Casino.Integration.Tests.Wallet;

[Collection(WalletDbDefinition.Name)]
public class WalletFaultToleranceTests(PostgresFixture db, ITestOutputHelper output)
{
    private const int Accounts = 10;
    private const int BetsPerAccount = 30;
    private const long InitialChips = 5_000;
    private const long Stake = 10;
    private const long WinPayout = 30;

    /// <summary>
    /// Simula un cliente que reintenta con la MISMA IdempotencyKey hasta tener respuesta.
    /// Es lo que hara el frontend o la saga ante un error de red.
    /// </summary>
    private static async Task<int> RetryUntilSuccessAsync(Func<Task> operation)
    {
        var failures = 0;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                await operation();
                return failures;
            }
            catch (Exception ex) when (ex is not WalletDomainException)
            {
                failures++;
                await Task.Delay(20);
            }
        }

        throw new InvalidOperationException("La operacion no se pudo completar tras 200 reintentos.");
    }

    [Fact]
    public async Task Connection_kills_during_load_never_lose_or_duplicate_chips()
    {
        var accountIds = new List<Guid>();
        for (var i = 0; i < Accounts; i++)
        {
            var id = await db.Wallet.OpenAccountAsync(Guid.NewGuid());
            await db.Wallet.CreditAsync(id, "initial-credit", InitialChips);
            accountIds.Add(id);
        }

        // Servicio con pocos reintentos internos: el cliente tambien tiene que reintentar con la misma clave.
        var fragileWallet = new WalletService(db.Store, TimeProvider.System, maxAttempts: 2);
        var killed = 0L;
        using var stopChaos = new CancellationTokenSource();
        var chaos = Task.Run(async () =>
        {
            while (!stopChaos.IsCancellationRequested)
            {
                try
                {
                    killed += await db.KillAllOtherConnectionsAsync();
                    await Task.Delay(25, stopChaos.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        var clientRetries = await Task.WhenAll(accountIds.SelectMany(id =>
            Enumerable.Range(0, BetsPerAccount).Select(bet => Task.Run(async () =>
            {
                var reservationId = DeterministicReservationId(id, bet);
                var retries = await RetryUntilSuccessAsync(() =>
                    fragileWallet.ReserveAsync(id, $"bet-{bet}", reservationId, Stake));
                retries += await RetryUntilSuccessAsync(() =>
                    fragileWallet.SettleAsync(id, $"settle-{bet}", reservationId, bet % 2 == 0 ? WinPayout : 0));
                return retries;
            }))));

        await stopChaos.CancelAsync();
        await chaos;

        output.WriteLine($"Conexiones terminadas a la fuerza: {killed}; reintentos de cliente: {clientRetries.Sum()}");
        Assert.True(killed > 0, "El caos no llego a cerrar ninguna conexion: la prueba no demuestra nada.");

        var wins = BetsPerAccount / 2;
        var expectedAvailable = InitialChips - (BetsPerAccount * Stake) + (wins * WinPayout);
        foreach (var id in accountIds)
        {
            var account = await db.Wallet.GetAsync(id);
            Assert.Equal(expectedAvailable, account.Available);
            Assert.Equal(0, account.Reserved);
            Assert.Equal(2 + (BetsPerAccount * 2), account.Version);
        }
    }

    [Fact]
    public async Task Database_down_fails_without_changing_state_and_recovers_with_same_key()
    {
        var accountId = await db.Wallet.OpenAccountAsync(Guid.NewGuid());
        await db.Wallet.CreditAsync(accountId, "initial-credit", 1_000);

        var unreachable = new NpgsqlConnectionStringBuilder(db.ConnectionString)
        {
            Port = 1,
            Timeout = 2,
            Pooling = false,
        }.ConnectionString;
        using var brokenStore = DocumentStore.For(options =>
        {
            WalletMartenConfiguration.Configure(options, unreachable);
            options.AutoCreateSchemaObjects = JasperFx.AutoCreate.None;
        });
        var brokenWallet = new WalletService(brokenStore, TimeProvider.System, maxAttempts: 3);
        var reservationId = Guid.NewGuid();

        var error = await Record.ExceptionAsync(() => brokenWallet.ReserveAsync(accountId, "bet-1", reservationId, 100));

        Assert.NotNull(error);
        Assert.IsNotType<WalletDomainException>(error);
        Assert.Equal(1_000, (await db.Wallet.GetAsync(accountId)).Available);

        // Cuando la base vuelve, el cliente reintenta con la misma clave y se aplica una sola vez.
        var first = await db.Wallet.ReserveAsync(accountId, "bet-1", reservationId, 100);
        var again = await db.Wallet.ReserveAsync(accountId, "bet-1", reservationId, 100);

        Assert.False(first.IsDuplicate);
        Assert.True(again.IsDuplicate);
        Assert.Equal(900, (await db.Wallet.GetAsync(accountId)).Available);
    }

    /// <summary>Id estable por (cuenta, apuesta): un reintento del cliente reenvia exactamente la misma reserva.</summary>
    private static Guid DeterministicReservationId(Guid accountId, int bet)
    {
        Span<byte> bytes = stackalloc byte[16];
        accountId.TryWriteBytes(bytes);
        BitConverter.TryWriteBytes(bytes[12..], bet);
        return new Guid(bytes);
    }
}
