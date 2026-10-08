using Casino.Modules.Realtime.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Casino.Modules.Realtime.Api;

public static class RealtimeModule
{
    /// <summary>El host registra SignalR (y el backplane de Redis si hay); este modulo agrega el notificador.</summary>
    public static IServiceCollection AddRealtimeModule(this IServiceCollection services)
    {
        services.AddSingleton<PlayerNotifier>();
        return services;
    }

    public static IEndpointRouteBuilder MapRealtimeModule(this IEndpointRouteBuilder app)
    {
        app.MapHub<PlayerHub>(PlayerHub.Path);
        return app;
    }
}
