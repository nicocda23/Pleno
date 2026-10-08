import { describeBet, OUTSIDE_BETS, pocketColor, straightBet } from "./roulette";

describe("pocketColor", () => {
  it("colors the european wheel", () => {
    expect(pocketColor(0)).toBe("green");
    expect(pocketColor(1)).toBe("red");
    expect(pocketColor(2)).toBe("black");
    expect(pocketColor(17)).toBe("black");
    expect(pocketColor(36)).toBe("red");
  });

  it("has 18 red, 18 black and one green pocket", () => {
    const colors = Array.from({ length: 37 }, (_, n) => pocketColor(n));

    expect(colors.filter((c) => c === "red")).toHaveLength(18);
    expect(colors.filter((c) => c === "black")).toHaveLength(18);
    expect(colors.filter((c) => c === "green")).toHaveLength(1);
  });
});

describe("bets", () => {
  it("every multiplier is an exact divisor of 36, so payouts are whole chips", () => {
    for (const bet of [...OUTSIDE_BETS, straightBet(7)]) {
      expect(36 % bet.multiplier).toBe(0);
    }
  });

  it("offers 12 outside bets with unique ids", () => {
    expect(OUTSIDE_BETS).toHaveLength(12);
    expect(new Set(OUTSIDE_BETS.map((b) => b.id)).size).toBe(12);
  });

  it("describes bets in Spanish", () => {
    expect(describeBet("Red", [])).toBe("Rojo");
    expect(describeBet("Straight", [17])).toBe("Pleno 17");
    expect(describeBet("Dozen", [2])).toBe("Docena 2");
    expect(describeBet("Low", [])).toBe("1 - 18");
  });
});
