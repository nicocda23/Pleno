# Autenticacion: como usarla en desarrollo

Decision y motivos: [ADR 0003](adr/0003-identidad-con-keycloak.md).

## Levantar todo
`aspire run` (o `aspire start`) levanta Postgres, Redis, RabbitMQ, **Keycloak** y la API. Keycloak queda en
**http://localhost:8080** (consola de administracion: usuario `admin`; su clave es un secreto de usuario, ver abajo).

## Secretos que necesita el entorno local
Ninguno se versiona. Se guardan con user-secrets:

| Secreto | Proyecto | Para que |
| --- | --- | --- |
| `Parameters:keycloak-admin-password` | `src/Casino.AppHost` | Clave del administrador de Keycloak |
| `Fairness:MasterKey` | `src/Casino.GamesService` | Clave maestra que cifra las server seeds (32 bytes en base64) |

```
dotnet user-secrets set "Parameters:keycloak-admin-password" "<valor>" --project src/Casino.AppHost
dotnet user-secrets set "Fairness:MasterKey" "<32 bytes aleatorios en base64>" --project src/Casino.GamesService
```

## Usuarios de desarrollo del realm `casino`
**Solo para desarrollo local.** Las contraseñas son triviales a proposito y el repositorio es publico: no las reutilices en ningun lado.

| Usuario | Contraseña | Rol |
| --- | --- | --- |
| `jugador1` | `jugador1-dev` | player |
| `jugador2` | `jugador2-dev` | player |
| `backoffice1` | `backoffice1-dev` | backoffice |

Cualquiera puede ademas registrarse desde la pantalla de login (registro habilitado) y recibe el rol `player`.

## Como lo usa el front (React)
- Flujo **Authorization Code con PKCE**, cliente publico `casino-web` (redirecciones permitidas: `http://localhost:5173/*`).
- Autoridad (issuer): `http://localhost:8080/realms/casino`.
- Cada llamada a la API lleva `Authorization: Bearer <access_token>`. Tras el login, llamar a `GET /me`: da de alta al jugador y
  devuelve su `userId`, su nombre y sus roles.

## Probar la API a mano
Con el cliente de pruebas `casino-tests` (solo desarrollo) se pide un token con usuario y contraseña:

```
curl -s -d "grant_type=password" -d "client_id=casino-tests" -d "username=jugador1" -d "password=jugador1-dev" \
  http://localhost:8080/realms/casino/protocol/openid-connect/token
```

Luego `curl -H "Authorization: Bearer <access_token>" http://localhost:5188/me`.

## Endpoints
| Endpoint | Rol | Descripcion |
| --- | --- | --- |
| `GET /me` | player | Datos del usuario autenticado (lo da de alta si es nuevo) |
| `GET /wallet/me`, `GET /wallet/me/balance?asOf=` | player | Mi cuenta y mi saldo a una fecha |
| `GET /fairness/me`, `POST /fairness/me/rotate` | player | Mis seeds (compromiso) y rotacion |
| `POST /games/roulette/bets` (`Idempotency-Key`) | player | Apostar; el servidor asigna el nonce |
| `GET /games/roulette/rounds`, `GET /games/roulette/rounds/{id}` | player | Mi historial y una ronda propia |
| `GET /backoffice/users` | backoffice | Jugadores registrados (id y fecha de alta) para el panel `/admin` |
| `GET /backoffice/wallet/users/{id}`, `.../balance`, `POST .../credit` | backoffice | Consulta y ajuste manual (cada carga queda en la auditoria) |
| `GET /wallet/me/movements?limit=&before=` | player | Mi extracto: cada cambio de fichas disponibles con el saldo resultante (paginado por cursor) |
| `GET /backoffice/wallet/credits?limit=&before=&userId=&from=&to=` | backoffice | Historial general de cargas con filtros, paginado y total de fichas del filtro |
| `GET /backoffice/games/slots/settings`, `POST .../settings/preview`, `POST .../settings`, `GET .../settings/history` | backoffice | Ajustes de la tragamonedas: ver, probar sin guardar, publicar una version nueva y su historial |
| `GET /backoffice/wallet/audit?limit=` | backoffice | Registro de auditoria: quien cargo fichas, a quien, cuanto y cuando |

## Panel de administracion (`/admin`)
El rol `backoffice` es el de administrador: ve el enlace **Admin** en el menu y puede cargar fichas a cualquier jugador (cada carga lleva
`Idempotency-Key`). Como Keycloak solo importa el realm la primera vez, para dar el rol a un usuario que ya existe se asigna en la consola
de Keycloak (Users > usuario > Role mapping > `backoffice`) o con la API de administracion. El usuario conserva `player`, asi que puede seguir jugando.

### Auditoria de las cargas
Cada carga manual guarda una anotacion (`AuditEntry`, esquema de la Wallet): administrador, jugador, monto, `Idempotency-Key`, id de la
transaccion y fecha. Solo ids, nunca nombre ni email. Es de solo-agregar y esta protegida por la misma idempotencia que la carga: repetir
la misma carga no duplica la anotacion, y si fallo entre la carga y su anotacion, reintentar con la misma clave completa la que faltaba.
Las cargas rechazadas (monto invalido, cuenta inexistente) no dejan anotacion. La doble aprobacion llega en la fase 5.
