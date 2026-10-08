using Casino.Contracts;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Infrastructure;
using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Infrastructure;
using JasperFx;
using Marten;
using Wolverine;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

/// <summary>
/// Composicion de la infraestructura compartida: el host es dueño del unico DocumentStore y de Wolverine.
/// Cada modulo registra sus tipos y sus handlers; la conexion, el esquema y las colas se deciden aca.
/// </summary>
internal static class CasinoInfrastructure
{
    public const string SchemaName = "casino";

    // Colas y exchanges de RabbitMQ.
    public const string WalletCommandsQueue = "wallet.commands";
    public const string WalletEventsExchange = "wallet.events";
    public const string GamesWalletEventsQueue = "games.wallet-events";
    public const string RealtimeWalletEventsQueue = "realtime.wallet-events";

    public static WebApplicationBuilder AddCasinoInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddMarten(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("casinodb")
                    ?? throw new InvalidOperationException("Falta la connection string 'casinodb' (la inyecta Aspire).");

                var options = new StoreOptions();
                options.Connection(connectionString);
                options.DatabaseSchemaName = SchemaName;
                options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                WalletMartenConfiguration.Register(options);
                GamesMartenConfiguration.Register(options);
                return options;
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        var rabbit = builder.Configuration.GetConnectionString("rabbitmq");
        builder.Host.UseWolverine(options =>
        {
            options.ServiceName = "casino";
            options.Discovery.IncludeAssembly(typeof(WalletService).Assembly);
            options.Discovery.IncludeAssembly(typeof(FairnessService).Assembly);

            // Inbox durable: los mensajes recibidos quedan registrados y no se procesan dos veces.
            // Outbox durable: lo que se envia queda guardado hasta que el broker lo confirma.
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();

            if (!string.IsNullOrWhiteSpace(rabbit))
            {
                ConfigureRabbitMq(options, new Uri(rabbit));
            }
        });

        return builder;
    }

    private static void ConfigureRabbitMq(WolverineOptions options, Uri rabbit)
    {
        var broker = options.UseRabbitMq(rabbit).AutoProvision();

        // Ordenes hacia la Wallet: una cola con un solo dueño.
        options.ListenToRabbitQueue(WalletCommandsQueue).UseDurableInbox();
        options.PublishMessage<ReserveStake>().ToRabbitQueue(WalletCommandsQueue);
        options.PublishMessage<RoundResolved>().ToRabbitQueue(WalletCommandsQueue);

        // Hechos de la Wallet: pub/sub. Un exchange "fanout" copia cada hecho a la cola de cada consumidor,
        // asi sumar un consumidor nuevo (por ejemplo, otro juego) no requiere tocar a la Wallet.
        broker.DeclareExchange(WalletEventsExchange, exchange =>
        {
            exchange.ExchangeType = ExchangeType.Fanout;
            exchange.BindQueue(GamesWalletEventsQueue);
            exchange.BindQueue(RealtimeWalletEventsQueue);
        });
        options.PublishMessage<StakeReserved>().ToRabbitExchange(WalletEventsExchange);
        options.PublishMessage<StakeRejected>().ToRabbitExchange(WalletEventsExchange);
        options.PublishMessage<StakeSettled>().ToRabbitExchange(WalletEventsExchange);
        options.PublishMessage<StakeReleased>().ToRabbitExchange(WalletEventsExchange);
        options.PublishMessage<StakeSettlementRejected>().ToRabbitExchange(WalletEventsExchange);
        options.PublishMessage<BalanceChanged>().ToRabbitExchange(WalletEventsExchange);
    }
}
