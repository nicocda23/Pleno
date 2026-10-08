using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Casino.Integration.Tests.Wallet;

[Collection(WalletDbDefinition.Name)]
public sealed class WalletApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WalletApiTests(PostgresFixture db)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:casinodb", db.ConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> OpenAccountAsync()
    {
        var response = await _client.PostAsJsonAsync("/wallet/accounts", new { userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accountId").GetGuid();
    }

    private Task<HttpResponseMessage> PostAsync(Guid accountId, string operation, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/wallet/accounts/{accountId}/{operation}")
        {
            Content = JsonContent.Create(body),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return _client.SendAsync(request);
    }

    private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    [Fact]
    public async Task Bet_lifecycle_over_http_updates_the_balance()
    {
        var accountId = await OpenAccountAsync();
        var reservationId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(accountId, "credit", new { amount = 1_000 }, "k-credit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(accountId, "reserve", new { reservationId, stake = 100 }, "k-reserve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(accountId, "settle", new { reservationId, payout = 250 }, "k-settle")).StatusCode);

        var account = await _client.GetFromJsonAsync<JsonElement>($"/wallet/accounts/{accountId}");
        Assert.Equal(1_150, account.GetProperty("available").GetInt64());
        Assert.Equal(0, account.GetProperty("reserved").GetInt64());
    }

    [Fact]
    public async Task Repeating_a_request_returns_the_original_transaction_and_flags_the_duplicate()
    {
        var accountId = await OpenAccountAsync();

        var first = await (await PostAsync(accountId, "credit", new { amount = 500 }, "k1")).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await PostAsync(accountId, "credit", new { amount = 500 }, "k1")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(first.GetProperty("isDuplicate").GetBoolean());
        Assert.True(second.GetProperty("isDuplicate").GetBoolean());
        Assert.Equal(first.GetProperty("transactionId").GetGuid(), second.GetProperty("transactionId").GetGuid());

        var account = await _client.GetFromJsonAsync<JsonElement>($"/wallet/accounts/{accountId}");
        Assert.Equal(500, account.GetProperty("available").GetInt64());
    }

    [Fact]
    public async Task Errors_are_mapped_to_meaningful_status_codes()
    {
        var accountId = await OpenAccountAsync();
        await PostAsync(accountId, "credit", new { amount = 100 }, "k-credit");

        var missingKey = await PostAsync(accountId, "credit", new { amount = 100 }, key: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        var reusedKey = await PostAsync(accountId, "credit", new { amount = 999 }, "k-credit");
        Assert.Equal(HttpStatusCode.Conflict, reusedKey.StatusCode);
        Assert.Equal("IdempotencyKeyReused", await ProblemTitleAsync(reusedKey));

        var insufficient = await PostAsync(accountId, "reserve", new { reservationId = Guid.NewGuid(), stake = 5_000 }, "k-big");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, insufficient.StatusCode);

        var invalidAmount = await PostAsync(accountId, "credit", new { amount = -5 }, "k-negative");
        Assert.Equal(HttpStatusCode.BadRequest, invalidAmount.StatusCode);

        var unknownAccount = await PostAsync(Guid.NewGuid(), "credit", new { amount = 10 }, "k-ghost");
        Assert.Equal(HttpStatusCode.NotFound, unknownAccount.StatusCode);
    }

    [Fact]
    public async Task Audit_endpoint_rebuilds_the_balance_at_a_date()
    {
        var accountId = await OpenAccountAsync();
        await PostAsync(accountId, "credit", new { amount = 700 }, "k-credit");

        static string Iso(DateTimeOffset date) => Uri.EscapeDataString(date.UtcDateTime.ToString("o"));

        var now = await _client.GetFromJsonAsync<JsonElement>(
            $"/wallet/accounts/{accountId}/balance?asOf={Iso(DateTimeOffset.UtcNow.AddMinutes(5))}");
        Assert.Equal(700, now.GetProperty("total").GetInt64());

        var beforeOpening = await _client.GetAsync(
            $"/wallet/accounts/{accountId}/balance?asOf={Iso(DateTimeOffset.UtcNow.AddDays(-1))}");
        Assert.Equal(HttpStatusCode.NotFound, beforeOpening.StatusCode);
    }
}
