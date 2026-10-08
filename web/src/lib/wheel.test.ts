import { BALL_POCKET_RADIUS, BALL_TRACK_RADIUS, DEFAULT_TIMINGS, easeOutCubic, pocketAt, pocketCenterAngle, POCKETS, WHEEL_ORDER, WheelModel } from "./wheel";
import { pocketColor } from "./roulette";

const TAU = Math.PI * 2;

/** Avanza el modelo en cuadros de ~16 ms hasta que se asiente (o se agote el limite). */
function run(model: WheelModel, limitMs = 20_000, frame = 16) {
  let elapsed = 0;
  while (model.phase !== "settled" && elapsed < limitMs) {
    model.update(frame);
    elapsed += frame;
  }
  return elapsed;
}

describe("wheel layout", () => {
  it("contains every number 0 to 36 exactly once, starting at zero", () => {
    expect([...WHEEL_ORDER].sort((a, b) => a - b)).toEqual(Array.from({ length: POCKETS }, (_, n) => n));
    expect(WHEEL_ORDER[0]).toBe(0);
    expect(POCKETS).toBe(37);
  });

  it("alternates red and black around the wheel (the zero is the only green)", () => {
    for (let i = 1; i < POCKETS - 1; i++) {
      expect(pocketColor(WHEEL_ORDER[i]!)).not.toBe(pocketColor(WHEEL_ORDER[i + 1]!));
    }
    expect(pocketColor(WHEEL_ORDER[POCKETS - 1]!)).not.toBe(pocketColor(WHEEL_ORDER[1]!));
  });

  it("matches the known european sequence around the zero", () => {
    expect(WHEEL_ORDER.slice(0, 4)).toEqual([0, 32, 15, 19]);
    expect(WHEEL_ORDER.slice(-3)).toEqual([35, 3, 26]);
  });

  it("maps a number to its pocket center and back", () => {
    for (let n = 0; n <= 36; n++) {
      expect(pocketAt(pocketCenterAngle(n))).toBe(n);
      expect(pocketAt(pocketCenterAngle(n) + TAU * 3)).toBe(n); // vueltas completas no cambian el casillero
      expect(pocketAt(pocketCenterAngle(n) - TAU)).toBe(n);
    }
  });

  it("rejects a number that is not on the wheel", () => {
    expect(() => pocketCenterAngle(37)).toThrow(RangeError);
    expect(() => pocketCenterAngle(-1)).toThrow(RangeError);
  });
});

