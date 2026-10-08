using Casino.BuildingBlocks;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Roulette;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Casino.Modules.Games.Api;

/// <summary>El jugador y su cuenta salen del token: el cuerpo solo dice QUE se apuesta.</summary>
public sealed record PlaceBetBody(string BetType, int[]? Selection, long Stake);

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
    string? FailureReason,
    DateTimeOffset PlacedAt);

internal static class RouletteEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";
    private const int MaxHistory = 100;

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/games/roulette").WithTags("Roulette");
        group.RequireAuthorization(policy => policy.RequireRole(Roles.Player));
        group.AddEndpointFilter(MapDomainErrors);

        // La apuesta se resuelve de forma asincrona (Wallet reserva -> juego sortea -> Wallet liquida): responde 202
        // y la ronda se consulta por su BetId. El servidor asigna el nonce; el cliente no puede elegirlo.
        group.MapPost("/bets", async (PlaceBetBody body, HttpContext http, RouletteService roulette, CancellationToken ct) =>
        {
            if (!Enum.TryParse<RouletteBetType>(body.BetType, ignoreCase: true, out var betType))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: nameof(GamesError.InvalidBet),
                    detail: $"Tipo de apuesta desconocido: {body.BetType}.");
            }

            var userId = http.User.GetUserId();
            var placed = await roulette.PlaceBetAsync(
                new PlaceRouletteBetRequest(userId, PlayerIds.WalletAccountFor(userId), betType, body.Selection ?? [], body.Stake),
                http.Request.Headers[IdempotencyHeader].ToString(),
                ct);
            return Results.Accepted($"/games/roulette/rounds/{placed.BetId}", placed);
        });

        // Solo se ven las rondas propias. Una ajena responde 404 (no 403) para no revelar que existe.
        group.MapGet("/rounds/{betId:guid}", async (Guid betId, HttpContext http, RouletteService roulette, CancellationToken ct) =>
        {
            var round = await roulette.GetRoundAsync(betId, ct);
            if (round.UserId != http.User.GetUserId())
            {
                throw new GamesDomainException(GamesError.RoundNotFound, $"No existe la ronda {betId}.");
            }

            return Results.Ok(ToResponse(round));
        });

        group.MapGet("/rounds", async (HttpContext http, RouletteService roulette, int? limit, CancellationToken ct) =>
        {
            var rounds = await roulette.GetHistoryAsync(http.User.GetUserId(), Math.Clamp(limit ?? 20, 1, MaxHistory), ct);
            return Results.Ok(rounds.Select(ToResponse));
        });
    }

    private static RoundResponse ToResponse(RouletteRound round) => new(
        round.Id, round.Status, round.BetType.ToString(), round.Selection, round.Stake, round.PairId, round.Nonce,
        round.WinningNumber, round.Payout, round.FailureReason, round.PlacedAt);

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
