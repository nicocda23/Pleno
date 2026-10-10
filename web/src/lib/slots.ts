/** Como se ve cada simbolo. Si el servidor agrega uno nuevo en la configuracion, se muestra su nombre. */
const GLYPHS: Record<string, string> = {
  Cereza: "🍒",
  Limon: "🍋",
  Naranja: "🍊",
  Campana: "🔔",
  Bar: "BAR",
  Siete: "7",
};

export const glyphOf = (name: string): string => GLYPHS[name] ?? name;

/** Nombre legible (con tildes) para mostrar en pantalla. */
const LABELS: Record<string, string> = { Limon: "Limón" };
export const labelOf = (name: string): string => LABELS[name] ?? name;

/** Que tan grande fue el premio, segun cuantas veces devolvio la apuesta: decide cuanta fiesta se arma. */
export type WinTier = "win" | "big" | "mega";
export const winTier = (multiplier: number | null): WinTier => ((multiplier ?? 0) >= 50 ? "mega" : (multiplier ?? 0) >= 10 ? "big" : "win");

export const WIN_TITLES: Record<WinTier, string> = { win: "¡Ganaste!", big: "¡GRAN PREMIO!", mega: "¡MEGA PREMIO!" };

/** Que rodillos forman el premio: los tres si salieron iguales; si no, la racha del mismo simbolo desde la izquierda. */
export function winningReels(reels: readonly string[]): boolean[] {
  const first = reels[0];
  let run = 0;
  while (run < reels.length && reels[run] === first) run++;
  return reels.map((_, i) => i < run);
}
