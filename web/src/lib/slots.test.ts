import { winTier, winningReels } from "./slots";

describe("slots prizes", () => {
  it("sizes the celebration by how many times the stake came back", () => {
    expect(winTier(3)).toBe("win");
    expect(winTier(10)).toBe("big");
    expect(winTier(50)).toBe("mega");
    expect(winTier(null)).toBe("win");
  });

  it("lights up the reels that make the prize", () => {
    expect(winningReels(["Siete", "Siete", "Siete"])).toEqual([true, true, true]);
    expect(winningReels(["Cereza", "Cereza", "Bar"])).toEqual([true, true, false]);
    expect(winningReels(["Cereza", "Bar", "Cereza"])).toEqual([true, false, false]);
  });
});
