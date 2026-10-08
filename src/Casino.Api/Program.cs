using Casino.Modules.Games.Api;
using Casino.Modules.Realtime.Api;
using Casino.Modules.Users.Api;
using Casino.Modules.Wallet.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddCasinoInfrastructure();
builder.AddCasinoAuthentication();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// SignalR: si hay Redis, es el "backplane" que reparte los avisos entre todas las instancias de la API.
// Sin el, un aviso generado en una instancia no llegaria a un navegador conectado a otra.
var signalR = builder.Services.AddSignalR();
var redis = builder.Configuration.GetConnectionString("redis");
if (!string.IsNullOrWhiteSpace(redis))
{
    signalR.AddStackExchangeRedis(redis);
}

builder.Services.AddUsersModule();
builder.Services.AddWalletModule();
builder.Services.AddGamesModule();
builder.Services.AddRealtimeModule();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/", () => "Casino API");

// Archivos publicos (wwwroot), por ejemplo la pagina de verificacion de jugadas. No llevan secretos.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UsePlayerProvisioning();

// Todos los endpoints de negocio exigen un token valido y el rol correspondiente (ver cada modulo).
app.MapUsersModule();
app.MapWalletModule();
app.MapGamesModule();
app.MapRealtimeModule();

app.Run();

public partial class Program;
