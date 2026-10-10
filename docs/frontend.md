# Frontend (`web/`)

React 19 + Vite + TypeScript. Decisiones y motivos: [ADR 0005](adr/0005-frontend-react-con-oidc.md).

## Levantarlo
Con todo el sistema (recomendado): `aspire start` levanta Postgres, RabbitMQ, Redis, Keycloak, la API **y el front** en
**http://localhost:5173**. Entrá con `jugador1` / `jugador1-dev` o `jugador2` / `jugador2-dev` (ver [autenticacion.md](autenticacion.md)),
o creá una cuenta nueva desde la pantalla de Keycloak.

Solo el front (necesita la API y Keycloak ya corriendo):

```
cd web
npm install
npm run dev
```

## Comandos
| Comando | Qué hace |
| --- | --- |
| `npm run dev` | Servidor de desarrollo en :5173 (el puerto es fijo: Keycloak solo acepta esa redirección) |
| `npm run build` | Chequeo de tipos y build de producción |
| `npm run lint` | ESLint (incluye las reglas de React hooks) |
| `npm test` | Pruebas unitarias y de componentes (Vitest) |
| `npm run e2e` | Pruebas con un navegador real (Playwright). Requieren `aspire start` activo; no corren en el CI |

La primera vez: `npx playwright install chromium`.

## Configuración
Valores públicos (no hay secretos en el navegador), con defaults para desarrollo:

| Variable | Default |
| --- | --- |
| `VITE_API_URL` | `http://localhost:5188` |
| `VITE_OIDC_AUTHORITY` | `http://localhost:8080/realms/casino` |
| `VITE_OIDC_CLIENT_ID` | `casino-web` |

La API necesita `Cors:AllowedOrigins` con el origen del front (el AppHost ya lo configura).

## Estructura
```
web/src/
  auth/        sesión OIDC (AuthProvider), regreso de Keycloak, puerta de páginas privadas
  api/         cliente HTTP con token, tipos del contrato, hooks de datos
  realtime/    conexión SignalR y saldo en vivo (reductor con versión)
  pages/       Lobby, Ruleta, Historial
  components/  cabecera, saldo animado, avisos, casillero
  lib/         formato, mensajes en español, lógica de la ruleta, enlace de verificación
  theme/       tema claro y oscuro
  test/        utilidades de prueba (sesión, API y conexión simuladas)
web/e2e/       recorridos con navegador real
```

## Qué hay hoy
- Login/logout con Keycloak, saldo en vivo con indicador de conexión, tema claro y oscuro.
- Lobby con catálogo (Ruleta disponible; el resto "Próximamente") y últimas jugadas.
- Mesa de ruleta: tapete completo (los 37 números, caballos, calles, cuadros, seisenas, docenas, columnas y chances simples) y rueda animada con PixiJS. Se puede apostar a varios lugares en la misma tirada (con "Deshacer" y "Quitar todo").
- Tragamonedas de 3 rodillos con tabla de pagos y retorno publicados (`/tragamonedas`); los rodillos frenan en lo que ya decidió el servidor.
- Panel de administración: carga de fichas (`/admin`), historial de cargas (`/admin/cargas`) y ajustes de la tragamonedas (`/admin/tragamonedas`, con prueba en vivo del retorno antes de publicar).
- Blackjack (`/blackjack`): mesas compartidas contra el crupier (cartas con CSS, turno con cuenta regresiva, Pedir/Plantarme) y verificación en el navegador del zapato con la semilla revelada.
- Uno (`/uno`) y mesas entre jugadores: el componente genérico `TablesLobby` (lista de mesas, crear mesa pública o privada, unirse con un click o con el código de 6 caracteres (`POST /tables/join`), sala de espera con bots y "Empezar") es el que van a reutilizar Truco y Poker; Uno aporta el tablero (mano, pila, color en juego, turno con cuenta regresiva, historial) y la verificación del compromiso de la partida. El aviso `GameBroadcast` / `tableChanged` refresca la lista y la mesa de cualquier juego de mesas.
- Poker (`/poker`): Texas Hold'em de una mano por mesa (2 a 6 jugadores o bots) sobre `TablesLobby`: tus 2 cartas, comunitarias, pozo, pila y apuesta de cada jugador (D/SB/BB), cuenta regresiva del turno, Retirarme/Pasar/Igualar y Subir (slider + campo + ½ pozo/pozo/All-in dentro de `minRaiseTo`..`maxRaiseTo`), showdown con las manos reveladas y verificación del compromiso; la lógica de cartas y textos vive en `lib/poker.ts`.
- Movimientos (`/movimientos`): extracto de la cuenta con el saldo después de cada cambio, derivado del ledger. El administrador tiene el historial completo de cargas (`/admin/cargas`) con filtros por jugador y fechas y el total del filtro.
- Historial con verificación: revelar la semilla y abrir la página pública `/verify` ya completa.
