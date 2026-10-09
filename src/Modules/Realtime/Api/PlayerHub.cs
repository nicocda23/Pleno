using Casino.BuildingBlocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Casino.Modules.Realtime.Api;

/// <summary>
/// Canal en vivo de un jugador. Exige token con rol player. El servidor decide a que grupo entra cada conexion
/// (el de SU cuenta, derivada del token) y el hub no expone ningun metodo invocable por el cliente: el navegador solo escucha.
/// </summary>
[Authorize(Roles = Roles.Player)]
public sealed class PlayerHub : Hub
{
    public const string Path = "/hubs/player";

    public static string GroupFor(Guid accountId) => $"account:{accountId:N}";

    public override async Task OnConnectedAsync()
    {
        var accountId = PlayerIds.WalletAccountFor(Context.User!.GetUserId());
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(accountId));
        await base.OnConnectedAsync();
    }
}
