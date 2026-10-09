using Casino.BuildingBlocks;
using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Casino.Modules.Wallet.Api;

public sealed record CreditRequest(long Amount);

public sealed record AuditEntryResponse(
    string Action, Guid ActorUserId, Guid TargetUserId, long Amount, Guid TransactionId, DateTimeOffset OccurredAt);

public sealed record OperationResponse(Guid TransactionId, bool IsDuplicate);

public sealed record AccountResponse(
    Guid AccountId,
    Guid UserId,
    long Available,
    long Reserved,
    long Version,
    IReadOnlyDictionary<Guid, long> OpenReservations);

// Reservar, liquidar y liberar NO son endpoints: son pasos internos de una apuesta que ocurren por mensajes.
// Un jugador solo puede consultar SU cuenta; acreditar fichas a mano es cosa del backoffice.
internal static class WalletEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void Map(IEndpointRouteBuilder app)
    {
        var player = app.MapGroup("/wallet").WithTags("Wallet");
        player.RequireAuthorization(policy => policy.RequireRole(Roles.Player));
        player.AddEndpointFilter(MapDomainErrors);

        // La cuenta se deriva del usuario autenticado: no hay forma de pedir la de otro.
        player.MapGet("/me", (HttpContext http, WalletService wallet, CancellationToken ct) =>
            GetAccountAsync(http.User.GetUserId(), wallet, ct));

        player.MapGet("/me/balance", (HttpContext http, DateTimeOffset asOf, WalletService wallet, CancellationToken ct) =>
            GetBalanceAsync(http.User.GetUserId(), asOf, wallet, ct));

        var backoffice = app.MapGroup("/backoffice/wallet").WithTags("Backoffice");
        backoffice.RequireAuthorization(policy => policy.RequireRole(Roles.Backoffice));
        backoffice.AddEndpointFilter(MapDomainErrors);

        backoffice.MapGet("/users/{userId:guid}", (Guid userId, WalletService wallet, CancellationToken ct) =>
            GetAccountAsync(userId, wallet, ct));

        backoffice.MapGet("/users/{userId:guid}/balance", (Guid userId, DateTimeOffset asOf, WalletService wallet, CancellationToken ct) =>
            GetBalanceAsync(userId, asOf, wallet, ct));

        // Ajuste manual de saldo; queda anotado en el registro de auditoria. La doble aprobacion llega en la fase 5.
        backoffice.MapPost("/users/{userId:guid}/credit", async (Guid userId, HttpContext context, CreditRequest body, WalletService wallet, BackofficeAudit audit, CancellationToken ct) =>
        {
            var key = context.Request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: nameof(WalletError.InvalidIdempotencyKey),
                    detail: $"El header {IdempotencyHeader} es obligatorio.");
            }

            var accountId = PlayerIds.WalletAccountFor(userId);
            var outcome = await wallet.CreditAsync(accountId, key, body.Amount, ct);
            // Tambien si fue un duplicado: asi un reintento completa la anotacion si fallo la primera vez.
            await audit.RecordCreditAsync(context.User.GetUserId(), userId, accountId, body.Amount, key, outcome.TransactionId, ct);
            return Results.Ok(new OperationResponse(outcome.TransactionId, outcome.IsDuplicate));
        });

        // Registro de auditoria: quien cargo fichas, a quien y cuando.
        backoffice.MapGet("/audit", async (int? limit, BackofficeAudit audit, CancellationToken ct) =>
        {
            var entries = await audit.ListAsync(Math.Clamp(limit ?? 50, 1, 200), ct);
            return Results.Ok(entries.Select(e => new AuditEntryResponse(e.Action, e.ActorUserId, e.TargetUserId, e.Amount, e.TransactionId, e.OccurredAt)));
        });
    }

    private static async Task<IResult> GetAccountAsync(Guid userId, WalletService wallet, CancellationToken ct)
    {
        var account = await wallet.GetAsync(PlayerIds.WalletAccountFor(userId), ct);
        return Results.Ok(new AccountResponse(
            account.Id, account.UserId, account.Available, account.Reserved, account.Version, account.OpenReservations));
    }

    private static async Task<IResult> GetBalanceAsync(Guid userId, DateTimeOffset asOf, WalletService wallet, CancellationToken ct)
    {
        var balance = await wallet.GetBalanceAtAsync(PlayerIds.WalletAccountFor(userId), asOf, ct);
        return balance is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "AccountNotOpenYet", detail: "La cuenta aun no existia en esa fecha.")
            : Results.Ok(balance);
    }

    private static async ValueTask<object?> MapDomainErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (WalletDomainException ex)
        {
            return Results.Problem(statusCode: StatusFor(ex.Error), title: ex.Error.ToString(), detail: ex.Message);
        }
    }

    private static int StatusFor(WalletError error) => error switch
    {
        WalletError.AccountNotFound or WalletError.ReservationNotFound or WalletError.TransactionNotFound
            => StatusCodes.Status404NotFound,
        WalletError.InvalidAmount or WalletError.InvalidIdempotencyKey
            => StatusCodes.Status400BadRequest,
        WalletError.InsufficientFunds
            => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict,
    };
}
