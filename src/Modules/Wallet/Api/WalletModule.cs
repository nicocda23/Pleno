using Casino.Modules.Wallet.Application;
using Marten;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Casino.Modules.Wallet.Api;

public static class WalletModule
{
    /// <summary>Requiere que el host registre antes Marten y Wolverine (compartidos entre modulos).</summary>
    public static IServiceCollection AddWalletModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxFactory, WolverineOutboxFactory>();
        services.AddSingleton(sp => new WalletService(
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<TimeProvider>(),
            outbox: sp.GetRequiredService<IOutboxFactory>()));
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
