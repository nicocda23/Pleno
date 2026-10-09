using Casino.BuildingBlocks;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Fairness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Casino.Modules.Games.Api;

public sealed record RotateRequest(string? ClientSeed);

internal static class FairnessEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fairness/me").WithTags("Provably fair");
        group.RequireAuthorization(policy => policy.RequireRole(Roles.Player));
        group.AddEndpointFilter(MapDomainErrors);

        // Compromiso y client seed del par activo, mas las server seeds ya reveladas para verificar jugadas pasadas.
        // Siempre del usuario autenticado: no se puede consultar el par de otro.
        group.MapGet("/", async (HttpContext http, FairnessService fairness, CancellationToken ct) =>
            Results.Ok(await fairness.GetPublicInfoAsync(http.User.GetUserId(), ct)));

        // Rotar revela la server seed anterior y empieza un par nuevo. No acepta ni nonce ni server seed: solo la client seed.
        group.MapPost("/rotate", async (HttpContext http, RotateRequest? body, FairnessService fairness, CancellationToken ct) =>
            Results.Ok(await fairness.RotateAsync(http.User.GetUserId(), body?.ClientSeed, ct)));
    }

    private static async ValueTask<object?> MapDomainErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (FairnessDomainException ex)
        {
            var status = ex.Error is FairnessError.InvalidClientSeed or FairnessError.InvalidCommitment
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status409Conflict;
            return Results.Problem(statusCode: status, title: ex.Error.ToString(), detail: ex.Message);
        }
    }
}
