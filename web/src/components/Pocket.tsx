import { pocketColor } from "../lib/roulette";

/** Casillero de la ruleta con su color. */
export function Pocket({ number, size = "md" }: { number: number; size?: "sm" | "md" | "xl" }) {
  const color = pocketColor(number);
  return (
    <span className={`pocket pocket--${color} pocket--${size}`} aria-label={`${number}, ${color === "red" ? "rojo" : color === "black" ? "negro" : "verde"}`}>
      {number}
    </span>
  );
}
