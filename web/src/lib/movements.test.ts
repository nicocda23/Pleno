import { endOfLocalDayIso, movementLabel, signedChips, startOfLocalDayIso } from "./movements";

const fmt = (n: number) => String(n);

describe("movements helpers", () => {
  it("labels every kind of movement and falls back to the raw name for an unknown one", () => {
    expect(movementLabel("Stake")).toBe("Apuesta");
    expect(movementLabel("Prize")).toBe("Premio cobrado");
    expect(movementLabel("Algo" as never)).toBe("Algo");
  });

  it("writes the sign explicitly", () => {
    expect(signedChips(250, fmt)).toBe("+250");
    expect(signedChips(-100, fmt)).toBe("−100");
    expect(signedChips(0, fmt)).toBe("0");
  });

  it("turns a date into the start of that local day, and the end into the start of the next", () => {
    expect(startOfLocalDayIso("2026-10-09")).toBe(new Date(2026, 9, 9).toISOString());
    expect(endOfLocalDayIso("2026-10-09")).toBe(new Date(2026, 9, 10).toISOString());
    expect(endOfLocalDayIso("2026-12-31")).toBe(new Date(2027, 0, 1).toISOString());
  });

  it("refuses a malformed date", () => {
    expect(startOfLocalDayIso("")).toBeNull();
    expect(startOfLocalDayIso("09/10/2026")).toBeNull();
    expect(endOfLocalDayIso("nada")).toBeNull();
  });
});
