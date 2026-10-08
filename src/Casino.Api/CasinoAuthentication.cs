using System.Security.Claims;
using Casino.BuildingBlocks;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

/// <summary>
/// Autenticacion con tokens JWT emitidos por Keycloak (OpenID Connect). La API NUNCA ve contraseñas: solo valida la firma,
/// el emisor, el destinatario (audience) y la vigencia del token.
/// </summary>
internal static class CasinoAuthentication
{
    public const string Audience = "casino-api";

    public static WebApplicationBuilder AddCasinoAuthentication(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["Authentication:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException(
                "Falta 'Authentication:Authority' (la URL del realm de Keycloak, por ejemplo http://localhost:8080/realms/casino). La inyecta Aspire.");
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = Audience;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.MapInboundClaims = false; // conserva "sub" y "preferred_username" tal cual vienen en el token
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Sin un "sub" valido no hay forma de identificar al jugador: el token no sirve.
                        if (context.Principal is not { } principal || !principal.TryGetUserId(out _))
                        {
                            context.Fail("El token no trae un sub valido.");
                            return Task.CompletedTask;
                        }

                        MapRealmRoles(context.Principal);
                        return Task.CompletedTask;
                    },
                };
            });
        builder.Services.AddAuthorization();
        return builder;
    }

    /// <summary>Keycloak entrega los roles del realm dentro del claim JSON "realm_access": { "roles": [...] }. Se pasan a roles de .NET.</summary>
    private static void MapRealmRoles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity
            || principal.FindFirst("realm_access")?.Value is not { } realmAccess)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(realmAccess);
            if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray().Select(r => r.GetString()).OfType<string>())
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }
        catch (JsonException)
        {
            // Un claim mal formado no concede ningun rol: queda sin permisos.
        }
    }
}
