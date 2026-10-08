using Casino.Modules.Games.Api;
using Casino.Modules.Wallet.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddCasinoInfrastructure();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddWalletModule();
builder.Services.AddGamesModule();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/", () => "Casino API");

// Los modulos no tienen autenticacion todavia (fase 3): solo se exponen en Development.
if (app.Environment.IsDevelopment())
{
    app.MapWalletModule();
    app.MapGamesModule();
}

app.Run();

public partial class Program;
