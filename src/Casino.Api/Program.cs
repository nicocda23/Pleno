using Casino.Modules.Wallet.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWalletModule();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/", () => "Casino API");

// La Wallet no tiene autenticacion todavia (fase 3): solo se expone en Development.
if (app.Environment.IsDevelopment())
{
    app.MapWalletModule();
}

app.Run();

public partial class Program;
