var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
// Una base de datos POR SERVICIO: ningun servicio lee las tablas de otro. Todas viven en el mismo servidor de Postgres (en la nube
// serian servidores o instancias separadas, sin cambiar el codigo).
var walletDb = postgres.AddDatabase("walletdb");
var usersDb = postgres.AddDatabase("usersdb");
var gamesDb = postgres.AddDatabase("gamesdb");

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

// Servicio de la Wallet: dueño de las fichas. No se expone al navegador: se llega a el por el gateway (la API) o por mensajes.
var wallet = builder.AddProject<Projects.Casino_WalletService>("wallet")
    .WithReference(walletDb).WaitFor(walletDb)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithEnvironment("Authentication__Authority", keycloakAuthority);

// Servicio de juegos: plataforma provably fair y los juegos habilitados (ruleta, tragamonedas...). Tampoco se expone al navegador.
// La clave maestra de las semillas (Fairness:MasterKey) es un secreto de usuario de ESTE proyecto, no de la API.
var games = builder.AddProject<Projects.Casino_GamesService>("games")
    .WithReference(gamesDb).WaitFor(gamesDb)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithEnvironment("Authentication__Authority", keycloakAuthority);

builder.AddProject<Projects.Casino_Api>("api")
    .WithReference(usersDb).WaitFor(usersDb)
    .WithReference(wallet).WaitFor(wallet) // el gateway reenvia /wallet y /backoffice/wallet a este servicio
    .WithReference(games).WaitFor(games) // ... y /games, /fairness y /backoffice/games a este
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
