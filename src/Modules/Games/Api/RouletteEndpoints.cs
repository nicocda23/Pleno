using Casino.Modules.Games.Application;
using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Roulette;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Casino.Modules.Games.Api;

public sealed record PlaceBetBody(Guid UserId, Guid AccountId, string BetType, int[]? Selection, long Stake);

public sealed record RoundResponse(
    Guid BetId,
    RoundStatus Status,
    string BetType,
    IReadOnlyList<int> Selection,
    long Stake,
    Guid PairId,
    long Nonce,
    int? WinningNumber,
    long? Payout,
    string? FailureReason);

internal static class RouletteEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/games/roulette").WithTags("Roulette");
        group.AddEndpointFilter(MapDomainErrors);

        // La apuesta se resuelve de forma asincrona (Wallet reserva -> juego sortea -> Wallet liquida): responde 202
        // y la ronda se consulta por su BetId. El servidor asigna el nonce; el cliente no puede elegirlo.
        group.MapPost("/bets", async (PlaceBetBody body, HttpRequest http, RouletteService roulette, CancellationToken ct) =>
        {
            if (!Enum.TryParse<RouletteBetType>(body.BetType, ignoreCase: true, out var betType))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: nameof(GamesError.InvalidBet),
                    detail: $"Tipo de apuesta desconocido: {body.BetType}.");
            }

            var placed = await roulette.PlaceBetAsync(
                new PlaceRouletteBetRequest(body.UserId, body.AccountId, betType, body.Selection ?? [], body.Stake),
                http.Headers[IdempotencyHeader].ToString(),
                ct);
            return Results.Accepted($"/games/roulette/rounds/{placed.BetId}", placed);
        });

        group.MapGet("/rounds/{betId:guid}", async (Guid betId, RouletteService roulette, CancellationToken ct) =>
        {
            var round = await roulette.GetRoundAsync(betId, ct);
            return Results.Ok(new RoundResponse(
                round.Id, round.Status, round.BetType.ToString(), round.Selection, round.Stake, round.PairId, round.Nonce,
                round.WinningNumber, round.Payout, round.FailureReason));
        });
    }

    private static async ValueTask<object?> MapDomainErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (GamesDomainException ex)
        {
            var status = ex.Error switch
            {
                GamesError.RoundNotFound => StatusCodes.Status404NotFound,
                GamesError.BetKeyReused => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Problem(statusCode: status, title: ex.Error.ToString(), detail: ex.Message);
        }
        catch (FairnessDomainException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Error.ToString(), detail: ex.Message);
        }
    }
}
