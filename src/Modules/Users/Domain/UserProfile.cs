namespace Casino.Modules.Users.Domain;

/// <summary>
/// Perfil minimo del jugador. El id es el "sub" del token de Keycloak. A proposito NO guarda email, nombre ni documento:
/// esos datos personales viven solo en Keycloak y no se copian a nuestra base, a los logs ni a las trazas.
/// </summary>
public sealed class UserProfile
{
    public Guid Id { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
}
