const chips = new Intl.NumberFormat("es-AR", { maximumFractionDigits: 0 });

/** Fichas con separador de miles: 1000 -> "1.000". Las fichas son enteros: cualquier decimal se descarta. */
export function formatChips(value: number): string {
  return chips.format(Math.trunc(value));
}

const dateTime = new Intl.DateTimeFormat("es-AR", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });

export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso));
}
