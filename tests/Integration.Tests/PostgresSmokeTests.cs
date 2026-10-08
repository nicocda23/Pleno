using Testcontainers.PostgreSql;

namespace Casino.Integration.Tests;

public class PostgresSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Postgres_container_accepts_queries()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", conn);

        Assert.Equal(1, await cmd.ExecuteScalarAsync());
    }
}

