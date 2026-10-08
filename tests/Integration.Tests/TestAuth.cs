using System.Net.Http.Headers;
using System.Security.Cryptography;
using Casino.Integration.Tests.Wallet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Casino.Integration.Tests;

/// <summary>
/// Emite tokens JWT de prueba con una clave aleatoria por corrida (nunca se guarda en disco) y configura la API para
/// confiar en ella. La validacion que corre es la REAL de JwtBearer: firma, emisor, destinatario, vigencia y el
/// mapeo de roles de Keycloak. Solo cambia de donde sale la clave publica.
/// </summary>
public static class TestAuth
{
    public const string Issuer = "https://test-issuer.local/realms/casino";
    public const string Audience = "casino-api";

    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>Un token como el que entregaria Keycloak: "sub", "preferred_username" y roles dentro de "realm_access".</summary>
    public static string Token(
        Guid userId,
        string username = "tester",
        string[]? roles = null,
        string audience = Audience,
        TimeSpan? lifetime = null,
        SecurityKey? signingKey = null,
        string? subject = null,
        object? realmAccess = null)
    {
        var now = DateTime.UtcNow;
        var lifespan = lifetime ?? TimeSpan.FromMinutes(5);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            // Un token ya vencido necesita fecha de inicio anterior a la de vencimiento.
            NotBefore = lifespan < TimeSpan.Zero ? now.Add(lifespan).AddMinutes(-1) : now,
            Expires = now.Add(lifespan),
            IssuedAt = now,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject ?? userId.ToString(),
                ["preferred_username"] = username,
                ["realm_access"] = realmAccess ?? new Dictionary<string, object> { ["roles"] = roles ?? [Casino.BuildingBlocks.Roles.Player] },
            },
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static SymmetricSecurityKey NewForeignKey() => new(RandomNumberGenerator.GetBytes(32));

    /// <summary>Configura la API de prueba para aceptar los tokens de <see cref="Token"/>.</summary>
    public static void Configure(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Authentication:Authority", Issuer);
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { Issuer = Issuer });
            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            options.TokenValidationParameters.ValidIssuer = Issuer;
        }));
    }

    public static HttpClient ClientFor(this WebApplicationFactory<Program> app, Guid userId, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(app);

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Token(userId, roles: roles.Length == 0 ? [Casino.BuildingBlocks.Roles.Player] : roles));
        return client;
    }

    /// <summary>La API completa con Postgres (y RabbitMQ si se indica) reales y autenticacion de prueba.</summary>
    public static WebApplicationFactory<Program> StartApp(
        PostgresFixture db,
        RabbitMqFixture? rabbit = null,
        int? welcomeChips = 0,
        Action<IWebHostBuilder>? customize = null,
        bool testIssuer = true)
    {
        ArgumentNullException.ThrowIfNull(db);

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:casinodb", db.ConnectionString);
            builder.UseSetting("Fairness:MasterKey", db.MasterKey);
            if (rabbit is not null)
            {
                builder.UseSetting("ConnectionStrings:rabbitmq", rabbit.ConnectionString);
            }

            if (welcomeChips is { } chips)
            {
                builder.UseSetting("Wallet:WelcomeChips", chips.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (testIssuer)
            {
                Configure(builder);
            }

            customize?.Invoke(builder);
        });
    }
}
