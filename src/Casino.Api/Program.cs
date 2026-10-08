using Casino.Modules.Games.Api;
using Casino.Modules.Users.Api;
using Casino.Modules.Wallet.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddCasinoInfrastructure();
builder.AddCasinoAuthentication();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddUsersModule();
builder.Services.AddWalletModule();
builder.Services.AddGamesModule();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/", () => "Casino API");

app.UseAuthentication();
app.UseAuthorization();
app.UsePlayerProvisioning();

// Todos los endpoints de negocio exigen un token valido y el rol correspondiente (ver cada modulo).
app.MapUsersModule();
app.MapWalletModule();
app.MapGamesModule();

app.Run();

public partial class Program;
