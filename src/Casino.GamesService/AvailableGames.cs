using Casino.Modules.Games.Platform;
using Casino.Modules.Games.Roulette;
using Casino.Modules.Games.Slots;

namespace Casino.Hosts.Games;

/// <summary>
/// Los juegos que este servicio sabe cargar. Para sumar un juego se agrega aca (una linea); para sacarlo, se quita o se deja afuera con
/// <c>Games:Enabled</c>. Las pruebas de contrato recorren esta lista, asi que todo juego nuevo queda verificado sin tocar los tests.
/// </summary>
public static class AvailableGames
{
    public static IGameModule[] All() => [new RouletteGameModule(), new SlotsGameModule()];
}
