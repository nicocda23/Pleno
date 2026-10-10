import type { TrucoAction, TrucoActionType, TrucoEvent, TrucoPending } from "../api/types";

// Las cartas del Truco en el navegador. Las reglas las decide el servidor: aca solo se DECODIFICA cada carta (0 a 39, baraja española) para dibujarla,
// se redactan los hechos de la partida en español y se rotulan los botones de cada jugada. Mismo reparto de ids que TrucoCards del servidor.

export const DECK_SIZE = 40;
const NUMBERS = [1, 2, 3, 4, 5, 6, 7, 10, 11, 12] as const;

export type TrucoSuit = 0 | 1 | 2 | 3;
export const SUIT_NAMES = ["espadas", "bastos", "oros", "copas"] as const;
/** Simbolos simples de cada palo (texto, sin imagenes). */
export const SUIT_SYMBOLS = ["♠", "♣", "●", "♥"] as const;
/** Color de cada palo sobre la carta clara. */
export const SUIT_CSS = ["#1f4fa8", "#1f7a46", "#b8860b", "#c02b2b"] as const;

export interface TrucoCardInfo {
  suit: TrucoSuit;
  number: number;
  symbol: string;
  /** Para lectores de pantalla y botones: "1 de espadas". */
  label: string;
}

export const isValidCard = (card: number): boolean => Number.isInteger(card) && card >= 0 && card < DECK_SIZE;

export function decodeCard(card: number): TrucoCardInfo {
  if (!isValidCard(card)) throw new RangeError(`Carta de Truco fuera de rango: ${card}`);
  const suit = Math.floor(card / 10) as TrucoSuit;
  const number = NUMBERS[card % 10]!;
  return { suit, number, symbol: SUIT_SYMBOLS[suit], label: `${number} de ${SUIT_NAMES[suit]}` };
}

/** Cuanto vale la carta para ganar una baza (mas alto gana): 14 el 1 de espadas ... 1 los 4. Sirve para ordenar la mano. */
export function strengthOf(card: number): number {
  const { suit, number } = decodeCard(card);
  switch (number) {
    case 1: return suit === 0 ? 14 : suit === 1 ? 13 : 8;
    case 7: return suit === 0 ? 12 : suit === 2 ? 11 : 4;
    case 3: return 10;
    case 2: return 9;
    case 12: return 7;
    case 11: return 6;
    case 10: return 5;
    case 6: return 3;
    case 5: return 2;
    default: return 1;
  }
}

/** Mejor carta primero. */
export const byStrength = (a: number, b: number): number => strengthOf(b) - strengthOf(a) || a - b;

const CALL_NAMES: Partial<Record<string, string>> = {
  truco: "truco",
  retruco: "retruco",
  vale4: "vale cuatro",
  envido: "envido",
  real_envido: "real envido",
  falta_envido: "falta envido",
};

const signed = (n: number): string => `+${n}`;

/**
 * El hecho de la partida en una frase: "Jugador 2 cantó truco", "Bot 2 no quiso", "Ganó la baza Jugador 1", "Parda", "Ronda para Jugador 2: +2".
 * `nameOf` da el nombre del asiento (viene del servidor: "Jugador 2" / "Bot 2").
 */
export function eventText(event: TrucoEvent, nameOf: (seat: number) => string): string {
  const who = nameOf(event.seat);
  switch (event.kind) {
    case "start":
      return "Empezó la ronda";
    case "play":
      return event.card === null ? `${who} jugó una carta` : `${who} jugó ${decodeCard(event.card).label}`;
    case "truco":
    case "retruco":
    case "vale4":
    case "envido":
    case "real_envido":
    case "falta_envido":
      return `${who} cantó ${CALL_NAMES[event.kind]}`;
    case "quiero":
      return `${who} quiso`;
    case "no_quiero":
      return `${who} no quiso`;
    case "mazo":
      return `${who} se fue al mazo`;
    case "baza":
      return `Ganó la baza ${who}`;
    case "parda":
      return "Parda";
    case "envido_result": {
      const points = event.value === null ? "" : ` (${signed(event.value)})`;
      if (event.a === null || event.b === null) return `Envido: ganó ${who}${points}`;
      return `Envido: ${nameOf(0)} tenía ${event.a} y ${nameOf(1)} ${event.b} — ganó ${who}${points}`;
    }
    case "hand_end":
      return `Ronda para ${who}: ${signed(event.value ?? 0)}`;
  }
}

/** Lo que valdria la ronda en cada nivel de truco vigente. */
export const trucoWorth = (level: number): number => (level <= 0 ? 1 : level + 1);

export const trucoLevelName = (level: number): string => (level === 1 ? "Truco" : level === 2 ? "Retruco" : level === 3 ? "Vale cuatro" : "Sin truco");

const PLAIN: Record<Exclude<TrucoActionType, "play">, string> = {
  envido: "Envido",
  real_envido: "Real envido",
  falta_envido: "Falta envido",
  truco: "Truco",
  retruco: "Retruco",
  vale4: "Vale cuatro",
  quiero: "Quiero",
  no_quiero: "No quiero",
  mazo: "Al mazo",
};

/** El rotulo del boton de una jugada. Con un truco pendiente, subir es "Quiero retruco" / "Quiero vale cuatro"; con un envido pendiente, subir es "Subir: real envido". */
export function actionLabel(type: Exclude<TrucoActionType, "play">, pending: TrucoPending | null): string {
  if (pending?.kind === "truco" && (type === "retruco" || type === "vale4")) return `Quiero ${CALL_NAMES[type]}`;
  if (pending?.kind === "envido" && (type === "envido" || type === "real_envido" || type === "falta_envido")) return `Subir: ${CALL_NAMES[type]}`;
  return PLAIN[type];
}

export type ActionGroup = "envido" | "truco" | "response" | "mazo";

export const GROUP_LABELS: Record<ActionGroup, string> = { envido: "Envido", truco: "Truco", response: "Respuesta", mazo: "Mazo" };

export function groupOf(type: Exclude<TrucoActionType, "play">): ActionGroup {
  switch (type) {
    case "envido":
    case "real_envido":
    case "falta_envido":
      return "envido";
    case "truco":
    case "retruco":
    case "vale4":
      return "truco";
    case "quiero":
    case "no_quiero":
      return "response";
    case "mazo":
      return "mazo";
  }
}

/** El cuerpo de la jugada de una carta. */
export const playBody = (card: number): TrucoAction => ({ type: "play", card });

/** El cartel del canto pendiente: "Jugador 2 cantó truco: ¿quiero?". Con envido, el ultimo canto de la cadena. */
export function pendingText(pending: TrucoPending, nameOf: (seat: number) => string): string {
  const who = nameOf(pending.caller);
  if (pending.kind === "truco") return `${who} cantó ${trucoLevelName(pending.level).toLowerCase()}: ¿quiero?`;
  const chain = pending.calls.map((c) => CALL_NAMES[c] ?? c).join(", ");
  return `${who} cantó ${chain || "envido"}: ¿quiero?`;
}
