using Casino.BuildingBlocks;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Infrastructure;
using Casino.Modules.Games.Slots;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Casino.Modules.Games.Api;

public static class GamesModule
{
    /// <summary>Requiere que el host registre antes el IDocumentStore y provea 'Fairness:MasterKey' (user-secrets o Key Vault).</summary>
    public static IServiceCollection AddGamesModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => SeedProtector.FromBase64Key(sp.GetRequiredService<IConfiguration>()["Fairness:MasterKey"]));
        services.TryAddSingleton<IOutboxFactory, WolverineOutboxFactory>();
        services.AddSingleton<FairnessService>();
        services.AddSingleton(sp => new SlotsSettingsStore(
            sp.GetRequiredService<Marten.IDocumentStore>(),
            SlotsOptions.Load(sp.GetRequiredService<IConfiguration>()),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<RouletteService>();
        services.AddSingleton<SlotsService>();
        return services;
    }

    /// <summary>Sin autenticacion todavia (fase 3): el host solo lo expone en Development.</summary>
    public static IEndpointRouteBuilder MapGamesModule(this IEndpointRouteBuilder app)
    {
        FairnessEndpoints.Map(app);
        RouletteEndpoints.Map(app);
        SlotsEndpoints.Map(app);
        SlotsAdminEndpoints.Map(app);
        return app;
    }
}
