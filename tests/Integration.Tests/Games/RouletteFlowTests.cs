using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Casino.Contracts;
using Casino.Integration.Tests.Wallet;
using Casino.Modules.Games.Application;
using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Roulette;
using Casino.Modules.Wallet.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;
using Xunit.Abstractions;

namespace Casino.Integration.Tests.Games;

/// <summary>
/// El juego completo de punta a punta, con Postgres y RabbitMQ reales:
/// API -> (Wallet reserva) -> juego sortea con el nonce del servidor -> (Wallet liquida) -> ronda cerrada.
/// </summary>
[Collection(WalletDbDefinition.Name)]
public sealed class RouletteFlowTests(PostgresFixture db, RabbitMqFixture rabbit, ITestOutputHelper output) : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _apps = [];

    public void Dispose()
    {
        foreach (var app in _apps)
        {
            app.Dispose();
        }
    }

    /// <param name="gamesDown">Simula que el motor de juegos no esta consumiendo: nadie sortea ni responde.</param>
    private WebApplicationFactory<Program> StartApp(int ttlSeconds = 60, bool gamesDown = false)
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:casinodb", db.ConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbit.ConnectionString);
            builder.UseSetting("Fairness:MasterKey", db.MasterKey);
            builder.UseSetting("Wallet:ReservationTtlSeconds", ttlSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (gamesDown)
            {
                builder.ConfigureServices(services => services.ConfigureWolverine(options =>
                    options.Discovery.CustomizeHandlerDiscovery(query =>
                        query.Excludes.WithCondition("games down", type => type == typeof(WalletEventsHandler)))));
            }
        });
        _apps.Add(app);
        return app;
    }

    private static async Task<Guid> FundedAccountAsync(WebApplicationFactory<Program> app, long chips)
    {
        var wallet = app.Services.GetRequiredService<WalletService>();
        var accountId = await wallet.OpenAccountAsync(Guid.NewGuid());
        await wallet.CreditAsync(accountId, "initial-credit", chips);
        return accountId;
    }

    private static async Task<HttpResponseMessage> PlaceAsync(
        HttpClient client, Guid userId, Guid accountId, string betType, int[] selection, long stake, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/games/roulette/bets")
        {
            Content = JsonContent.Create(new { userId, accountId, betType, selection, stake }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> PlacedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<ITrackedSession> TrackPublishAsync(WebApplicationFactory<Program> app, object message)
    {
        Func<IMessageContext, Task> publish = async context => await context.PublishAsync(message);
        return app.Services.GetRequiredService<IHost>().TrackActivity()
            .IncludeExternalTransports()
            .Timeout(TimeSpan.FromSeconds(45))
            .ExecuteAndWaitAsync(publish);
    }

    private static Task<JsonElement> RoundAsync(HttpClient client, Guid betId) =>
        client.GetFromJsonAsync<JsonElement>($"/games/roulette/rounds/{betId}");

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, int seconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch (Exception)
            {
                // Todavia no esta listo (por ejemplo, el broker se esta recuperando).
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"No se cumplio a tiempo: {what}");
    }

    private static Task WaitForStatusAsync(HttpClient client, Guid betId, string status, int seconds = 90) =>
        WaitUntilAsync(async () => (await RoundAsync(client, betId)).GetProperty("status").GetString() == status, $"ronda {betId} en {status}", seconds);

    [Fact]
    public async Task A_bet_runs_from_placement_to_settlement_and_the_player_can_verify_the_result()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 1_000);

        var placed = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Red", [], 100, "bet-1"));
        var betId = placed.GetProperty("betId").GetGuid();
        var commitment = placed.GetProperty("commitment").GetString()!;
        Assert.Equal(0, placed.GetProperty("nonce").GetInt64());

        await WaitForStatusAsync(client, betId, "Settled");

        var round = await RoundAsync(client, betId);
        var winning = round.GetProperty("winningNumber").GetInt32();
        var payout = round.GetProperty("payout").GetInt64();
        var expectedPayout = RouletteBet.Create(RouletteBetType.Red, [], 100).PayoutFor(winning);
        Assert.Equal(expectedPayout, payout);

        var account = await app.Services.GetRequiredService<WalletService>().GetAsync(accountId);
        Assert.Equal((1_000 - 100 + payout, 0L), (account.Available, account.Reserved));

        // El jugador rota para que se revele la seed y verifica el sorteo con datos publicos.
        var rotated = await (await client.PostAsync($"/fairness/{userId}/rotate", content: null)).Content.ReadFromJsonAsync<JsonElement>();
        var retired = rotated.GetProperty("retired")[0];
        var serverSeed = retired.GetProperty("serverSeed").GetString()!;
        Assert.True(ProvablyFair.MatchesCommitment(serverSeed, commitment));
        var recomputed = new FairRng(serverSeed, retired.GetProperty("clientSeed").GetString()!, round.GetProperty("nonce").GetInt64())
            .NextInt(RouletteBet.PocketCount);
        Assert.Equal(winning, recomputed);
        output.WriteLine($"Salio el {winning}; premio {payout}; verificado contra el compromiso {commitment[..16]}...");
    }

    [Fact]
    public async Task Insufficient_funds_rejects_the_round_and_it_never_blocks_the_seed_rotation()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 50);

        var placed = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Red", [], 500, "bet-1"));
        var betId = placed.GetProperty("betId").GetGuid();

        await WaitForStatusAsync(client, betId, "Rejected");

        var round = await RoundAsync(client, betId);
        Assert.Equal("InsufficientFunds", round.GetProperty("failureReason").GetString());
        Assert.Equal(JsonValueKind.Null, round.GetProperty("winningNumber").ValueKind);
        Assert.Equal(50, (await app.Services.GetRequiredService<WalletService>().GetAsync(accountId)).Available);

        await WaitUntilAsync(
            async () => (await client.PostAsync($"/fairness/{userId}/rotate", content: null)).StatusCode == HttpStatusCode.OK,
            "rotacion permitida tras cerrar la ronda");
    }

    [Fact]
    public async Task Placing_the_same_bet_twice_reserves_once_and_a_reused_key_with_other_content_is_rejected()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 1_000);

        var first = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Even", [], 100, "same-key"));
        var second = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Even", [], 100, "same-key"));

        Assert.Equal(first.GetProperty("betId").GetGuid(), second.GetProperty("betId").GetGuid());
        Assert.False(first.GetProperty("alreadyPlaced").GetBoolean());
        Assert.True(second.GetProperty("alreadyPlaced").GetBoolean());
        Assert.Equal(first.GetProperty("nonce").GetInt64(), second.GetProperty("nonce").GetInt64());

        var betId = first.GetProperty("betId").GetGuid();
        await WaitForStatusAsync(client, betId, "Settled");
        var payout = (await RoundAsync(client, betId)).GetProperty("payout").GetInt64();
        var account = await app.Services.GetRequiredService<WalletService>().GetAsync(accountId);
        Assert.Equal(1_000 - 100 + payout, account.Available); // una sola vez: nunca -200

        var reused = await PlaceAsync(client, userId, accountId, "Even", [], 999, "same-key");
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
    }

    [Fact]
    public async Task Invalid_bets_are_rejected_before_anything_is_reserved()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var accountId = await FundedAccountAsync(app, 1_000);
        var userId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceAsync(client, userId, accountId, "Straight", [40], 10, "k1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceAsync(client, userId, accountId, "Split", [1, 5], 10, "k2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceAsync(client, userId, accountId, "Red", [], 0, "k3")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceAsync(client, userId, accountId, "Inventada", [], 10, "k4")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceAsync(client, userId, accountId, "Red", [], 10, "")).StatusCode);

        var account = await app.Services.GetRequiredService<WalletService>().GetAsync(accountId);
        Assert.Equal((1_000L, 0L), (account.Available, account.Reserved));
    }

    [Fact]
    public async Task A_duplicated_stake_reserved_message_does_not_draw_a_second_time()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 1_000);
        var placed = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Black", [], 100, "bet-1"));
        var betId = placed.GetProperty("betId").GetGuid();
        await WaitForStatusAsync(client, betId, "Settled");
        var before = await RoundAsync(client, betId);

        var session = await TrackPublishAsync(app, new StakeReserved(betId, accountId, 100, Guid.NewGuid()));

        Assert.Empty(session.Sent.MessagesOf<RoundResolved>());
        var after = await RoundAsync(client, betId);
        Assert.Equal(before.GetProperty("winningNumber").GetInt32(), after.GetProperty("winningNumber").GetInt32());
        Assert.Equal("Settled", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Forty_concurrent_bets_get_contiguous_server_assigned_nonces_and_every_balance_closes()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 10_000);

        var placements = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
            await PlacedAsync(await PlaceAsync(client, userId, accountId, "Straight", [17], 10, $"bet-{i}")))));

        var betIds = placements.Select(p => p.GetProperty("betId").GetGuid()).ToList();
        Assert.Equal(Enumerable.Range(0, 40).Select(n => (long)n), placements.Select(p => p.GetProperty("nonce").GetInt64()).Order());

        foreach (var betId in betIds)
        {
            await WaitForStatusAsync(client, betId, "Settled");
        }

        var rounds = await Task.WhenAll(betIds.Select(id => RoundAsync(client, id)));
        var totalPayout = rounds.Sum(r => r.GetProperty("payout").GetInt64());
        var account = await app.Services.GetRequiredService<WalletService>().GetAsync(accountId);
        Assert.Equal((10_000 - (40 * 10) + totalPayout, 0L), (account.Available, account.Reserved));
        Assert.All(rounds, r => Assert.Equal(r.GetProperty("winningNumber").GetInt32() == 17 ? 360 : 0, r.GetProperty("payout").GetInt64()));
        output.WriteLine($"40 apuestas liquidadas; premios totales {totalPayout}");
    }

    [Fact]
    public async Task When_the_game_never_answers_the_wallet_gives_the_stake_back_and_a_late_result_cannot_pay()
    {
        using var app = StartApp(ttlSeconds: 2, gamesDown: true);
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 1_000);
        var wallet = app.Services.GetRequiredService<WalletService>();

        var placed = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Red", [], 100, "bet-1"));
        var betId = placed.GetProperty("betId").GetGuid();

        // La Wallet reserva, el juego nunca contesta, y al vencer la reserva las fichas vuelven al jugador.
        await WaitUntilAsync(async () => (await wallet.GetAsync(accountId)).Reserved == 100, "reserva abierta", 30);
        await WaitUntilAsync(async () => (await wallet.GetAsync(accountId)).Reserved == 0, "reserva liberada por vencimiento", 60);
        Assert.Equal(1_000, (await wallet.GetAsync(accountId)).Available);

        // Si el resultado llega despues del vencimiento, la Wallet lo rechaza: no se paga un premio sobre una reserva ya devuelta.
        var late = await TrackPublishAsync(app, new RoundResolved(betId, accountId, 200));

        var rejected = late.Sent.SingleMessage<StakeSettlementRejected>();
        Assert.Equal("ReservationNotOpen", rejected.Reason);
        Assert.Equal(1_000, (await wallet.GetAsync(accountId)).Available);
    }

    [Fact]
    public async Task Expiry_and_late_settlement_void_the_round_and_free_the_seed_rotation()
    {
        using var app = StartApp(gamesDown: true);
        var client = app.CreateClient();
        var roulette = app.Services.GetRequiredService<RouletteService>();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 1_000);

        // Ronda 1: vence la reserva antes del sorteo.
        var expired = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Red", [], 100, "bet-expired"));
        var expiredId = expired.GetProperty("betId").GetGuid();
        await roulette.OnStakeReleasedAsync(new StakeReleased(expiredId, accountId, 100, Guid.NewGuid()));

        // Ronda 2: el juego sortea, pero la liquidacion llega tarde y la Wallet la rechaza.
        var late = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Black", [], 100, "bet-late"));
        var lateId = late.GetProperty("betId").GetGuid();
        await roulette.OnStakeReservedAsync(new StakeReserved(lateId, accountId, 100, Guid.NewGuid()));
        await roulette.OnSettlementRejectedAsync(new StakeSettlementRejected(lateId, accountId, "ReservationNotOpen"));

        var expiredRound = await RoundAsync(client, expiredId);
        Assert.Equal("Voided", expiredRound.GetProperty("status").GetString());
        Assert.Equal("ReservationExpired", expiredRound.GetProperty("failureReason").GetString());
        Assert.Equal(JsonValueKind.Null, expiredRound.GetProperty("winningNumber").ValueKind);

        var lateRound = await RoundAsync(client, lateId);
        Assert.Equal("Voided", lateRound.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, lateRound.GetProperty("winningNumber").ValueKind); // el sorteo queda para auditoria

        // Ninguna apuesta pendiente: se puede rotar.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/fairness/{userId}/rotate", content: null)).StatusCode);
    }

    [Fact]
    public async Task A_broker_outage_in_the_middle_of_betting_loses_no_bet_and_blocks_rotation_until_they_close()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        var userId = Guid.NewGuid();
        var accountId = await FundedAccountAsync(app, 1_000);
        List<Guid> betIds = [];

        await rabbit.StopBrokerAsync();
        try
        {
            // Con el broker caido las apuestas se aceptan igual: quedan en el outbox.
            for (var i = 0; i < 5; i++)
            {
                var placed = await PlacedAsync(await PlaceAsync(client, userId, accountId, "Odd", [], 10, $"bet-{i}"));
                betIds.Add(placed.GetProperty("betId").GetGuid());
            }

            var blocked = await client.PostAsync($"/fairness/{userId}/rotate", content: null);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("PendingBets", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        }
        finally
        {
            await rabbit.StartBrokerAsync();
        }

        foreach (var betId in betIds)
        {
            await WaitForStatusAsync(client, betId, "Settled", seconds: 150);
        }

        var rounds = await Task.WhenAll(betIds.Select(id => RoundAsync(client, id)));
        var totalPayout = rounds.Sum(r => r.GetProperty("payout").GetInt64());
        var account = await app.Services.GetRequiredService<WalletService>().GetAsync(accountId);
        Assert.Equal((1_000 - 50 + totalPayout, 0L), (account.Available, account.Reserved));
        Assert.Equal(Enumerable.Range(0, 5).Select(n => (long)n), rounds.Select(r => r.GetProperty("nonce").GetInt64()).Order());

        await WaitUntilAsync(
            async () => (await client.PostAsync($"/fairness/{userId}/rotate", content: null)).StatusCode == HttpStatusCode.OK,
            "rotacion permitida cuando cerraron las apuestas");
    }
}
