# Mejoras y observaciones

Lista viva de cosas que se ven probando la app. Se anota acá, se prioriza después.
Formato: `- [ ]` pendiente · `- [x]` hecho. Agregar la fecha o el PR al cerrar un ítem.

## Juegos

### Uno
- [ ] **Avisar cuando hay que robar.** Si no tenés nada para jugar y tenés que robar del mazo, falta una alerta o un resaltado de color (por ejemplo, el mazo pulsando o con un borde dorado y un cartel "Tocá el mazo para robar") que indique que tenés que tocar el mazo. Hoy solo cambia el texto del estado, que es fácil de no ver. Se detecta con `myTurn && game.playable.length === 0 && game.drawnCard === null`.
- [ ] **Relleno en los mazos.** Que el mazo de robo (y el de descarte) parezcan tener más cartas abajo: capas apiladas con un pequeño desfase, tipo `box-shadow` o pseudo-elementos. No hace falta que sea real; opcionalmente, que se vea más finito cuando quedan pocas cartas.
- [ ] **Animación al robar.** Cuando robás cartas del mazo (+2, +4 o robo normal), que se vea el efecto de las cartas saliendo del mazo hacia la mano. Lo mismo para los rivales: cartas que vuelan del mazo hacia su lugar. Escalonar cuando son varias, y respetar `prefers-reduced-motion`.

### Slots
- [ ] _(anotar)_

### Blackjack
- [ ] **Más continuidad entre manos.** Pasa demasiado tiempo entre una mano y la siguiente. Acortar las pausas (reparto, resolución, limpieza de mesa), solapar animaciones y arrancar la siguiente ronda más rápido, por ejemplo con un conteo corto o apuesta automática repetida.

### Poker
- [ ] _(anotar)_

### Truco
- [ ] _(anotar)_

### Crash
- [ ] **CRÍTICO: a veces no aparece la opción de retirar (detener) tras iniciar la apuesta.** El jugador pierde las fichas sin poder cobrar. Causa desconocida. Mínimo: no debe iniciarse/debitarse la apuesta si el cliente no puede retirar (por ejemplo, confirmar que el tiempo real está conectado y el botón de retiro habilitado antes de aceptar la apuesta). Investigar también por qué falta el botón (¿estado de ronda, reconexión, mensaje perdido?) y evaluar retiro automático o reembolso si la ronda arrancó sin poder retirar.

### Ruleta
- [ ] _(anotar)_

## Entorno
Local (Aspire, Docker, Keycloak), red local, configuración, despliegue.
- [ ] _(anotar)_

## Billetera
Saldo, asientos, historial, idempotencia.
- [ ] _(anotar)_

## Usuarios
Registro, login, perfil, roles, admin.
- [ ] _(anotar)_

## Front / UX general
Navegación, responsive, accesibilidad, mensajes, sonidos.
- [ ] **Extraer el módulo de "cuántas fichas" del tragamonedas a un componente compartido.** Separarlo de `Slots.tsx` en un componente reutilizable y usarlo en todos los juegos donde aplique (ruleta, blackjack, crash, etc.), para unificar la UX y evitar duplicación.

## Tiempo real
Mesas, reconexión, latencia.
- [ ] _(anotar)_

## Rendimiento y observabilidad
Pruebas de carga, trazas, métricas, logs.
- [ ] _(anotar)_

## Otros
- [ ] _(anotar)_