describe("WheelModel", () => {
  it("starts idle with the ball resting at the zero", () => {
    const model = new WheelModel();
    model.update(500);

    expect(model.phase).toBe("idle");
    expect(model.result).toBeNull();
    expect(model.wheelAngle).toBeGreaterThan(0); // gira despacio mientras espera
  });

  it("lands the ball exactly on the winning pocket, for every number", () => {
    for (let n = 0; n <= 36; n++) {
      const model = new WheelModel();
      model.spin();
      model.update(700);
      model.land(n);
      run(model);

      expect(model.phase).toBe("settled");
      expect(model.result).toBe(n);
      // En el marco de la rueda, la bola esta en el centro del casillero ganador.
      expect(pocketAt(model.ballAngle - model.wheelAngle)).toBe(n);
      const local = (((model.ballAngle - model.wheelAngle) % TAU) + TAU) % TAU;
      expect(local).toBeCloseTo(pocketCenterAngle(n), 6);
      expect(model.ballRadius).toBeCloseTo(BALL_POCKET_RADIUS, 6);
    }
  });

  it("keeps spinning while the result is not known yet (the server may take a while)", () => {
    const model = new WheelModel();
    model.spin();

    run(model, 10_000);

    expect(model.phase).toBe("spinning");
    expect(model.result).toBeNull();
  });

  it("does not land before the minimum spin time even if the result is known at once", () => {
    const model = new WheelModel();
    model.spin();
    model.land(17);

    model.update(DEFAULT_TIMINGS.minSpinMs - 100);

    expect(model.phase).toBe("spinning");

    model.update(200);
    expect(model.phase).toBe("landing");
  });

  it("settles within a bounded, suspenseful time", () => {
    const model = new WheelModel();
    model.spin();
    model.land(8);

    const elapsed = run(model);

    expect(elapsed).toBeGreaterThanOrEqual(DEFAULT_TIMINGS.minSpinMs + DEFAULT_TIMINGS.landingMs - 40);
    expect(elapsed).toBeLessThanOrEqual(DEFAULT_TIMINGS.minSpinMs + DEFAULT_TIMINGS.landingMs + 100);
  });

  it("makes the ball go around the wheel several times before dropping, against the wheel direction", () => {
    const model = new WheelModel();
    model.spin();
    model.update(100);
    model.land(25);
    model.update(DEFAULT_TIMINGS.minSpinMs);
    expect(model.phase).toBe("landing");
    const ballAtLanding = model.ballAngle;

    run(model);

    const turns = (ballAtLanding - model.ballAngle) / TAU;
    expect(turns).toBeGreaterThanOrEqual(DEFAULT_TIMINGS.extraTurns - 0.01);
    expect(turns).toBeLessThanOrEqual(DEFAULT_TIMINGS.extraTurns + 1.01);
  });

  it("drops the ball from the track into the pocket only near the end", () => {
    const model = new WheelModel();
    model.spin();
    model.land(30);
    model.update(DEFAULT_TIMINGS.minSpinMs + 100);

    expect(model.phase).toBe("landing");
    expect(model.ballRadius).toBeCloseTo(BALL_TRACK_RADIUS, 1);

    run(model);
    expect(model.ballRadius).toBeCloseTo(BALL_POCKET_RADIUS, 6);
  });

  it("can start landing on a result that arrives after the minimum time", () => {
    const model = new WheelModel();
    model.spin();
    model.update(DEFAULT_TIMINGS.minSpinMs + 3_000);
    expect(model.phase).toBe("spinning");

    model.land(12);
    model.update(16);

    expect(model.phase).toBe("landing");
    run(model);
    expect(model.result).toBe(12);
  });

  it("with reduced motion settles at once, with no animation", () => {
    const model = new WheelModel({ reduceMotion: true });
    model.spin();
    model.land(21);

    expect(model.phase).toBe("settled");
    expect(model.result).toBe(21);
    expect(pocketAt(model.ballAngle - model.wheelAngle)).toBe(21);
  });

  it("returns to rest when the bet is cancelled, with no result", () => {
    const model = new WheelModel();
    model.spin();
    model.update(500);

    model.cancel();

    expect(model.phase).toBe("idle");
    expect(model.result).toBeNull();
  });

  it("can spin again after settling", () => {
    const model = new WheelModel();
    model.spin();
    model.land(5);
    run(model);
    expect(model.result).toBe(5);

    model.spin();
    expect(model.phase).toBe("spinning");
    expect(model.result).toBeNull();
    model.land(31);
    run(model);

    expect(model.result).toBe(31);
    expect(pocketAt(model.ballAngle - model.wheelAngle)).toBe(31);
  });

  it("lands correctly regardless of how the frames are sliced", () => {
    for (const frame of [4, 16, 33, 100]) {
      const model = new WheelModel();
      model.spin();
      model.land(13);
      run(model, 30_000, frame);

      expect(model.result).toBe(13);
      expect(pocketAt(model.ballAngle - model.wheelAngle)).toBe(13);
    }
  });

  it("eases out: the ball slows down as it lands", () => {
    expect(easeOutCubic(0)).toBe(0);
    expect(easeOutCubic(1)).toBe(1);
    expect(easeOutCubic(0.5) - easeOutCubic(0.4)).toBeLessThan(easeOutCubic(0.1) - easeOutCubic(0));
  });

  it("refuses an unknown winning number", () => {
    const model = new WheelModel();
    model.spin();

    expect(() => model.land(99)).toThrow(RangeError);
  });
});
