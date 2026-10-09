import { formatChips } from "./format";

describe("formatChips", () => {
  it("groups thousands", () => {
    expect(formatChips(1000)).toBe("1.000");
    expect(formatChips(1234567)).toBe("1.234.567");
  });

  it("prints small values and zero", () => {
    expect(formatChips(0)).toBe("0");
    expect(formatChips(50)).toBe("50");
  });

  it("never shows decimals: chips are whole numbers", () => {
    expect(formatChips(99.9)).toBe("99");
    expect(formatChips(-12.7)).toBe("-12");
  });
});
