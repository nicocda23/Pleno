using Casino.Modules.Games.Application;
using Casino.Modules.Games.Infrastructure;
using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Infrastructure;
using Marten;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Casino.Integration.Tests.Wallet;

/// <summary>Un Postgres real compartido por la coleccion. Los tests de la coleccion corren en serie.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public IDocumentStore Store { get; private set; } = null!;

    public WalletService Wallet { get; private set; } = null!;

    /// <summary>Clave maestra de pruebas: se genera por corrida y nunca se escribe en disco.</summary>
    public string MasterKey { get; } = SeedProtector.GenerateBase64Key();

    public FairnessService Fairness { get; private set; } = null!;

    public SeedProtector Protector => SeedProtector.FromBase64Key(MasterKey);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        ConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            MaxPoolSize = 80,
        }.ConnectionString;

        Store = DocumentStore.For(options =>
        {
            WalletMartenConfiguration.Configure(options, ConnectionString);
            GamesMartenConfiguration.Register(options);
        });
        await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        Wallet = new WalletService(Store, TimeProvider.System);
        Fairness = new FairnessService(Store, Protector, TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        Store?.Dispose();
        await _postgres.DisposeAsync();
    }

    /// <summary>Cierra a la fuerza todas las conexiones de otras sesiones (simula caidas de red o reinicios del servidor).</summary>
    public async Task<long> KillAllOtherConnectionsAsync()
    {
        var killerConnection = new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString;
        await using var conn = new NpgsqlConnection(killerConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid()",
            conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }
}

[CollectionDefinition(Name)]
public sealed class WalletDbDefinition : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>, ICollectionFixture<Casino.Integration.Tests.Users.KeycloakFixture>, ICollectionFixture<RedisFixture>
{
    public const string Name = "wallet-db";
}
