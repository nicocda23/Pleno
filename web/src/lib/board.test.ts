import type { RouletteBetType } from "../api/types";
import { aggregateChips, allSpots, cellOf, chipBreakdown, COLUMNS, edgeSpots, numberSpots, outsideSpots, ROWS, totalStake, type Spot } from "./board";

// Port de las reglas del servidor (src/Modules/Games/Roulette/RouletteBet.cs), solo para comprobar que el tapete
// no ofrece NUNCA una apuesta que la API rechazaria.
const row = (n: number) => Math.floor((n - 1) / 3);
const col = (n: number) => (n - 1) % 3;
const inGrid = (n: number) => n >= 1 && n <= 36;

function isLegal(betType: RouletteBetType, selection: number[]): boolean {
  const n = [...selection].sort((a, b) => a - b);
  switch (betType) {
    case "Straight":
      return n.length === 1 && n[0]! >= 0 && n[0]! <= 36;
    case "Split": {
      if (n.length !== 2) return false;
      const [a, b] = [n[0]!, n[1]!];
      if (a === 0) return b === 1 || b === 2 || b === 3;
      return b - a === 3 || (b - a === 1 && row(a) === row(b));
    }
    case "Street":
      return n.length === 3 && inGrid(n[0]!) && col(n[0]!) === 0 && n[1] === n[0]! + 1 && n[2] === n[0]! + 2;
    case "Trio":
      return JSON.stringify(n) === "[0,1,2]" || JSON.stringify(n) === "[0,2,3]";
    case "Corner":
      return n.length === 4 && inGrid(n[0]!) && col(n[0]!) < 2 && n[1] === n[0]! + 1 && n[2] === n[0]! + 3 && n[3] === n[0]! + 4 && n[3]! <= 36;
    case "FirstFour":
      return JSON.stringify(n) === "[0,1,2,3]";
    case "SixLine":
      return n.length === 6 && inGrid(n[0]!) && col(n[0]!) === 0 && n[5]! <= 36 && n.every((v, i) => v === n[0]! + i);
    case "Dozen":
    case "Column":
      return n.length === 1 && n[0]! >= 1 && n[0]! <= 3;
    default:
      return n.length === 0; // chances simples
  }
}

describe("board spots", () => {
  const spots = allSpots();
  const byKind = (kind: Spot["kind"]) => spots.filter((s) => s.kind === kind);

  it("offers exactly the legal bets of a european table: 37 + 60 + 12 + 2 + 22 + 1 + 11 inside, and 12 outside", () => {
    expect(byKind("number")).toHaveLength(37);
    expect(byKind("split")).toHaveLength(60);
    expect(byKind("street")).toHaveLength(12);
    expect(byKind("trio")).toHaveLength(2);
    expect(byKind("corner")).toHaveLength(22);
    expect(byKind("firstfour")).toHaveLength(1);
    expect(byKind("sixline")).toHaveLength(11);
    expect(byKind("dozen")).toHaveLength(3);
    expect(byKind("column")).toHaveLength(3);
    expect(byKind("outside")).toHaveLength(6);
    expect(spots).toHaveLength(37 + 108 + 12);
  });

  it("never offers a bet the API would reject", () => {
    for (const s of spots) {
      expect(isLegal(s.betType, s.selection), `${s.id} deberia ser legal`).toBe(true);
    }
  });

  it("has unique ids and no duplicated bet", () => {
    expect(new Set(spots.map((s) => s.id)).size).toBe(spots.length);
  });

  it("every payout multiplier is a whole number (36 divided by the covered numbers)", () => {
    for (const s of spots) {
      expect(Number.isInteger(s.multiplier), s.id).toBe(true);
      expect(36 % s.covered).toBe(0);
    }
    expect(numberSpots()[17]!.multiplier).toBe(36);
    expect(byKind("split")[0]!.multiplier).toBe(18);
    expect(byKind("street")[0]!.multiplier).toBe(12);
    expect(byKind("corner")[0]!.multiplier).toBe(9);
    expect(byKind("sixline")[0]!.multiplier).toBe(6);
    expect(byKind("dozen")[0]!.multiplier).toBe(3);
    expect(outsideSpots().find((s) => s.betType === "Red")!.multiplier).toBe(2);
  });

  it("the covered count matches the selection for every inside bet", () => {
    for (const s of spots.filter((x) => ["number", "split", "street", "corner", "sixline", "trio", "firstfour"].includes(x.kind))) {
      expect(s.selection).toHaveLength(s.covered);
    }
  });

  it("places every edge point inside the number grid, without two points on the same spot", () => {
    const edges = edgeSpots();
    for (const s of edges) {
      expect(s.x).toBeGreaterThanOrEqual(0);
      expect(s.x).toBeLessThanOrEqual(COLUMNS);
      expect(s.y).toBeGreaterThanOrEqual(0);
      expect(s.y).toBeLessThanOrEqual(ROWS);
    }
    const positions = edges.map((s) => `${s.x},${s.y}`);
    expect(new Set(positions).size).toBe(edges.length);
  });

  it("locates the numbers like a real layout", () => {
    expect(cellOf(1)).toEqual({ column: 0, row: 2 });
    expect(cellOf(3)).toEqual({ column: 0, row: 0 });
    expect(cellOf(2)).toEqual({ column: 0, row: 1 });
    expect(cellOf(34)).toEqual({ column: 11, row: 2 });
    expect(cellOf(36)).toEqual({ column: 11, row: 0 });
    expect(() => cellOf(0)).toThrow(RangeError);
  });

  it("puts a split exactly between the two cells it covers", () => {
    const split = byKind("split").find((s) => s.selection.join() === "17,20")!;
    const a = cellOf(17);
    const b = cellOf(20);

    expect(split.x).toBe((a.column + b.column) / 2 + 0.5);
    expect(split.y).toBe((a.row + b.row) / 2 + 0.5);
  });

  it("puts a corner at the meeting point of its four cells", () => {
    const corner = byKind("corner").find((s) => s.selection.join() === "8,9,11,12")!;
    const cells = corner.selection.map(cellOf);

    expect(corner.x).toBe(Math.max(...cells.map((c) => c.column)));
    expect(corner.y).toBe(Math.max(...cells.map((c) => c.row)));
  });

  it("every number from 1 to 36 is reachable from the grid: straight, split, street, corner and six line", () => {
    for (let n = 1; n <= 36; n++) {
      const covering = spots.filter((s) => s.selection.includes(n) && ["number", "split", "street", "corner", "sixline"].includes(s.kind));
      expect(covering.length, `numero ${n}`).toBeGreaterThanOrEqual(1 + 1 + 1);
    }
  });
});

