using Casino.BuildingBlocks;
using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Casino.Modules.Wallet.Api;

public sealed record LoadChipsRequest(Guid ToUserId, long Amount);

public sealed record CashierMeResponse(Guid UserId, string Level, Guid? ParentUserId);

public sealed record JurisdictionMemberResponse(Guid UserId, string Level, long? Available, long? Reserved);

public sealed record CashierTransferResponse(Guid TargetUserId, long Amount, Guid TransactionId, DateTimeOffset OccurredAt);

/// <summary>
/// Cajeros y jefes de cajeros. La identidad y el nivel salen del token (nunca del cuerpo): el jefe carga a sus cajeros y el cajero a sus jugadores, siempre desde
/// su propio saldo. Ver ADR 0014.
/// </summary>
internal static class CashierEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cashier").WithTags("Cashier");
        group.RequireAuthorization(policy => policy.RequireRole(Roles.Cashier, Roles.HeadCashier));
        group.AddEndpointFilter(WalletEndpoints.MapDomainErrors);

        // Mi lugar en la jerarquia (null si el backoffice todavia no me asigno uno).
        group.MapGet("/me", async (HttpContext http, CashierService cashiers, CancellationToken ct) =>
        {
            var userId = http.User.GetUserId();
            var node = await cashiers.GetNodeAsync(userId, ct);
            return node is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: nameof(WalletError.NotInJurisdiction), detail: "Todavia no te asignaron un lugar en la jerarquia de cajeros.")
                : Results.Ok(new CashierMeResponse(userId, CashierService.NameOf(node.Level), node.ParentUserId));
        });

        // La gente de mi jurisdiccion directa, con su saldo.
        group.MapGet("/members", async (HttpContext http, CashierService cashiers, int? limit, CancellationToken ct) =>
        {
            var members = await cashiers.ListMembersAsync(http.User.GetUserId(), Math.Clamp(limit ?? 100, 1, 500), ct);
            return Results.Ok(members.Select(m => new JurisdictionMemberResponse(m.UserId, CashierService.NameOf(m.Level), m.Available, m.Reserved)));
        });

        // Cargar fichas desde mi saldo a alguien de mi jurisdiccion.
        group.MapPost("/transfers", async (HttpContext http, LoadChipsRequest body, CashierService cashiers, CancellationToken ct) =>
        {
            var key = http.Request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: nameof(WalletError.InvalidIdempotencyKey),
                    detail: $"El header {IdempotencyHeader} es obligatorio.");
            }

            var roles = http.User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value);
            var outcome = await cashiers.LoadChipsAsync(http.User.GetUserId(), roles, body.ToUserId, body.Amount, key, ct);
            return Results.Ok(new OperationResponse(outcome.TransactionId, outcome.IsDuplicate));
        });

        // Mis ultimas cargas.
        group.MapGet("/transfers", async (HttpContext http, BackofficeAudit audit, int? limit, CancellationToken ct) =>
        {
            var entries = await audit.ListTransfersByActorAsync(http.User.GetUserId(), Math.Clamp(limit ?? 25, 1, 100), ct);
            return Results.Ok(entries.Select(e => new CashierTransferResponse(e.TargetUserId, e.Amount, e.TransactionId, e.OccurredAt)));
        });
    }
}
