# Tiempo real y verificacion de jugadas

## Como se conecta el front (SignalR)
- URL del hub: `/hubs/player`. Exige un token con rol `player`.
- Un navegador no puede poner cabeceras en un WebSocket, asi que el token viaja en la query: `?access_token=<token>`
  (el cliente de SignalR para JavaScript lo hace solo con `accessTokenFactory`). La API acepta ese token **solo** en la ruta del hub.
- El servidor mete cada conexion en el grupo de **su** cuenta (derivada del token). El hub no tiene metodos invocables: el
  navegador solo escucha. No hay forma de unirse al grupo de otro jugador.

```js
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5188/hubs/player", { accessTokenFactory: () => accessToken })
  .withAutomaticReconnect()
  .build();

connection.on("balanceChanged", ({ available, reserved, version }) => { /* actualizar saldo */ });
connection.on("roundClosed", ({ betId, game, status, winningNumber, stake, payout, failureReason }) => { /* mostrar resultado */ });
await connection.start();
```

## Avisos
| Evento | Cuando | Datos |
| --- | --- | --- |
| `balanceChanged` | Cambia el saldo de la cuenta (reserva, premio, ajuste...) | `available`, `reserved`, `version` |
| `roundClosed` | Una ronda termina | `betId`, `game`, `status` (`Settled`, `Rejected` o `Voided`), `winningNumber`, `stake`, `payout`, `failureReason` |

- La entrega es "al menos una vez" y puede llegar **fuera de orden** entre instancias: usar `version` y descartar un
  `balanceChanged` con version menor o igual a la ya mostrada.
- Los avisos solo cubren lo que pasa **mientras hay conexion**. Al conectar o reconectar, el front debe pedir el estado actual por HTTP
  (`GET /wallet/me`, `GET /games/roulette/rounds`) y despues seguir con los avisos.
- `payout` de `roundClosed` es 0 salvo que `status` sea `Settled`.

## Varias instancias de la API
Con `ConnectionStrings:redis` configurado, SignalR usa Redis como **backplane**: un aviso generado en una instancia llega a los
navegadores conectados a cualquier otra. Sin Redis, cada instancia solo conoce a sus propias conexiones.

## Pagina de verificacion de jugadas
`/verify/index.html` (publica, sin iniciar sesion). El jugador pega el compromiso, la server seed **revelada**, su client seed,
el nonce y el numero que salio. Todo se calcula en su navegador (WebCrypto): no se envia nada al servidor.

1. Comprueba que `SHA-256(server seed)` es el compromiso publicado antes de jugar.
2. Recalcula el numero con `HMAC-SHA256` y compara.

La logica esta en `src/Casino.Api/wwwroot/verify/verify.js`, una **tercera implementacion** del algoritmo de
[provably-fair.md](provably-fair.md) (despues de C# y del Node original). Sus pruebas (`node --test "tests/web/*.test.mjs"`)
usan los mismos vectores que las de C#.

La server seed solo se revela al **rotar** las seeds (`POST /fairness/me/rotate`).
