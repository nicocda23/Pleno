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
