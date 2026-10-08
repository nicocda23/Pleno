using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Casino.Integration.Tests.Users;

/// <summary>
/// Un Keycloak real con el realm de la plataforma importado (el MISMO archivo que usa el AppHost).
/// Arranca bajo demanda: tarda un minuto, asi que solo lo paga quien lo usa.
/// </summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    private const string Image = "quay.io/keycloak/keycloak:26.7.0";
    private readonly Lock _lock = new();
    private Task? _start;
    private IContainer? _container;

    public string Authority { get; private set; } = string.Empty;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public Task EnsureStartedAsync()
    {
        lock (_lock)
        {
            return _start ??= StartAsync();
        }
    }

    private async Task StartAsync()
    {
        var realmFile = Path.Combine(AppContext.BaseDirectory, "realms", "casino-realm.json");
        var container = new ContainerBuilder(Image)
            .WithCommand("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)))
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithResourceMapping(realmFile, "/opt/keycloak/data/import/")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(8080)
                .ForPath("/realms/casino/.well-known/openid-configuration")))
            .Build();
        await container.StartAsync();

        _container = container;
        Authority = $"http://localhost:{container.GetMappedPublicPort(8080)}/realms/casino";
    }

    /// <summary>Pide un token como lo haria un cliente de pruebas (usuario y contraseña de desarrollo del realm).</summary>
    public async Task<string> TokenAsync(string username, string password)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            $"{Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "casino-tests",
                ["username"] = username,
                ["password"] = password,
            }));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }
}
