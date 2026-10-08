using Casino.Modules.Wallet.Application;
using Casino.Modules.Wallet.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Casino.Modules.Wallet.Api;

public sealed record OpenAccountRequest(Guid UserId);

public sealed record CreditRequest(long Amount);

public sealed record ReserveRequest(Guid ReservationId, long Stake);

public sealed record SettleRequest(Guid ReservationId, long Payout);

public sealed record ReleaseRequest(Guid ReservationId);

public sealed record ReverseRequest(Guid TransactionId);

public sealed record OperationResponse(Guid TransactionId, bool IsDuplicate);

public sealed record AccountResponse(
    Guid AccountId,
    Guid UserId,
    long Available,
    long Reserved,
    long Version,
    IReadOnlyDictionary<Guid, long> OpenReservations);

internal static class WalletEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wallet").WithTags("Wallet");
        group.AddEndpointFilter(MapDomainErrors);

        group.MapPost("/accounts", async (OpenAccountRequest request, WalletService wallet, CancellationToken ct) =>
        {
            var accountId = await wallet.OpenAccountAsync(request.UserId, ct);
            return Results.Created($"/wallet/accounts/{accountId}", new { accountId });
        });

        group.MapGet("/accounts/{accountId:guid}", async (Guid accountId, WalletService wallet, CancellationToken ct) =>
        {
            var account = await wallet.GetAsync(accountId, ct);
            return Results.Ok(new AccountResponse(
                account.Id, account.UserId, account.Available, account.Reserved, account.Version, account.OpenReservations));
        });

        group.MapGet("/accounts/{accountId:guid}/balance", async (Guid accountId, DateTimeOffset asOf, WalletService wallet, CancellationToken ct) =>
        {
            var balance = await wallet.GetBalanceAtAsync(accountId, asOf, ct);
            return balance is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "AccountNotOpenYet", detail: "La cuenta aun no existia en esa fecha.")
                : Results.Ok(balance);
        });

        group.MapPost("/accounts/{accountId:guid}/credit", (Guid accountId, HttpRequest http, CreditRequest body, WalletService wallet, CancellationToken ct) =>
            Execute(http, key => wallet.CreditAsync(accountId, key, body.Amount, ct)));

        group.MapPost("/accounts/{accountId:guid}/reserve", (Guid accountId, HttpRequest http, ReserveRequest body, WalletService wallet, CancellationToken ct) =>
            Execute(http, key => wallet.ReserveAsync(accountId, key, body.ReservationId, body.Stake, ct)));

        group.MapPost("/accounts/{accountId:guid}/settle", (Guid accountId, HttpRequest http, SettleRequest body, WalletService wallet, CancellationToken ct) =>
            Execute(http, key => wallet.SettleAsync(accountId, key, body.ReservationId, body.Payout, ct)));

        group.MapPost("/accounts/{accountId:guid}/release", (Guid accountId, HttpRequest http, ReleaseRequest body, WalletService wallet, CancellationToken ct) =>
            Execute(http, key => wallet.ReleaseAsync(accountId, key, body.ReservationId, ct)));

        group.MapPost("/accounts/{accountId:guid}/reverse", (Guid accountId, HttpRequest http, ReverseRequest body, WalletService wallet, CancellationToken ct) =>
            Execute(http, key => wallet.ReverseAsync(accountId, key, body.TransactionId, ct)));
    }

    private static async Task<IResult> Execute(HttpRequest http, Func<string, Task<OperationOutcome>> operation)
    {
        var key = http.Headers[IdempotencyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: nameof(WalletError.InvalidIdempotencyKey),
                detail: $"El header {IdempotencyHeader} es obligatorio.");
        }

        var outcome = await operation(key);
        return Results.Ok(new OperationResponse(outcome.TransactionId, outcome.IsDuplicate));
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