describe("chipBreakdown", () => {
  it("uses the biggest chips first", () => {
    expect(chipBreakdown(0)).toEqual([]);
    expect(chipBreakdown(10)).toEqual([{ value: 10, count: 1 }]);
    expect(chipBreakdown(160)).toEqual([{ value: 100, count: 1 }, { value: 50, count: 1 }, { value: 10, count: 1 }]);
    expect(chipBreakdown(1_230)).toEqual([{ value: 500, count: 2 }, { value: 100, count: 2 }, { value: 10, count: 3 }]);
  });

  it("always adds back up to the stake", () => {
    for (const stake of [1, 7, 10, 99, 100, 555, 1_001, 12_345]) {
      expect(chipBreakdown(stake).reduce((sum, c) => sum + c.value * c.count, 0)).toBe(stake);
    }
  });

  it("ignores negatives and decimals", () => {
    expect(chipBreakdown(-5)).toEqual([]);
    expect(chipBreakdown(10.9)).toEqual([{ value: 10, count: 1 }]);
  });
});

describe("aggregateChips", () => {
  const spots = allSpots();
  const red = spots.find((s) => s.id === "Red:")!;
  const black = spots.find((s) => s.id === "Black:")!;
  const seven = spots.find((s) => s.id === "Straight:7")!;

  it("lets the player bet on many places at once, even opposite ones like red and black", () => {
    const placed = aggregateChips([
      { spot: red, amount: 100 },
      { spot: black, amount: 50 },
      { spot: seven, amount: 10 },
    ]);

    expect(placed.map((p) => p.spot.id)).toEqual(["Red:", "Black:", "Straight:7"]);
    expect(totalStake(placed)).toBe(160);
  });

  it("adds up the chips dropped on the same place, keeping the order of first touch", () => {
    const placed = aggregateChips([
      { spot: seven, amount: 10 },
      { spot: red, amount: 5 },
      { spot: seven, amount: 50 },
    ]);

    expect(placed).toHaveLength(2);
    expect(placed[0]).toMatchObject({ stake: 60 });
    expect(placed[0]!.spot.id).toBe("Straight:7");
  });

  it("is empty with nothing placed", () => {
    expect(aggregateChips([])).toEqual([]);
    expect(totalStake([])).toBe(0);
  });
});
