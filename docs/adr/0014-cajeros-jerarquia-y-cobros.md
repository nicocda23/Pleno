# ADR 0014 - Cajeros: jerarquia, cargas por transferencia, comision y cobros

- **Estado:** propuesta
- **Fecha:** 2026-10-10

## Contexto
Hoy el unico rol que mueve fichas "desde afuera" es `backoffice`: puede acreditar a cualquier jugador (`CreditAsync`, siempre con `IdempotencyKey` y auditoria). Queremos simular un mundo
mas natural: **cajeros** que cargan fichas a los jugadores de su jurisdiccion, una **jerarquia** (a quien pertenece cada jugador), un **incentivo** para que la carga ocurra y un
**proceso de cobro** (el camino inverso). Siguen valiendo las reglas del proyecto: montos enteros (`long`), el saldo se **deriva** de asientos de partida doble, toda operacion que mueve
fichas lleva `IdempotencyKey`, la identidad sale del token y la PII no va a logs.

## Decision
- **Rol `cashier` (cajero) en Keycloak; la jerarquia vive en nuestra base, no en Keycloak.** Keycloak solo responde "quien es cajero". Quien depende de quien es un dato del modulo de usuarios: cada jugador
  guarda a que cajero pertenece (`CashierId`, un solo padre: un arbol simple, no un grafo). Un cajero solo ve y carga a los jugadores de su rama; `backoffice` ve todo. El cajero tambien es
  un jugador (conserva `player`) pero opera con su **propia cuenta de fichas**.
- **Las fichas de una carga salen del saldo del cajero.** Backoffice le acredita fichas al cajero (como hoy a cualquiera) y el cajero las reparte: cargar es una **transferencia** cajero → jugador, no
  una emision. Asi nadie crea fichas de la nada, la liquidez del cajero es limitada y todo cuadra por partida doble. Hace falta una operacion nueva de la Wallet, `Transfer(from, to, key, amount)`,
  atomica (un debito y un credito en la misma transaccion), idempotente y con auditoria (quien, a quien, cuanto, cuando; solo ids).
- **Incentivo: comision del cajero.** El cajero se queda con un **porcentaje de lo que carga**, en fichas, con tope por carga y por dia. Los jugadores no reciben bonus por una carga. La
  comision es otra transferencia (de la casa o del propio monto, a definir en la fase B) y queda auditada como movimiento aparte.
- **Cobro (extraccion): el camino inverso, con el protocolo que ya existe.** El jugador pide retirar: sus fichas se **reservan** (mismo protocolo reservar → resolver → liquidar de los juegos). Su
  cajero (o backoffice) **aprueba y paga**; al liquidar se debitan las fichas del jugador y se **transfieren al cajero**, que cierra el ciclo. Si no se aprueba o vence, la reserva se **libera** y las
  fichas vuelven al jugador. La doble aprobacion para montos altos sigue siendo de la fase 5.
- **Por fases, cada una con tests (unitarios + Testcontainers) y su ADR de cierre:** **A** rol, jerarquia, cuenta del cajero y `Transfer`; **B** comision; **C** pedidos de retiro y aprobacion.

## Consecuencias
- La Wallet gana una operacion (transferencia) y, con ella, el riesgo de dos cuentas en una transaccion: hay que probar concurrencia, idempotencia y saldos que cierran (suma total de fichas constante).
- Un cajero sin fichas no puede cargar: es una restriccion buscada, pero necesita una pantalla para que backoffice le recargue.
- La jerarquia de un solo padre es simple pero rigida: cambiar a un jugador de cajero es una operacion de backoffice auditada; subcajeros (niveles) quedan para despues.
- Esto es un juego con fichas ficticias. Si algun dia las fichas tuvieran valor real, habria que revisar aspectos legales y de prevencion de lavado antes de seguir.
- Queda para despues: niveles de cajeros (cajero de cajeros), referidos entre jugadores, limites configurables por cajero y reportes de liquidacion.
