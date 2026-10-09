# ADR 0004 - Tiempo real con SignalR, RabbitMQ y Redis

- **Estado:** aceptada
- **Fecha:** 2026-10-08

## Contexto
El jugador tiene que ver su saldo y el resultado de cada ronda en vivo, aunque haya varias instancias de la API. Los datos
nacen en otros modulos (Wallet y juegos) y viajan por mensajes.

## Decision
- **Un modulo `Realtime`** consume mensajes del broker y los empuja al navegador con **SignalR**. No consulta a la Wallet ni a los
  juegos: solo traduce hechos (`BalanceChanged`, `RoundClosed`) en avisos.
- **Un exchange por tema, una cola por consumidor.** Los handlers de Wolverine son globales a la aplicacion: se ejecutan
  sin importar por que cola llego el mensaje. Si un exchange copiara todos los hechos a todas las colas, un handler que tambien
  maneja otro modulo correria dos veces (un aviso duplicado al navegador). Por eso: `wallet.stake-events` -> `games.wallet-events`,
  `wallet.balance-events` -> `realtime.wallet-events`, `games.events` -> `realtime.game-events`.
- **Grupo por cuenta, decidido por el servidor.** Cada conexion entra al grupo de su cuenta derivada del token. El hub no expone
  metodos al cliente.
- **Token en la query solo para el hub.** Un navegador no puede enviar cabeceras en un WebSocket. El riesgo (un token en una URL
  puede quedar en logs de proxies) se mitiga con tokens de 5 minutos y limitando el uso de la query a `/hubs/player`.
- **Redis como backplane** cuando hay `ConnectionStrings:redis`: el aviso generado en una instancia llega a todas.
- **Verificacion de jugadas en el navegador** (`/verify`): una tercera implementacion del algoritmo, estatica y publica, que no
  llama al servidor. Cuantas mas implementaciones independientes coincidan con los mismos vectores, mas confiable es el esquema.

## Consecuencias
- Los avisos son "al menos una vez" y pueden llegar fuera de orden: el front usa `version` para descartar los viejos y vuelve a
  consultar el estado por HTTP al reconectar.
- Agregar un consumidor de un hecho existente exige su propia cola enlazada al exchange del tema; si un modulo nuevo maneja un
  tipo de mensaje que ya maneja otro, hay que pensar si se ejecutaran ambos.
- El cableado de Redis en Aspire (cadena de conexion con TLS y clave) no pudo probarse con `aspire run` en esta maquina.
