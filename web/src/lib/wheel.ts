// Modelo de la rueda: matematica pura, sin dibujo. Asi es determinista y se prueba sin navegador.
// PixiJS solo lee `wheelAngle`, `ballAngle` y `ballRadius` en cada cuadro.
//
// Convencion de angulos: radianes, el cero apunta a las 12 en punto y crecen en sentido horario (como en pantalla).

/** Orden de los casilleros en una rueda europea, en sentido horario empezando por el cero. */
export const WHEEL_ORDER = [
  0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
] as const;

export const POCKETS = WHEEL_ORDER.length;
export const POCKET_STEP = (Math.PI * 2) / POCKETS;
const TAU = Math.PI * 2;

/** Angulo (en el marco de la rueda) del centro del casillero que lleva el numero `n`. */
export function pocketCenterAngle(n: number): number {
  const index = WHEEL_ORDER.indexOf(n as (typeof WHEEL_ORDER)[number]);
  if (index < 0) throw new RangeError(`No existe el casillero ${n}`);
  return (index + 0.5) * POCKET_STEP;
}

/** Numero del casillero que esta bajo un angulo del marco de la rueda. */
export function pocketAt(angle: number): number {
  const wrapped = ((angle % TAU) + TAU) % TAU;
  return WHEEL_ORDER[Math.floor(wrapped / POCKET_STEP) % POCKETS]!;
}

export const easeOutCubic = (p: number) => 1 - Math.pow(1 - p, 3);
export const easeOutQuad = (p: number) => 1 - (1 - p) * (1 - p);

export type WheelPhase = "idle" | "spinning" | "landing" | "settled";

export interface WheelTimings {
  /** Velocidad de la rueda girando (rad/s). */
  wheelSpeed: number;
  /** Velocidad de la bola en su pista, en sentido contrario (rad/s). */
  ballSpeed: number;
  /** Tiempo minimo girando antes de aterrizar, para que el suspenso no dependa de la velocidad del servidor. */
  minSpinMs: number;
  /** Duracion del aterrizaje de la bola. */
  landingMs: number;
  /** Vueltas extra minimas que da la bola mientras aterriza. */
  extraTurns: number;
}

export const DEFAULT_TIMINGS: WheelTimings = {
  wheelSpeed: 0.9,
  ballSpeed: 5.2,
  minSpinMs: 1_800,
  landingMs: 3_200,
  extraTurns: 2.5,
};

/** Radio de la bola, relativo al radio de la rueda (1 = borde exterior). */
export const BALL_TRACK_RADIUS = 0.92;
export const BALL_POCKET_RADIUS = 0.62;

export class WheelModel {
  phase: WheelPhase = "idle";
  /** Angulo de la rueda en el marco del mundo. */
  wheelAngle = 0;
  /** Angulo de la bola en el marco del mundo. */
  ballAngle = 0;
  ballRadius = BALL_TRACK_RADIUS;
  /** Numero en el que cayo la bola (solo en `settled`). */
  result: number | null = null;

  private readonly timings: WheelTimings;
  private readonly reduceMotion: boolean;
  private spinElapsed = 0;
  private pendingTarget: number | null = null;
  private landing: {
    elapsed: number;
    wheelStart: number;
    wheelDelta: number;
    ballStart: number;
    ballDelta: number;
    target: number;
  } | null = null;
  private idleSpeed = 0.18;

  constructor(options: { timings?: Partial<WheelTimings>; reduceMotion?: boolean } = {}) {
    this.timings = { ...DEFAULT_TIMINGS, ...options.timings };
    this.reduceMotion = options.reduceMotion ?? false;
  }

  /** Empieza a girar (la bola recorre su pista). Se llama al enviar la apuesta, sin conocer todavia el resultado. */
  spin(): void {
    this.phase = "spinning";
    this.result = null;
    this.spinElapsed = 0;
    this.pendingTarget = null;
    this.landing = null;
    this.ballRadius = BALL_TRACK_RADIUS;
  }

