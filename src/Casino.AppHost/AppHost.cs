var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
var casinoDb = postgres.AddDatabase("casinodb");

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin();

// Keycloak: servidor de identidad (registro, login y tokens). Puerto fijo: el emisor (issuer) de los tokens incluye la URL,
// y el front y la API tienen que coincidir. La contraseña del administrador NO esta en el codigo: es un secreto de usuario
// (dotnet user-secrets set "Parameters:keycloak-admin-password" <valor> --project src/Casino.AppHost).
const int KeycloakPort = 8080;
var keycloakAuthority = string.Concat("http://localhost:", KeycloakPort.ToString(System.Globalization.CultureInfo.InvariantCulture), "/realms/casino");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.7.0")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithHttpEndpoint(port: KeycloakPort, targetPort: 8080, name: "http")
    .WithBindMount("./realms", "/opt/keycloak/data/import", isReadOnly: true)
    // El volumen va en /opt/keycloak/data (que existe en la imagen con el dueño correcto), no en una subcarpeta que Docker crearia como root.
    .WithVolume("keycloak-data", "/opt/keycloak/data");


// Origen del front. Keycloak solo acepta redirecciones a esta URL (ver realms/casino-realm.json), asi que el puerto es fijo.
const string WebOrigin = "http://localhost:5173";

builder.AddProject<Projects.Casino_Api>("api")
    .WithReference(casinoDb).WaitFor(casinoDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithEnvironment("Authentication__Authority", keycloakAuthority)
    .WithEnvironment("Cors__AllowedOrigins__0", WebOrigin)
    .WithExternalHttpEndpoints();

// El front (React + Vite). Se levanta con `npm run dev`; las URLs de la API y de Keycloak son las de desarrollo por defecto.
builder.AddViteApp("web", "../../web")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5173;
        endpoint.IsProxied = false;
    });

builder.Build().Run();
