using Casino.Modules.Games.Infrastructure;
using Casino.Modules.Wallet.Infrastructure;
using JasperFx;
using Marten;

/// <summary>El host es dueño del unico DocumentStore: cada modulo registra sus tipos, pero la conexion y el esquema se deciden aca.</summary>
internal static class CasinoStore
{
    public const string SchemaName = "casino";

    public static IServiceCollection AddCasinoStore(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentStore>(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("casinodb")
                ?? throw new InvalidOperationException("Falta la connection string 'casinodb' (la inyecta Aspire).");

            return DocumentStore.For(options =>
            {
                options.Connection(connectionString);
                options.DatabaseSchemaName = SchemaName;
                options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                WalletMartenConfiguration.Register(options);
                GamesMartenConfiguration.Register(options);
            });
        });
        return services;
    }
}
