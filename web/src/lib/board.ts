import type { RouletteBetType } from "../api/types";
import { describeBet } from "./roulette";

// Geometria del tapete. El tapete de numeros tiene 12 columnas x 3 filas; el cero queda a la izquierda.
//   fila 0:  3  6  9 ... 36
//   fila 1:  2  5  8 ... 35
//   fila 2:  1  4  7 ... 34
// Los puntos de apuesta de los bordes (caballos, calles, cuadros...) se ubican en unidades de celda: x en [0, 12], y en [0, 3];
// x = 0 es el borde entre el cero y la primera columna.

export const COLUMNS = 12;
export const ROWS = 3;

export type SpotKind = "number" | "split" | "street" | "corner" | "sixline" | "trio" | "firstfour" | "dozen" | "column" | "outside";

export interface Spot {
  id: string;
  kind: SpotKind;
  betType: RouletteBetType;
  selection: number[];
  /** Cuantos numeros cubre: el pago total por ficha es 36 / covered (siempre un entero). */
  covered: number;
  multiplier: number;
  label: string;
  /** Posicion del punto en unidades de celda (solo los de los bordes). */
  x?: number;
  y?: number;
}

const COVERED: Record<RouletteBetType, number> = {
  Straight: 1,
  Split: 2,
  Street: 3,
  Trio: 3,
  Corner: 4,
  FirstFour: 4,
  SixLine: 6,
  Dozen: 12,
  Column: 12,
  Red: 18,
  Black: 18,
  Even: 18,
  Odd: 18,
  Low: 18,
  High: 18,
};

function spot(kind: SpotKind, betType: RouletteBetType, selection: number[], position?: { x: number; y: number }): Spot {
  const covered = COVERED[betType];
  const sorted = [...selection].sort((a, b) => a - b);
  return {
    id: `${betType}:${sorted.join("-")}`,
    kind,
    betType,
    selection: sorted,
    covered,
    multiplier: 36 / covered,
    label: describeBet(betType, sorted),
    ...position,
  };
}

/** Columna y fila (0 = arriba) de un numero del 1 al 36 en el tapete. */
export function cellOf(n: number): { column: number; row: number } {
  if (n < 1 || n > 36) throw new RangeError(`El ${n} no esta en el tapete de numeros`);
  return { column: Math.floor((n - 1) / 3), row: ROWS - 1 - ((n - 1) % 3) };
}

/** Los 37 casilleros (pleno). */
export function numberSpots(): Spot[] {
  return Array.from({ length: 37 }, (_, n) => spot("number", "Straight", [n]));
}

/** Puntos de los bordes: caballos, calles, cuadros, seisenas, tríos y primeros cuatro. */
export function edgeSpots(): Spot[] {
  const spots: Spot[] = [];
  const top = (column: number, row: number) => 3 * column + ROWS - row; // numero de la celda (columna, fila)

  for (let column = 0; column < COLUMNS; column++) {
    // Caballo vertical: entre dos filas de la misma columna.
    for (let row = 0; row < ROWS - 1; row++) {
      spots.push(spot("split", "Split", [top(column, row) - 1, top(column, row)], { x: column + 0.5, y: row + 1 }));
    }
    // Calle: el borde inferior de la columna.
    spots.push(spot("street", "Street", [3 * column + 1, 3 * column + 2, 3 * column + 3], { x: column + 0.5, y: ROWS }));
  }

  for (let column = 0; column < COLUMNS - 1; column++) {
    for (let row = 0; row < ROWS; row++) {
      // Caballo horizontal: entre dos columnas, en la misma fila.
      spots.push(spot("split", "Split", [top(column, row), top(column + 1, row)], { x: column + 1, y: row + 0.5 }));
    }
    for (let row = 0; row < ROWS - 1; row++) {
      // Cuadro: en la interseccion de cuatro celdas.
      const tl = top(column, row);
      spots.push(spot("corner", "Corner", [tl - 1, tl, tl + 2, tl + 3], { x: column + 1, y: row + 1 }));
    }
    // Seisena: el borde inferior entre dos columnas.
    spots.push(spot("sixline", "SixLine", [3 * column + 1, 3 * column + 2, 3 * column + 3, 3 * column + 4, 3 * column + 5, 3 * column + 6], { x: column + 1, y: ROWS }));
  }

  // Alrededor del cero (borde izquierdo del tapete).
  spots.push(spot("split", "Split", [0, 3], { x: 0, y: 0.5 }));
  spots.push(spot("split", "Split", [0, 2], { x: 0, y: 1.5 }));
  spots.push(spot("split", "Split", [0, 1], { x: 0, y: 2.5 }));
  spots.push(spot("trio", "Trio", [0, 2, 3], { x: 0, y: 1 }));
  spots.push(spot("trio", "Trio", [0, 1, 2], { x: 0, y: 2 }));
  spots.push(spot("firstfour", "FirstFour", [0, 1, 2, 3], { x: 0, y: 3 }));

  return spots;
}

/** Apuestas exteriores: docenas, columnas y chances simples. */
export function outsideSpots(): Spot[] {
  return [
    spot("dozen", "Dozen", [1]),
    spot("dozen", "Dozen", [2]),
    spot("dozen", "Dozen", [3]),
    spot("column", "Column", [1]),
    spot("column", "Column", [2]),
    spot("column", "Column", [3]),
    spot("outside", "Low", []),
    spot("outside", "Even", []),
    spot("outside", "Red", []),
    spot("outside", "Black", []),
    spot("outside", "Odd", []),
    spot("outside", "High", []),
  ];
}

export const allSpots = (): Spot[] => [...numberSpots(), ...edgeSpots(), ...outsideSpots()];

/** Estado de una apuesta: cuanto se aposto y donde. Una sola apuesta por tirada (por ahora). */
export interface PlacedChips {
  spot: Spot;
  stake: number;
}

const DENOMINATIONS = [500, 100, 50, 10, 1] as const;

/** Descompone un monto en las fichas que se apilan sobre el casillero (las de mayor valor primero). */
export function chipBreakdown(stake: number): { value: number; count: number }[] {
  let rest = Math.max(0, Math.trunc(stake));
  const result: { value: number; count: number }[] = [];
  for (const value of DENOMINATIONS) {
    const count = Math.floor(rest / value);
    if (count > 0) {
      result.push({ value, count });
      rest -= count * value;
    }
  }
  return result;
}
