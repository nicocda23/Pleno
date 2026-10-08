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
        var group = app.MapGroup("/fairness").WithTags("Provably fair");
        group.AddEndpointFilter(MapDomainErrors);

        // Compromiso y client seed del par activo, mas las server seeds ya reveladas para verificar jugadas pasadas.
        group.MapGet("/{userId:guid}", async (Guid userId, FairnessService fairness, CancellationToken ct) =>
            Results.Ok(await fairness.GetPublicInfoAsync(userId, ct)));

        // Rotar revela la server seed anterior y empieza un par nuevo. No acepta ni nonce ni server seed: solo la client seed.
        group.MapPost("/{userId:guid}/rotate", async (Guid userId, RotateRequest? body, FairnessService fairness, CancellationToken ct) =>
            Results.Ok(await fairness.RotateAsync(userId, body?.ClientSeed, ct)));
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
