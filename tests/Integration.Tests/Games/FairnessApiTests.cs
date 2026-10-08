using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Casino.Integration.Tests.Wallet;
using Casino.Modules.Games.Fairness;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Casino.Integration.Tests.Games;

[Collection(WalletDbDefinition.Name)]
public sealed class FairnessApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public FairnessApiTests(PostgresFixture db)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:casinodb", db.ConnectionString);
            builder.UseSetting("Fairness:MasterKey", db.MasterKey);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Public_info_exposes_the_commitment_but_never_the_active_server_seed()
    {
        var userId = Guid.NewGuid();

        var response = await _client.GetAsync($"/fairness/{userId}");
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^[0-9a-f]{64}$", body.GetProperty("active").GetProperty("commitment").GetString()!);
        Assert.Equal(0, body.GetProperty("active").GetProperty("nextNonce").GetInt64());
        Assert.DoesNotContain("encrypted", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serverSeed", body.GetProperty("active").ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rotating_reveals_a_seed_that_matches_the_commitment_published_before()
    {
        var userId = Guid.NewGuid();
        var before = await _client.GetFromJsonAsync<JsonElement>($"/fairness/{userId}");
        var commitment = before.GetProperty("active").GetProperty("commitment").GetString()!;

        var response = await _client.PostAsJsonAsync($"/fairness/{userId}/rotate", new { clientSeed = "mi-semilla-nueva" });
        var after = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retired = after.GetProperty("retired")[0];
        Assert.True(ProvablyFair.MatchesCommitment(retired.GetProperty("serverSeed").GetString()!, commitment));
        Assert.Equal("mi-semilla-nueva", after.GetProperty("active").GetProperty("clientSeed").GetString());
        Assert.NotEqual(commitment, after.GetProperty("active").GetProperty("commitment").GetString());
    }

    [Fact]
    public async Task Rotating_without_a_body_generates_a_random_client_seed()
    {
        var userId = Guid.NewGuid();

        var response = await _client.PostAsync($"/fairness/{userId}/rotate", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^[0-9a-f]{16}$", body.GetProperty("active").GetProperty("clientSeed").GetString()!);
    }

    [Fact]
    public async Task An_invalid_client_seed_is_a_bad_request()
    {
        var response = await _client.PostAsJsonAsync($"/fairness/{Guid.NewGuid()}/rotate", new { clientSeed = new string('x', 65) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidClientSeed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_api_accepts_no_way_to_choose_a_nonce_or_a_server_seed()
    {
        var userId = Guid.NewGuid();
        var before = await _client.GetFromJsonAsync<JsonElement>($"/fairness/{userId}");

        // Aunque el cliente mande campos extra, el servidor los ignora: ni nonce ni server seed se pueden imponer.
        var response = await _client.PostAsJsonAsync(
            $"/fairness/{userId}/rotate",
            new { clientSeed = "ok", nonce = 999, serverSeed = new string('a', 64), nextNonce = 999 });
        var after = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, after.GetProperty("active").GetProperty("nextNonce").GetInt64());
        Assert.NotEqual(
            new string('a', 64),
            after.GetProperty("retired")[0].GetProperty("serverSeed").GetString());
        Assert.NotEqual(
            before.GetProperty("active").GetProperty("commitment").GetString(),
            after.GetProperty("active").GetProperty("commitment").GetString());
    }
}
