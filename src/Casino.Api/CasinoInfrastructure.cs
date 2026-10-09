using Casino.Contracts;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Infrastructure;
using Casino.Modules.Realtime.Application;
using Casino.Modules.Users.Infrastructure;
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

    // Ordenes hacia la Wallet: una cola con un solo dueño.
    public const string WalletCommandsQueue = "wallet.commands";

    // Un exchange por TEMA, con una cola por consumidor. Los handlers de Wolverine son globales a la aplicacion (no importa por
    // que cola llega un mensaje), asi que cada cola recibe solo lo que su modulo maneja: si no, se ejecutarian dos veces.
    public const string WalletStakeEventsExchange = "wallet.stake-events";
    public const string WalletBalanceEventsExchange = "wallet.balance-events";
    public const string GamesEventsExchange = "games.events";
    public const string UsersEventsExchange = "users.events";

    public const string GamesWalletEventsQueue = "games.wallet-events";
    public const string RealtimeWalletEventsQueue = "realtime.wallet-events";
    public const string RealtimeGameEventsQueue = "realtime.game-events";
    public const string WalletUserEventsQueue = "wallet.user-events";

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
                UsersMartenConfiguration.Register(options);
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
            options.Discovery.IncludeAssembly(typeof(PlayerNotifier).Assembly);

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
        options.UnknownMessageBehavior = UnknownMessageBehavior.LogOnly;

        // Ordenes hacia la Wallet.
        options.ListenToRabbitQueue(WalletCommandsQueue).UseDurableInbox();
        options.PublishMessage<ReserveStake>().ToRabbitQueue(WalletCommandsQueue);
        options.PublishMessage<RoundResolved>().ToRabbitQueue(WalletCommandsQueue);
        options.PublishMessage<ExpireReservation>().ToRabbitQueue(WalletCommandsQueue);

        // Hechos de la reserva y la liquidacion: los consumen los juegos.
        broker.DeclareExchange(WalletStakeEventsExchange, exchange =>
        {
            exchange.ExchangeType = ExchangeType.Fanout;
            exchange.BindQueue(GamesWalletEventsQueue);
        });
        options.PublishMessage<StakeReserved>().ToRabbitExchange(WalletStakeEventsExchange);
        options.PublishMessage<StakeRejected>().ToRabbitExchange(WalletStakeEventsExchange);
        options.PublishMessage<StakeSettled>().ToRabbitExchange(WalletStakeEventsExchange);
        options.PublishMessage<StakeReleased>().ToRabbitExchange(WalletStakeEventsExchange);
        options.PublishMessage<StakeSettlementRejected>().ToRabbitExchange(WalletStakeEventsExchange);
        options.ListenToRabbitQueue(GamesWalletEventsQueue).UseDurableInbox();

        // Cambios de saldo: los consume el tiempo real para avisar al navegador.
        broker.DeclareExchange(WalletBalanceEventsExchange, exchange =>
        {
            exchange.ExchangeType = ExchangeType.Fanout;
            exchange.BindQueue(RealtimeWalletEventsQueue);
        });
        options.PublishMessage<BalanceChanged>().ToRabbitExchange(WalletBalanceEventsExchange);
        options.ListenToRabbitQueue(RealtimeWalletEventsQueue).UseDurableInbox();

        // Rondas cerradas: las consume el tiempo real.
        broker.DeclareExchange(GamesEventsExchange, exchange =>
        {
            exchange.ExchangeType = ExchangeType.Fanout;
            exchange.BindQueue(RealtimeGameEventsQueue);
        });
        options.PublishMessage<RoundClosed>().ToRabbitExchange(GamesEventsExchange);
        options.ListenToRabbitQueue(RealtimeGameEventsQueue).UseDurableInbox();

        // Hechos de usuarios: la Wallet abre la cuenta cuando entra un jugador nuevo.
        broker.DeclareExchange(UsersEventsExchange, exchange =>
        {
            exchange.ExchangeType = ExchangeType.Fanout;
            exchange.BindQueue(WalletUserEventsQueue);
        });
        options.PublishMessage<UserRegistered>().ToRabbitExchange(UsersEventsExchange);
        options.ListenToRabbitQueue(WalletUserEventsQueue).UseDurableInbox();
    }
}