  /** Informa el numero ganador. Si todavia no paso el tiempo minimo de giro, aterriza apenas se cumpla. */
  land(target: number): void {
    pocketCenterAngle(target); // valida el numero
    if (this.phase !== "spinning") this.spin();

    if (this.reduceMotion) {
      this.finish(target);
      return;
    }
    this.pendingTarget = target;
  }

  /** La apuesta no llego a jugarse (rechazada o anulada): la rueda vuelve a su reposo sin bola en ningun casillero. */
  cancel(): void {
    this.phase = "idle";
    this.result = null;
    this.pendingTarget = null;
    this.landing = null;
    this.ballRadius = BALL_TRACK_RADIUS;
  }

  update(deltaMs: number): void {
    const dt = deltaMs / 1000;
    switch (this.phase) {
      case "idle":
        this.wheelAngle += this.idleSpeed * dt;
        this.ballAngle = this.wheelAngle + pocketCenterAngle(0);
        this.ballRadius = BALL_TRACK_RADIUS;
        break;

      case "spinning":
        this.wheelAngle += this.timings.wheelSpeed * dt;
        this.ballAngle -= this.timings.ballSpeed * dt;
        this.spinElapsed += deltaMs;
        if (this.pendingTarget !== null && this.spinElapsed >= this.timings.minSpinMs) this.beginLanding(this.pendingTarget);
        break;

      case "landing":
        this.advanceLanding(deltaMs);
        break;

      case "settled":
        // Quieta: la bola viaja con la rueda, que ya se detuvo.
        break;
    }
  }

  private beginLanding(target: number): void {
    const { wheelSpeed, extraTurns, landingMs } = this.timings;
    const seconds = landingMs / 1000;
    // La rueda frena de forma lineal: recorre la mitad de lo que haria a velocidad constante.
    const wheelDelta = wheelSpeed * seconds * 0.5;
    const wheelEnd = this.wheelAngle + wheelDelta;
    // La bola tiene que terminar sobre el casillero ganador, que para entonces estara en `wheelEnd + centro`.
    const finalBall = wheelEnd + pocketCenterAngle(target);
    // Sentido contrario al de la rueda: se resta hasta tener al menos `extraTurns` vueltas por delante.
    let ballDelta = finalBall - this.ballAngle;
    while (ballDelta > -extraTurns * TAU) ballDelta -= TAU;
    while (ballDelta < -(extraTurns + 1) * TAU) ballDelta += TAU;

    this.landing = { elapsed: 0, wheelStart: this.wheelAngle, wheelDelta, ballStart: this.ballAngle, ballDelta, target };
    this.phase = "landing";
  }

  private advanceLanding(deltaMs: number): void {
    const landing = this.landing!;
    landing.elapsed += deltaMs;
    const progress = Math.min(1, landing.elapsed / this.timings.landingMs);

    this.wheelAngle = landing.wheelStart + landing.wheelDelta * easeOutQuad(progress);
    this.ballAngle = landing.ballStart + landing.ballDelta * easeOutCubic(progress);
    // La bola cae de la pista al casillero en el ultimo tramo, con un pequeño rebote.
    const drop = Math.min(1, Math.max(0, (progress - 0.6) / 0.4));
    const bounce = Math.sin(drop * Math.PI * 3) * 0.02 * (1 - drop);
    this.ballRadius = BALL_TRACK_RADIUS + (BALL_POCKET_RADIUS - BALL_TRACK_RADIUS) * easeOutCubic(drop) + bounce;

    if (progress >= 1) this.finish(landing.target);
  }

  private finish(target: number): void {
    // Estado final exacto, sin error de redondeo acumulado.
    const endWheel = this.landing ? this.landing.wheelStart + this.landing.wheelDelta : this.wheelAngle;
    this.wheelAngle = endWheel;
    // Sin salto: se conserva la cuenta de vueltas de la bola (un angulo equivalente con otras vueltas seria invisible pero incoherente).
    this.ballAngle = this.landing ? this.landing.ballStart + this.landing.ballDelta : endWheel + pocketCenterAngle(target);
    this.ballRadius = BALL_POCKET_RADIUS;
    this.result = target;
    this.phase = "settled";
    this.pendingTarget = null;
    this.landing = null;
  }
}
