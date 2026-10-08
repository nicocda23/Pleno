using System.Security.Cryptography;
using System.Text;
using Casino.BuildingBlocks;
using Casino.Contracts;
using Casino.Modules.Games.Roulette;
using Marten;
using Microsoft.Extensions.Logging;

namespace Casino.Modules.Games.Application;

public enum GamesError
{
    InvalidBet,
    InvalidIdempotencyKey,
    BetKeyReused,
    RoundNotFound,
}

public sealed class GamesDomainException(GamesError error, string message) : Exception(message)
{
    public GamesError Error { get; } = error;
}

public sealed record PlaceRouletteBetRequest(Guid UserId, Guid AccountId, RouletteBetType BetType, IReadOnlyList<int> Selection, long Stake);

/// <summary>Resultado de colocar una apuesta. La resolucion es asincrona: se consulta la ronda por su BetId.</summary>
public sealed record PlacedBet(Guid BetId, RoundStatus Status, long Nonce, string Commitment, string ClientSeed, bool AlreadyPlaced);

/// <summary>
/// Coordina la vida de una apuesta de ruleta. No hay un orquestador: cada paso reacciona a un mensaje de la Wallet y es idempotente.
/// Colocar -> (Wallet reserva) -> sortear -> (Wallet liquida) -> cerrar; si la Wallet rechaza o vence la reserva, se cierra anulada.
/// </summary>
public sealed partial class RouletteService(
    IDocumentStore store,
    FairnessService fairness,
    IOutboxFactory outbox,
    TimeProvider clock,
    ILogger<RouletteService> logger)
{
    /// <summary>
    /// Coloca una apuesta. En UNA transaccion: asigna el nonce (lo decide el servidor), guarda la ronda y encola la orden
    /// de reserva a la Wallet. Es idempotente: la misma IdempotencyKey de la misma cuenta devuelve la misma apuesta.
    /// </summary>
    public async Task<PlacedBet> PlaceBetAsync(PlaceRouletteBetRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new GamesDomainException(GamesError.InvalidIdempotencyKey, "La IdempotencyKey es obligatoria y de hasta 128 caracteres.");
        }

        if (!RouletteBet.TryCreate(request.BetType, request.Selection, request.Stake, out _))
        {
            throw new GamesDomainException(GamesError.InvalidBet, $"Apuesta invalida: {request.BetType} con esa seleccion y apuesta {request.Stake}.");
        }

        var betId = BetIdFor(request.AccountId, idempotencyKey);
        var now = clock.GetUtcNow();

        var allocation = await fairness.AllocateNonceAsync(
            request.UserId,
            betId,
            async (session, outboxSession, nonce) =>
            {
                session.Insert(new RouletteRound
                {
                    Id = betId,
                    UserId = request.UserId,
                    AccountId = request.AccountId,
                    BetType = request.BetType,
                    Selection = [.. request.Selection],
                    Stake = request.Stake,
                    PairId = nonce.PairId,
                    Nonce = nonce.Nonce,
                    Status = RoundStatus.Placed,
                    PlacedAt = now,
                });

                await outboxSession.PublishAsync(new ReserveStake(betId, request.AccountId, request.Stake));
            },
            ct);

        if (allocation.AlreadyAllocated)
        {
            // Reintento: la ronda ya existe. Si la misma clave llega con otro contenido, es un error del cliente.
            var existing = await GetRoundAsync(betId, ct);
            if (existing.AccountId != request.AccountId
                || existing.UserId != request.UserId
                || existing.BetType != request.BetType
                || existing.Stake != request.Stake
                || !existing.Selection.Order().SequenceEqual(request.Selection.Order()))
            {
                throw new GamesDomainException(GamesError.BetKeyReused, "La IdempotencyKey ya se uso con otra apuesta.");
            }

            return new PlacedBet(betId, existing.Status, allocation.Nonce, allocation.Commitment, allocation.ClientSeed, AlreadyPlaced: true);
        }

        return new PlacedBet(betId, RoundStatus.Placed, allocation.Nonce, allocation.Commitment, allocation.ClientSeed, AlreadyPlaced: false);
    }

    public async Task<RouletteRound> GetRoundAsync(Guid betId, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        return await session.LoadAsync<RouletteRound>(betId, ct)
            ?? throw new GamesDomainException(GamesError.RoundNotFound, $"No existe la ronda {betId}.");
    }

    /// <summary>Ultimas rondas de un jugador, de la mas reciente a la mas vieja.</summary>
    public async Task<IReadOnlyList<RouletteRound>> GetHistoryAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        return await session.Query<RouletteRound>()
            .Where(round => round.UserId == userId)
            .OrderByDescending(round => round.PlacedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>La Wallet reservo las fichas: se sortea con el nonce ya asignado y se informa el resultado.</summary>
    public async Task OnStakeReservedAsync(StakeReserved message, CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();
        var round = await session.LoadAsync<RouletteRound>(message.BetId, ct);
        if (round is null)
        {
            LogForeignBet(logger, message.BetId);
            return;
        }

        if (round.Status != RoundStatus.Placed)
        {
            return; // Entrega duplicada: ya se sorteo.
        }

        var inputs = await fairness.GetDrawInputsAsync(round.UserId, round.Id, ct);
        var outcome = RouletteGame.Play(round.ToBet(), inputs.ServerSeed, inputs.ClientSeed, inputs.Nonce);
        round.MarkResolved(outcome.WinningNumber, outcome.Payout);
        session.Store(round);

        // El resultado y el aviso a la Wallet salen en la misma transaccion: no puede quedar sorteada sin avisar.
        await using var outboxSession = outbox.Enroll(session);
        await outboxSession.PublishAsync(new RoundResolved(round.Id, round.AccountId, outcome.Payout));
        await session.SaveChangesAsync(ct);
    }

    /// <summary>La Wallet liquido el premio: la ronda termina bien.</summary>
    public Task OnStakeSettledAsync(StakeSettled message, CancellationToken ct = default) =>
        CloseAsync(message.BetId, (round, now) => round.MarkSettled(now), ct);

    /// <summary>La Wallet no pudo reservar (saldo insuficiente, cuenta inexistente...): la apuesta nunca se jugo.</summary>
    public Task OnStakeRejectedAsync(StakeRejected message, CancellationToken ct = default) =>
        CloseAsync(message.BetId, (round, now) => round.MarkRejected(message.Reason, now), ct);

    /// <summary>Vencio la reserva y la Wallet devolvio las fichas: la ronda se anula.</summary>
    public Task OnStakeReleasedAsync(StakeReleased message, CancellationToken ct = default) =>
        CloseAsync(message.BetId, (round, now) => round.MarkVoided("ReservationExpired", now), ct);

    /// <summary>La Wallet no pudo liquidar (el resultado llego tarde): la ronda se anula.</summary>
    public Task OnSettlementRejectedAsync(StakeSettlementRejected message, CancellationToken ct = default) =>
        CloseAsync(message.BetId, (round, now) => round.MarkVoided(message.Reason, now), ct);

    private async Task CloseAsync(Guid betId, Func<RouletteRound, DateTimeOffset, bool> transition, CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        var round = await session.LoadAsync<RouletteRound>(betId, ct);
        if (round is null)
        {
            LogForeignBet(logger, betId);
            return;
        }

        if (transition(round, clock.GetUtcNow()))
        {
            session.Store(round);
            await session.SaveChangesAsync(ct);
        }

        // Siempre (aunque la transicion ya estuviera aplicada): si un intento anterior cayo entre guardar la ronda y
        // cerrar la apuesta en el generador, el reintento lo completa. CompleteBet es idempotente.
        if (round.IsClosed)
        {
            await fairness.CompleteBetAsync(round.UserId, round.Id, ct);
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Mensaje de una apuesta que no es de ruleta ({BetId}): se ignora.")]
    private static partial void LogForeignBet(ILogger logger, Guid betId);


    /// <summary>Id de apuesta determinista por (cuenta, clave): reintentar con la misma clave apunta a la misma apuesta.</summary>
    internal static Guid BetIdFor(Guid accountId, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"bet:{accountId:N}:{idempotencyKey}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
