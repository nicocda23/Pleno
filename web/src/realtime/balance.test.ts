import { balanceReducer, initialBalance, type BalanceState } from "./balance";

const at = (version: number, available = 100, reserved = 0): BalanceState => ({ available, reserved, version, ready: true });

describe("balanceReducer", () => {
  it("starts not ready so the UI never shows a false zero", () => {
    expect(initialBalance.ready).toBe(false);
    expect(initialBalance.version).toBe(-1);
  });

  it("accepts the first HTTP snapshot", () => {
    const next = balanceReducer(initialBalance, { type: "seed", account: { available: 1000, reserved: 0, version: 2 } });

    expect(next).toEqual({ available: 1000, reserved: 0, version: 2, ready: true });
  });

  it("applies a newer live notice", () => {
    const next = balanceReducer(at(2, 1000), { type: "notice", notice: { available: 900, reserved: 100, version: 3 } });

    expect(next).toEqual({ available: 900, reserved: 100, version: 3, ready: true });
  });

  it("drops a stale notice that arrives out of order", () => {
    const state = at(5, 900, 100);

    const next = balanceReducer(state, { type: "notice", notice: { available: 1000, reserved: 0, version: 3 } });

    expect(next).toBe(state);
  });

  it("drops a repeated notice (at-least-once delivery)", () => {
    const state = at(4, 950);

    const next = balanceReducer(state, { type: "notice", notice: { available: 950, reserved: 0, version: 4 } });

    expect(next).toBe(state);
  });

  it("does not let an older HTTP snapshot roll the balance back", () => {
    const state = at(7, 800);

    const next = balanceReducer(state, { type: "seed", account: { available: 1000, reserved: 0, version: 2 } });

    expect(next).toBe(state);
  });

  it("accepts an HTTP snapshot with the same version (idempotent refresh)", () => {
    const next = balanceReducer(at(4, 950), { type: "seed", account: { available: 950, reserved: 0, version: 4 } });

    expect(next.available).toBe(950);
    expect(next.ready).toBe(true);
  });

  it("converges whatever the arrival order of the notices", () => {
    const notices = [2, 3, 4, 5, 6].map((version) => ({ available: 1000 - version * 10, reserved: version, version }));
    const shuffled = [notices[3]!, notices[0]!, notices[4]!, notices[2]!, notices[1]!, notices[4]!, notices[0]!];

    const final = shuffled.reduce<BalanceState>((state, notice) => balanceReducer(state, { type: "notice", notice }), initialBalance);

    expect(final.version).toBe(6);
    expect(final.available).toBe(940);
  });

  it("resets on logout", () => {
    expect(balanceReducer(at(9), { type: "reset" })).toEqual(initialBalance);
  });
});
