# ADR 0005 - Frontend en React con OIDC y avisos en vivo

- **Estado:** aceptada
- **Fecha:** 2026-10-08

## Contexto
El jugador necesita una interfaz web que inicie sesion de forma segura, muestre su saldo en vivo y deje apostar, sin que el
navegador guarde secretos ni pueda inventar su identidad.

## Decision
- **React 19 + Vite + TypeScript estricto.** TypeScript se queda en la serie **5.9**: las herramientas de lint todavia no soportan la 7
  (piden menos de 6.1). Se revisa al actualizar `typescript-eslint`.
- **Cliente publico de OpenID Connect** (`casino-web`): *Authorization Code + PKCE*, sin secreto. La libreria `oidc-client-ts` se
  encarga del flujo y de renovar el token (dura 5 minutos). Las contraseñas solo se escriben en la pantalla de Keycloak.
- **El token se guarda en `sessionStorage`** (se pierde al cerrar la pestaña). Es el compromiso habitual de una SPA: un XSS podria
  leerlo. Se mitiga con tokens de vida corta y sin pagina que renderice HTML de terceros. Alternativa descartada por ahora: un
  backend-for-frontend con cookie `HttpOnly`.
- **Datos del servidor con TanStack Query**; el saldo y las rondas en vivo con **SignalR**. No se usa una libreria de estado global:
  el unico estado compartido (saldo) es un reductor pequeño y probado.
- **El saldo respeta la `version`.** Los avisos pueden llegar repetidos o fuera de orden entre instancias de la API; el reductor solo
  acepta datos que no sean mas viejos que lo mostrado. Al reconectar se pide el estado completo por HTTP.
- **Idempotencia desde el cliente.** Cada intento de apuesta lleva una `Idempotency-Key`; si la respuesta se pierde por la red y el
  jugador reintenta, se reutiliza la misma clave (el servidor no cobra dos veces). Con una respuesta definitiva se genera una nueva.
- **Respaldo ante avisos perdidos:** mientras espera el resultado, la mesa consulta la ronda cada 3 s.
- **CORS por configuracion** (`Cors:AllowedOrigins`): sin configurar no se habilita nada. No hay cookies, asi que no se usa `AllowCredentials`.
- **Cuenta nueva:** la cuenta de la Wallet se abre por mensajes y puede tardar un instante; el front reintenta la consulta
  (404) hasta 30 veces antes de rendirse.
- **Pruebas en tres niveles:** unitarias y de componentes con Vitest (sesion, API y conexion simuladas), la logica de verificacion con
  `node --test`, y **de extremo a extremo con un navegador real** (Playwright) contra todo el sistema levantado. Estas ultimas no
  corren en el CI: necesitan toda la infraestructura (`npm run e2e` con `aspire start` activo).

## Consecuencias
- El front y la API viven en origenes distintos en desarrollo (`:5173` y `:5188`); el puerto 5173 es fijo porque Keycloak solo acepta
  redirecciones a esa URL.
- Los textos de la interfaz estan en español rioplatense (voseo) y con tildes; el codigo y sus comentarios, sin tildes por convencion del repo.
- Un navegador real encontro un error (`parsePrefill is not defined`) que ninguna prueba unitaria veia: las pruebas de extremo a
  extremo son parte del trabajo, no un extra.

## Anexo — Ruleta animada (paso 3b)
- **La animación no decide nada.** El servidor ya sorteó el número; la rueda (`lib/wheel.ts`, un modelo matemático puro sin dibujo) solo hace
  aterrizar la bola en ese casillero. PixiJS únicamente lee ángulos y dibuja, así la lógica se prueba sin navegador ni WebGL.
- **Suspenso independiente del servidor:** la rueda empieza a girar al enviar la apuesta y no aterriza antes de un mínimo de giro,
  aunque la respuesta llegue al instante; si el resultado tarda, sigue girando.
- **No se adelanta el resultado:** `holdBalance()` en el `RealtimeProvider` congela el saldo (guarda los avisos y el estado HTTP, en orden)
  hasta que la bola se detiene; recién ahí aparecen el saldo nuevo, el aviso emergente y el panel de resultado.
- **Tapete generado y verificado:** `lib/board.ts` produce las 157 apuestas legales (37 plenos, 60 caballos, 12 calles, 2 tríos,
  22 cuadros, 1 primeros cuatro, 11 seisenas, 12 exteriores) y una prueba comprueba que ninguna sería rechazada por la API.
- **Una apuesta por tirada:** el backend crea una ronda (y un nonce) por apuesta. Varias apuestas en una misma tirada requieren
  cambiar el modelo del servidor; queda como paso posterior (3c).
- **Accesibilidad:** con "reducir movimiento" la rueda se asienta al instante; la rueda expone texto alternativo y `data-state`.
- **Carga diferida:** PixiJS pesa mucho, así que la página de la ruleta se descarga recién al entrar a ella.
