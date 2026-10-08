# ADR 0002 - Coreografia de eventos con Wolverine y RabbitMQ

- **Estado:** aceptada
- **Fecha:** 2026-10-08

## Contexto
La plataforma tendra varios juegos (ruleta, tragamonedas, blackjack, poker) que mueven fichas a traves de la Wallet. Una saga
central que conozca todos los pasos de cada juego creceria con cada juego nuevo. Ademas, un saldo no puede perder ni duplicar
fichas aunque el broker o la base fallen en el peor momento.

## Decision
- **Coreografia** en lugar de orquestacion: cada modulo reacciona a mensajes y publica los suyos. La Wallet es dueña de las fichas
  (reserva, liquida y libera por vencimiento); cada juego es dueño de su ronda y solo informa `RoundResolved`.
  Agregar un juego es agregar un modulo que habla el mismo idioma, sin tocar la Wallet.
- **Contratos** en `Casino.Contracts`: records sin logica ni dependencias. Ordenes en imperativo, hechos en pasado.
- **Wolverine** sobre Marten para mensajeria y **RabbitMQ** como broker (Azure Service Bus en la nube mas adelante).
- **Outbox transaccional:** `WalletService` publica sus hechos con `IMartenOutbox` enlistado en la misma sesion que los eventos.
  Si el commit falla no sale nada; si sale bien, el mensaje esta garantizado aunque RabbitMQ este caido.
- **Inbox durable** en los listeners y consumidores **idempotentes**: la entrega es "al menos una vez" y cada orden deriva su
  `IdempotencyKey` de la apuesta (`reserve:{betId}`, `settle:{betId}`), asi un duplicado no tiene efecto.
- **Topologia:** una cola por orden hacia un dueño (`wallet.commands`) y un exchange `fanout` para los hechos (`wallet.events`)
  con una cola por consumidor (`games.wallet-events`, `realtime.wallet-events`).

## Consecuencias
- El flujo completo de una apuesta ya no esta en un solo lugar: se compensa con trazas de OpenTelemetry (fase 4) y pruebas de
  extremo a extremo.
- Wolverine 6 ya no incluye el compilador en tiempo de ejecucion: se agrega `WolverineFx.RuntimeCompilation` (o se pre-genera el
  codigo de los handlers para produccion).
- La regla delicada "una ronda ya sorteada nunca se libera" depende de tiempos: el juego debe resolver bastante antes de que
  venza la reserva.
