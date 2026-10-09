using Testcontainers.Redis;

namespace Casino.Integration.Tests.Wallet;

/// <summary>Un Redis real: es el backplane que reparte los avisos de SignalR entre varias instancias de la API.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:8.6").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
