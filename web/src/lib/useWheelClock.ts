import { useEffect, useRef, useState } from "react";
import type { WheelModel, WheelPhase } from "./wheel";

/**
 * Hace avanzar el modelo de la rueda con requestAnimationFrame y avisa cuando cambia de fase.
 * Vive aparte del dibujo: la bola "cae" aunque PixiJS no pueda dibujar, y el resultado se revela recien cuando se asienta.
 */
export function useWheelClock(model: WheelModel, onSettled: (result: number) => void): WheelPhase {
  const [phase, setPhase] = useState<WheelPhase>(model.phase);
  const settled = useRef(onSettled);
  useEffect(() => {
    settled.current = onSettled;
  });

  useEffect(() => {
    let frame = 0;
    let last: number | null = null;
    let current = model.phase;

    const tick = (now: number) => {
      const delta = last === null ? 0 : Math.min(now - last, 100); // una pestaña en segundo plano no debe "saltar" la animacion
      last = now;
      model.update(delta);
      if (model.phase !== current) {
        const before = current;
        current = model.phase;
        setPhase(current);
        if (before !== "settled" && current === "settled" && model.result !== null) settled.current(model.result);
      }
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [model]);

  return phase;
}
