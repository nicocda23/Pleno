using Casino.Contracts;
using Casino.Hosting;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Infrastructure;
using Casino.Modules.Realtime.Application;
using Casino.Modules.Users.Infrastructure;
using Wolverine;
using Wolverine.RabbitMQ;

/// <summary>
/// Composicion del host principal (puerta de entrada): su propia base de datos (usersdb) con los modulos que todavia viven aca
/// (usuarios y juegos) y la mensajeria que ESTOS modulos necesitan. La Wallet es un servicio aparte: aca solo se le envian ordenes
/// y se escuchan sus hechos por RabbitMQ.
/// </summary>
internal static class CasinoInfrastructure
{
    public const string SchemaName = "casino";

    public static WebApplicationBuilder AddCasinoInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddCasinoData("usersdb", SchemaName, options =>
        {
            GamesMartenConfiguration.Register(options);
            UsersMartenConfiguration.Register(options);
        });

        builder.AddCasinoMessaging(
            "api",
            [typeof(FairnessService).Assembly, typeof(PlayerNotifier).Assembly],
            Configure);

        return builder;
    }

    private static void Configure(WolverineOptions options)
    {
        // Ordenes hacia la Wallet.
        options.PublishMessage<ReserveStake>().ToRabbitQueue(MessagingTopology.WalletCommandsQueue);
        options.PublishMessage<RoundResolved>().ToRabbitQueue(MessagingTopology.WalletCommandsQueue);

        // Hechos de la reserva y la liquidacion: los consumen los juegos.
        options.ListenToRabbitQueue(MessagingTopology.GamesWalletEventsQueue).UseDurableInbox();

        // Cambios de saldo: los consume el tiempo real para avisar al navegador.
        options.ListenToRabbitQueue(MessagingTopology.RealtimeWalletEventsQueue).UseDurableInbox();

        // Rondas cerradas: las publican los juegos y las consume el tiempo real.
        options.PublishMessage<RoundClosed>().ToRabbitExchange(MessagingTopology.GamesEventsExchange);
        options.ListenToRabbitQueue(MessagingTopology.RealtimeGameEventsQueue).UseDurableInbox();

        // Hechos de usuarios: la Wallet abre la cuenta cuando entra un jugador nuevo.
        options.PublishMessage<UserRegistered>().ToRabbitExchange(MessagingTopology.UsersEventsExchange);
    }
}
