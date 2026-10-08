import { useEffect, useRef, useState } from "react";

const prefersReducedMotion = () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

/**
 * Lleva un numero hasta su nuevo valor con una animacion corta. El PRIMER valor aparece de golpe (no cuenta desde cero al
 * cargar): solo los cambios posteriores se animan. Se salta la animacion si el usuario pidio menos movimiento.
 * Mientras `target` es null (todavia no hay dato) devuelve null.
 *
 * El valor mostrado se DERIVA: es `target` salvo mientras corre una animacion hacia ese mismo `target`.
 */
export function useAnimatedNumber(target: number | null, durationMs = 700): number | null {
  const [tween, setTween] = useState<{ target: number; value: number } | null>(null);
  const settled = useRef<number | null>(null);

  useEffect(() => {
    if (target === null) {
      settled.current = null;
      return;
    }

    const from = settled.current;
    settled.current = target;
    if (from === null || from === target || prefersReducedMotion()) return;

    const start = performance.now();
    let frame = 0;
    const tick = (now: number) => {
      const progress = Math.min(1, (now - start) / durationMs);
      const eased = 1 - Math.pow(1 - progress, 3);
      setTween(progress < 1 ? { target, value: Math.round(from + (target - from) * eased) } : null);
      if (progress < 1) frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [target, durationMs]);

  if (target === null) return null;
  return tween && tween.target === target ? tween.value : target;
}
