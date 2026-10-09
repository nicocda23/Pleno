# Pruebas de carga con k6

Miden cuanto aguanta el sistema con muchos jugadores a la vez y sirven para comparar **antes y despues** de cambios de arquitectura
(por ejemplo, separar la Wallet y los juegos en servicios, fase 4). Estan en `tests/load/`.

## Que miden
Cada jugador virtual (VU) repite: apostar (ruleta con 1 a 3 apuestas por tirada, o tragamonedas) y consultar hasta que la ronda cierra.

| Metrica | Que es |
| --- | --- |
| **Aceptar apuesta / giro** | Cuanto tarda la API en responder `202`. Solo es el trayecto de entrada: la apuesta todavia no se resolvio. |
| **Cobrada de punta a punta** | Desde que la API acepta hasta que la ronda queda `Settled`: API → cola → Wallet reserva → juego sortea → cola → Wallet liquida. Es la que mas cambia al separar servicios. |
| **Apuestas sin cerrar a tiempo** | Rondas que no llegaron a un estado final en 20 s. Tiene que ser 0. |
| **Checks** | Aceptacion de cada apuesta y, al final, que **el saldo cierra** (ver abajo). |

Al terminar se verifica, por cada jugador, que **no quede ninguna reserva abierta** y que **la suma de los movimientos del extracto
sea exactamente el saldo disponible**. Si el sistema perdiera o duplicara fichas bajo carga, esta prueba falla.

## Como correrla
Requisitos: `k6` instalado y el sistema levantado (`aspire start`).

```
k6 run -e PLAYERS=20 -e VUS=20 -e RAMP=10s -e DURATION=60s -e POLL_MS=50 -e LABEL=monolito -e COMMIT=$(git rev-parse --short HEAD) tests/load/casino.js
```

| Variable | Por defecto | Para que |
| --- | --- | --- |
| `PLAYERS` / `VUS` | 10 / igual | Jugadores distintos y jugadores virtuales simultaneos |
| `RAMP` / `DURATION` | 15s / 60s | Subida gradual y tiempo sostenido |
| `SLOTS_RATIO` | 0.3 | Proporcion de giros de tragamonedas (el resto, ruleta) |
| `MAX_STAKE` | 5 | Fichas maximas por apuesta |
| `TOPUP` | 20000 | Fichas que el backoffice le carga a cada jugador antes de empezar |
| `POLL_MS` | 100 | Cada cuanto se consulta si cerro la ronda (la medicion de cobro tiene esa resolucion: usar 50 para afinar) |
| `BASE_URL` / `KC_URL` | `http://localhost:5188` / `http://localhost:8080` | Donde estan la API (o el gateway) y Keycloak |
| `LABEL` / `COMMIT` | `prueba` / `desconocido` | Se imprimen y van al archivo de resultados |
| `KC_ADMIN_PASSWORD` | vacio | **Opcional.** Con la clave del administrador de Keycloak se crean los jugadores sinteticos `load-001..N` (datos inventados). Sin ella se usan `jugador1` y `jugador2` |

La clave del administrador es un secreto: se pasa por variable de entorno en el momento (por ejemplo, tomandola de los user-secrets del
AppHost) y **nunca se escribe en el repositorio**. Los resultados crudos van a `tests/load/results/` (ignorados por git).

El script usa el cliente `casino-tests` del realm (login con usuario y contrasena, **solo desarrollo**) y el usuario `backoffice1` para
cargar fichas. Todo es local: no apuntar esto a un ambiente real.

## Como leer los numeros
- Mirar **mediana (med), p95 y p99**: el promedio esconde los casos lentos. p95 = el 95 % de las apuestas tardo eso o menos.
- **Hay ruido.** Todo corre en una misma maquina (k6, Docker, Keycloak, la API, el antivirus, otras compilaciones). Dos corridas iguales
  pueden diferir mucho. Para comparar, repetir cada escenario **al menos 3 veces** con la maquina quieta y comparar las medianas de las corridas.
- Las peticiones "fallidas" (~0,2 %) son los `404` de `GET /wallet/me` mientras se espera que se abra la cuenta de un jugador nuevo
  durante la preparacion; no son errores de la carga.
- Conviene medir un escalon que **no sature** (20 jugadores) y uno que **si** (50): el segundo muestra donde se acumula la cola.

## Linea base: monolito (antes de separar servicios)
Commit `b68f4a9` (`dev`). Un solo proceso `Casino.Api` con todos los modulos, una base de datos, RabbitMQ y Redis, en el portatil de desarrollo.
60 s sostenidos, 30 % tragamonedas, `POLL_MS=50`. Tiempos en milisegundos.

| Escenario | Aceptar apuesta (med / p95 / p99) | Cobrada de punta a punta, ruleta (med / p95 / p99) | Apuestas cerradas | Saldos |
| --- | --- | --- | --- | --- |
| 20 jugadores, corrida 1 | 27 / 142 / 379 | 177 / 428 / 3.375 | 1.777 | cierran |
| 20 jugadores, corrida 2 | 56 / 173 / 305 | 362 / 791 / 973 | 1.526 | cierran |
| 50 jugadores | 84 / 353 / 936 | 1.723 / 2.503 / 6.375 | 1.537 | cierran |

Lectura: con 20 jugadores el sistema responde con holgura (cobro de ~0,2 a 0,4 s). Con 50 **se satura**: el cobro de punta a punta sube a
~1,7 s de mediana y se cierran menos apuestas que con 20, lo que indica que el cuello esta en el trayecto por las colas (la propia
aceptacion de la apuesta sigue rapida). Ningun escenario perdio ni duplico fichas ni dejo rondas sin cerrar.

Estos numeros son la referencia para la fase 4: despues de separar la Wallet y los juegos se vuelven a medir los mismos escenarios y se
comparan en `docs/adr/` (ADR de la separacion).
