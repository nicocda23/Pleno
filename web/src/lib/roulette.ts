import type { RouletteBetType } from "../api/types";

const RED = new Set([1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36]);

export type PocketColor = "red" | "black" | "green";

/** Color de un casillero de la ruleta europea. El cero es verde. */
export function pocketColor(n: number): PocketColor {
  if (n === 0) return "green";
  return RED.has(n) ? "red" : "black";
}

export interface BetOption {
  id: string;
  label: string;
  hint: string;
  betType: RouletteBetType;
  selection: number[];
  /** Cuanto paga en total por cada ficha apostada, incluida la apuesta (36 dividido la cantidad de numeros que cubre). */
  multiplier: number;
}

/** Apuestas "externas" y pleno: las que se pueden elegir con un toque. Todas pagan en enteros (36 / cantidad cubierta). */
export const OUTSIDE_BETS: BetOption[] = [
  { id: "red", label: "Rojo", hint: "18 numeros", betType: "Red", selection: [], multiplier: 2 },
  { id: "black", label: "Negro", hint: "18 numeros", betType: "Black", selection: [], multiplier: 2 },
  { id: "even", label: "Par", hint: "18 numeros", betType: "Even", selection: [], multiplier: 2 },
  { id: "odd", label: "Impar", hint: "18 numeros", betType: "Odd", selection: [], multiplier: 2 },
  { id: "low", label: "1 - 18", hint: "Falta", betType: "Low", selection: [], multiplier: 2 },
  { id: "high", label: "19 - 36", hint: "Pasa", betType: "High", selection: [], multiplier: 2 },
  { id: "dozen1", label: "1ª docena", hint: "1 al 12", betType: "Dozen", selection: [1], multiplier: 3 },
  { id: "dozen2", label: "2ª docena", hint: "13 al 24", betType: "Dozen", selection: [2], multiplier: 3 },
  { id: "dozen3", label: "3ª docena", hint: "25 al 36", betType: "Dozen", selection: [3], multiplier: 3 },
  { id: "column1", label: "Columna 1", hint: "1, 4, 7...", betType: "Column", selection: [1], multiplier: 3 },
  { id: "column2", label: "Columna 2", hint: "2, 5, 8...", betType: "Column", selection: [2], multiplier: 3 },
  { id: "column3", label: "Columna 3", hint: "3, 6, 9...", betType: "Column", selection: [3], multiplier: 3 },
];

export function straightBet(n: number): BetOption {
  return { id: `straight-${n}`, label: `Pleno ${n}`, hint: "1 numero", betType: "Straight", selection: [n], multiplier: 36 };
}

const NAMES: Record<RouletteBetType, string> = {
  Straight: "Pleno",
  Split: "Caballo",
  Street: "Calle",
  Trio: "Trio",
  Corner: "Cuadro",
  FirstFour: "Primeros cuatro",
  SixLine: "Seisena",
  Dozen: "Docena",
  Column: "Columna",
  Red: "Rojo",
  Black: "Negro",
  Even: "Par",
  Odd: "Impar",
  Low: "1 - 18",
  High: "19 - 36",
};

/** Texto corto de una apuesta, por ejemplo "Pleno 17" o "Docena 2". */
export function describeBet(betType: RouletteBetType, selection: number[]): string {
  const name = NAMES[betType];
  const needsSelection = !["Red", "Black", "Even", "Odd", "Low", "High"].includes(betType);
  return needsSelection && selection.length > 0 ? `${name} ${selection.join(", ")}` : name;
}
