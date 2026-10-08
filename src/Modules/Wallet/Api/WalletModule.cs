using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Casino.Modules.Wallet.Api;

public static class WalletModule
{
    /// <summary>Registra Marten y el servicio de la Wallet. La connection string se lee de ConnectionStrings:casinodb.</summary>
    public static IServiceCollection AddWalletModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDocumentStore>(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("casinodb")
                ?? throw new InvalidOperationException("Falta la connection string 'casinodb' (la inyecta Aspire).");
            return DocumentStore.For(options => WalletMartenConfiguration.Configure(options, connectionString));
        });
        services.AddSingleton<WalletService>();
        return services;
    }

    /// <summary>
    /// Mapea los endpoints de la Wallet. SIN autenticacion todavia (llega con Keycloak en la fase 3):
    /// por eso el host solo los expone en Development.
    /// </summary>
    public static IEndpointRouteBuilder MapWalletModule(this IEndpointRouteBuilder app)
    {
        WalletEndpoints.Map(app);
        return app;
    }
}
