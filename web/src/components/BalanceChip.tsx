import { useEffect, useRef, useState } from "react";
import { formatChips } from "../lib/format";
import { useRealtime } from "../realtime/RealtimeProvider";
import { useAnimatedNumber } from "./useAnimatedNumber";

/** Saldo disponible en vivo. Destella en verde cuando sube y en rojo cuando baja. */
export function BalanceChip({ size = "md" }: { size?: "md" | "xl" }) {
  const { balance } = useRealtime();
  const shown = useAnimatedNumber(balance.ready ? balance.available : null);
  const previous = useRef<number | null>(null);
  const [flash, setFlash] = useState<"gain" | "loss" | null>(null);

  useEffect(() => {
    if (!balance.ready) return;
    // El primer dato real aparece sin destello: solo los cambios posteriores marcan ganancia o perdida.
    if (previous.current === null) {
      previous.current = balance.available;
      return;
    }
    if (balance.available !== previous.current) {
      setFlash(balance.available > previous.current ? "gain" : "loss");
      previous.current = balance.available;
      const timer = window.setTimeout(() => setFlash(null), 900);
      return () => window.clearTimeout(timer);
    }
  }, [balance.available, balance.ready]);

  return (
    <div className={`balance balance--${size} ${flash ? `balance--${flash}` : ""}`} aria-live="polite">
      <span className="balance__label">Fichas</span>
      <span className="balance__value" data-testid="balance-value">
        {shown === null ? "—" : formatChips(shown)}
      </span>
    </div>
  );
}
