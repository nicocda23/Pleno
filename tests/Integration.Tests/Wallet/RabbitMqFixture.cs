using System.Net;
using System.Net.Sockets;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Casino.Integration.Tests.Wallet;

/// <summary>Un RabbitMQ real. Usa un puerto fijo del host para poder apagarlo y volver a levantarlo (simula una caida del broker).</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new RabbitMqBuilder("rabbitmq:4.3-management")
            .WithPortBinding(FreePort(), 5672)
            .Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public Task StopBrokerAsync() => _container!.StopAsync();

    public Task StartBrokerAsync() => _container!.StartAsync();

    /// <summary>Mensajes que esperan en una cola (consulta pasiva: no la crea ni la modifica).</summary>
    public async Task<uint> QueueDepthAsync(string queue)
    {
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    /// <summary>
    /// Crea una cola durable SIN consumidor, enlazada a los exchanges indicados: acumula copia de todo lo que se publique ahi.
    /// Sirve para comprobar que un mensaje llego de verdad al broker, aunque la aplicacion tenga sus propios consumidores.
    /// </summary>
    public async Task<string> CreateAuditQueueAsync(params string[] exchanges)
    {
        var queue = $"test.audit.{Guid.NewGuid():N}";
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        foreach (var exchange in exchanges)
        {
            await channel.QueueBindAsync(queue, exchange, routingKey: string.Empty);
        }

        return queue;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
