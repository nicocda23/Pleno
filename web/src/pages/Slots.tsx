import { useEffect, useRef, useState } from "react";
import { usePaytable, usePlaceSpin, useSpin, useSpins } from "../api/hooks";
import type { Paytable, Spin } from "../api/types";
import { Confetti } from "../components/Confetti";
import { useToasts } from "../components/Toasts";
import { formatChips } from "../lib/format";
import { errorMessage, failureMessage } from "../lib/messages";
import { ReelsModel } from "../lib/reels";
import { glyphOf, labelOf } from "../lib/slots";
import { useWheelClock } from "../lib/useWheelClock";
import { playWinSound } from "../lib/winSound";
import { useRealtime } from "../realtime/RealtimeProvider";

const CHIPS = [10, 50, 100, 500];
const AUTO_COUNTS = [10, 25, 50, 100];
const AUTO_PAUSE_MS = 700;

const prefersReducedMotion = () =>
  typeof window !== "undefined" && typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Carga la tabla de pagos (publica) y recien despues arma la maquina: los rodillos necesitan saber cuantos simbolos hay. */
export function Slots() {
  const paytable = usePaytable();

  if (paytable.isError) return <p className="notice notice--error" role="alert">No se pudo cargar la tragamonedas. Probá de nuevo en un rato.</p>;
  if (!paytable.data) return <p className="muted" role="status">Cargando la tragamonedas…</p>;
  return <Machine paytable={paytable.data} />;
}

function Machine({ paytable }: { paytable: Paytable }) {
  const { balance, onRoundClosed, holdBalance } = useRealtime();
  const toasts = useToasts();
  const placeSpin = usePlaceSpin();
  const recent = useSpins(30);
  const names = paytable.symbols.map((s) => s.name);

  const [model] = useState(() => new ReelsModel(names.length, paytable.reels, { reduceMotion: prefersReducedMotion() }));
  const defaultStake = Math.min(10, paytable.maxStake);
  const [stake, setStake] = useState(defaultStake);
  // Texto del campo "Otro": vacio mientras se usa una ficha rapida; con contenido cuando el monto es personalizado.
  const [custom, setCustom] = useState("");
  const [waitingFor, setWaitingFor] = useState<string | null>(null);
  const [spinning, setSpinning] = useState(false);
  const [result, setResult] = useState<Spin | null>(null);
  const [winPopup, setWinPopup] = useState<Spin | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Giros automaticos que faltan (0 = apagado). El ref lo lee reveal(), que puede correr desde un callback viejo.
  const [auto, setAutoState] = useState(0);
  const autoRef = useRef(0);
  const setAuto = (n: number) => {
    autoRef.current = n;
    setAutoState(n);
  };

  // Misma apuesta = misma clave de idempotencia: si la respuesta se pierde y el jugador reintenta, el servidor no cobra dos veces.
  const pending = useRef<{ stake: number; key: string } | null>(null);
  // Resultado final que el cliente ya conoce pero que se muestra recien cuando los rodillos se detienen.
  const closedRef = useRef<Spin | null>(null);
  const landedRef = useRef(false);
  const reelsDoneRef = useRef(false);
  const releaseRef = useRef<(() => void) | null>(null);

  const release = () => {
    releaseRef.current?.();
    releaseRef.current = null;
  };
  useEffect(() => release, []);

  const stakeValid = Number.isInteger(stake) && stake >= paytable.minStake && stake <= paytable.maxStake;
  const canSpin = stakeValid && stake <= balance.available && !spinning && !placeSpin.isPending;

  const affordable = stakeValid ? Math.floor(balance.available / stake) : 0;

  const reveal = (spin: Spin) => {
    setResult(spin);
    setSpinning(false);
    setWaitingFor(null);
    release(); // recien ahora el saldo se actualiza
    if (spin.status === "Settled") {
      const payout = spin.payout ?? 0;
      if (payout > spin.stake && autoRef.current > 0) {
        // En automatico no se interrumpe con el popup: un aviso y sigue.
        toasts.show("info", `Ganaste ${formatChips(payout - spin.stake)} fichas.`);
        playWinSound();
      } else if (payout > spin.stake) {
        setWinPopup(spin);
        playWinSound();
      } else if (autoRef.current > 0) {
        // En automatico solo se avisa cuando se gana.
      } else if (payout === spin.stake) toasts.show("info", "Recuperaste tus fichas.");
      else toasts.show("loss", "Sin premio esta vez.");
    } else if (spin.status === "Rejected") {
      setAuto(0);
      toasts.show("error", failureMessage(spin.failureReason));
    } else {
      setAuto(0);
      toasts.show("info", "El giro se anuló y tus fichas volvieron a tu saldo.");
    }
  };

  /** El resultado se muestra cuando los rodillos ya frenaron Y el giro ya esta cobrado (lo que llegue ultimo). */
  const tryReveal = () => {
    const closed = closedRef.current;
    if (!closed || !reelsDoneRef.current) return;
    closedRef.current = null;
    reveal(closed);
  };

  const phase = useWheelClock(model, () => {
    reelsDoneRef.current = true;
    tryReveal();
  });

  const polled = useSpin(waitingFor);
  const onSpinState = (spin: Spin) => {
    if (spin.betId !== waitingFor) return;
    // En cuanto se sortea, los rodillos pueden empezar a frenar (aunque falte cobrar).
    if (spin.reels.length === paytable.reels && !landedRef.current) {
      landedRef.current = true;
      model.land(spin.reels.map((name) => Math.max(0, names.indexOf(name))));
    }
    if (spin.status === "Settled" && !closedRef.current) {
      closedRef.current = spin;
      tryReveal();
    } else if (spin.status === "Rejected" || spin.status === "Voided") {
      model.cancel();
      closedRef.current = null;
      reveal(spin);
    }
  };
  const onSpinStateRef = useRef(onSpinState);
  useEffect(() => {
    onSpinStateRef.current = onSpinState;
  });
  useEffect(() => {
    if (polled.data) onSpinStateRef.current(polled.data);
  }, [polled.data]);

  // Aviso en vivo: no trae los rodillos, asi que solo apura la consulta del giro.
  const refetchRef = useRef(polled.refetch);
  useEffect(() => {
    refetchRef.current = polled.refetch;
  });
  const waitingRef = useRef(waitingFor);
  useEffect(() => {
    waitingRef.current = waitingFor;
  });
  useEffect(
    () =>
      onRoundClosed((notice) => {
        if (notice.betId === waitingRef.current) void refetchRef.current();
      }),
    [onRoundClosed],
  );

  const submit = async () => {
    if (!canSpin) return;
    setError(null);
    if (pending.current?.stake !== stake) pending.current = { stake, key: crypto.randomUUID() };

    // Los rodillos empiezan a girar ya, sin conocer el resultado, y el saldo se congela hasta que frenen.
    setResult(null);
    setSpinning(true);
    closedRef.current = null;
    landedRef.current = false;
    reelsDoneRef.current = false;
    releaseRef.current = holdBalance();
    model.spin();

    try {
      const accepted = await placeSpin.mutateAsync({ stake, idempotencyKey: pending.current.key });
      pending.current = null; // aceptado: el proximo giro usa otra clave
      setWaitingFor(accepted.betId);
    } catch (e) {
      model.cancel();
      setSpinning(false);
      setAuto(0);
      release();
      setError(errorMessage(e));
      // Error definitivo (la API lo rechazo): proximo giro con clave nueva. Si fue de red, se conserva para reintentar sin duplicar.
      if (e instanceof Error && "status" in e && (e as { status: number }).status !== 0) pending.current = null;
    }
  };

  const submitRef = useRef(submit);
  useEffect(() => {
    submitRef.current = submit;
  });

  // Juego automatico: cuando los rodillos frenan y quedan giros, tras una pausa corta se lanza el siguiente.
  useEffect(() => {
    if (auto <= 0 || spinning || placeSpin.isPending) return;
    const timer = setTimeout(() => {
      if (!canSpin) {
        setAuto(0);
        if (stakeValid && stake > balance.available) toasts.show("info", "Se detuvo el juego automático: no te alcanzan las fichas.");
        return;
      }
      setAuto(autoRef.current - 1);
      void submitRef.current();
    }, result ? AUTO_PAUSE_MS : 0);
    return () => clearTimeout(timer);
  }, [auto, spinning, placeSpin.isPending, canSpin, result, stakeValid, stake, balance.available, toasts]);

  const label = phase === "settled" && result ? `Salió ${result.reels.map(labelOf).join(", ")}` : phase === "idle" ? "Rodillos quietos" : "Los rodillos están girando";

  return (
    <div className="stack">
      <header>
        <p className="eyebrow">Tragamonedas</p>
        <h1 className="display">Probá tu suerte</h1>
        <p className="muted">El servidor sortea cada giro con un nonce propio. Retorno teórico: {paytable.returnToPlayerPercent.toLocaleString("es-AR")} %.</p>
      </header>

      <div className="table-grid">
        <section className="card" aria-labelledby="maquina">
          <h2 id="maquina" className="section-title">La máquina</h2>
          <ReelsView model={model} names={names} phase={phase} label={label} />

          <h3 className="section-subtitle">Cuántas fichas</h3>
          <div className="chips chips--stake" role="radiogroup" aria-label="Fichas a apostar">
            {CHIPS.filter((value) => value <= paytable.maxStake).map((value) => (
              <button key={value} type="button" role="radio" aria-checked={stake === value} disabled={spinning || auto > 0} className={`choice choice--stake ${stake === value ? "choice--on" : ""}`} onClick={() => {
                setStake(value);
                setCustom("");
              }}>
                {formatChips(value)}
              </button>
            ))}
            <input
              className={`chip-input ${custom === "" ? "" : "chip-input--on"}`}
              type="number" inputMode="numeric" min={paytable.minStake} max={paytable.maxStake} step={1}
              placeholder="✎" aria-label={`Otro monto (${formatChips(paytable.minStake)} a ${formatChips(paytable.maxStake)})`}
              value={custom} disabled={spinning || auto > 0} aria-invalid={custom !== "" && !stakeValid}
              onChange={(e) => {
                setCustom(e.target.value);
                setStake(e.target.value === "" ? defaultStake : Math.trunc(Number(e.target.value)));
              }}
            />
          </div>

          {stake > balance.available && balance.ready && stakeValid && <p className="notice notice--error" role="alert">No te alcanzan las fichas para esa apuesta.</p>}
          {error && <p className="notice notice--error" role="alert">{error}</p>}

          <div className="dock">
            <button type="button" className="btn btn--gold btn--lg btn--block" disabled={!canSpin} onClick={() => void submit()}>
              {spinning ? "Girando…" : placeSpin.isPending ? "Enviando…" : `Girar por ${formatChips(stake)} fichas`}
            </button>
          </div>

          <h3 className="section-subtitle">Juego automático</h3>
          {auto > 0 ? (
            <button type="button" className="btn btn--ghost btn--block" onClick={() => setAuto(0)}>
              Detener (quedan {auto})
            </button>
          ) : (
            <div className="chips" role="group" aria-label="Giros automáticos">
              {AUTO_COUNTS.map((count) => (
                <button key={count} type="button" className="choice" disabled={!canSpin || count * stake > balance.available} onClick={() => setAuto(count)}>
                  {count} giros
                </button>
              ))}
            </div>
          )}
          {auto === 0 && stakeValid && balance.ready && affordable < Math.max(...AUTO_COUNTS) && (
            <p className={`notice ${affordable < Math.min(...AUTO_COUNTS) ? "notice--error" : ""}`} role="status">
              {affordable < Math.min(...AUTO_COUNTS)
                ? `Juego automático no disponible: con ${formatChips(stake)} fichas por giro te alcanzan para ${affordable} ${affordable === 1 ? "giro" : "giros"} y el juego automático arranca desde ${Math.min(...AUTO_COUNTS)} giros. Bajá la apuesta o cargá fichas.`
                : `Con ${formatChips(stake)} fichas por giro te alcanzan para ${affordable} giros: las opciones más largas están deshabilitadas.`}
            </p>
          )}

          <div aria-live="polite">{!spinning && result && <SpinResult spin={result} />}</div>
        </section>

        <section className="card" aria-labelledby="pagos">
          <h2 id="pagos" className="section-title">Tabla de pagos</h2>
          <ul className="paytable" aria-label="Premios">
            {[...paytable.symbols].sort((a, b) => b.triplePayout - a.triplePayout).map((symbol) => (
              <li key={symbol.name}>
                <span className="paytable__reels" aria-hidden="true">{`${glyphOf(symbol.name)} ${glyphOf(symbol.name)} ${glyphOf(symbol.name)}`}</span>
                <span className="sr-only">Tres {labelOf(symbol.name)}</span>
                <strong>x{formatChips(symbol.triplePayout)}</strong>
              </li>
            ))}
            {[...paytable.leadingPays].sort((a, b) => b.count - a.count).map((pay) => (
              <li key={`${pay.symbol}-${pay.count}`}>
                <span>{pay.count === 1 ? `${labelOf(pay.symbol)} en el primer rodillo` : `${pay.count} ${labelOf(pay.symbol)} seguidas desde la izquierda`}</span>
                <strong>x{formatChips(pay.payout)}</strong>
              </li>
            ))}
          </ul>
          <p className="muted">
            Retorno teórico {paytable.returnToPlayerPercent.toLocaleString("es-AR")} %. Algún premio en {paytable.hitRatePercent.toLocaleString("es-AR")} de cada 100 giros, casi siempre chico: los grandes son raros.
          </p>

          <h3 className="section-subtitle">Últimos giros</h3>
          <ul className="spin-list" aria-label="Últimos giros">
            {recent.data?.filter((s) => s.status === "Settled").slice(0, 30).map((s) => (
              <li key={s.betId}>
                <span aria-hidden="true">{s.reels.map(glyphOf).join(" ")}</span>
                <span className="sr-only">{s.reels.map(labelOf).join(", ")}</span>
                <span className={(s.payout ?? 0) > s.stake ? "win" : "muted"}>{(s.payout ?? 0) > 0 ? `+${formatChips(s.payout ?? 0)}` : "—"}</span>
              </li>
            ))}
            {recent.data?.every((s) => s.status !== "Settled") && <li className="muted">Sin giros todavía.</li>}
          </ul>
        </section>
      </div>

      {winPopup && (
        <div className="win-modal" role="dialog" aria-modal="true" aria-labelledby="win-title" onClick={() => setWinPopup(null)}>
          <Confetti />
          <div className="win-modal__box" onClick={(e) => e.stopPropagation()}>
            <div className="reels reels--static" aria-hidden="true">
              {winPopup.reels.map((name, i) => (
                <span key={i} className="reel">{glyphOf(name)}</span>
              ))}
            </div>
            <h2 id="win-title" className="win-modal__title">¡Ganaste!</h2>
            <p className="win-modal__amount">{formatChips((winPopup.payout ?? 0) - winPopup.stake)} fichas</p>
            <p className="muted">Apostaste {formatChips(winPopup.stake)} y cobraste {formatChips(winPopup.payout ?? 0)} (x{winPopup.multiplier}).</p>
            <button type="button" className="btn btn--gold" autoFocus onClick={() => setWinPopup(null)}>
              Continuar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

/** Dibuja lo que muestra el modelo: el modelo cambia solo, asi que se lo mira en cada cuadro y se re-renderiza solo si algo cambio. */
function ReelsView({ model, names, phase, label }: { model: ReelsModel; names: string[]; phase: string; label: string }) {
  const [shown, setShown] = useState(() => [...model.display]);
  const [stopped, setStopped] = useState(() => [...model.stopped]);

  useEffect(() => {
    let frame = 0;
    const tick = () => {
      setShown((current) => (current.every((v, i) => v === model.display[i]) ? current : [...model.display]));
      setStopped((current) => (current.every((v, i) => v === model.stopped[i]) ? current : [...model.stopped]));
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [model]);

  return (
    <div className="reels" role="img" aria-label={label} data-testid="reels" data-state={phase}>
      {shown.map((symbol, i) => (
        <span key={i} className={`reel ${stopped[i] ? "" : "reel--spinning"}`} data-testid={`reel-${i}`} data-symbol={names[symbol]}>
          {stopped[i] ? (
            <span className="reel__cell reel__cell--landed">{glyphOf(names[symbol] ?? "")}</span>
          ) : (
            // Cinta vertical con los simbolos repetidos: al desplazarla la mitad se ve continua. Cada rodillo, a su velocidad.
            <span className="reel__strip" aria-hidden="true" style={{ animationDuration: `${(names.length * (0.07 + (i % 2) * 0.035)).toFixed(3)}s`, animationDelay: `${-i * 0.11}s` }}>
              {[...names, ...names].map((name, k) => (
                <span key={k} className="reel__cell">{glyphOf(name)}</span>
              ))}
            </span>
          )}
        </span>
      ))}
    </div>
  );
}

function SpinResult({ spin }: { spin: Spin }) {
  if (spin.status === "Rejected") {
    return (
      <div className="outcome outcome--bad">
        <p className="outcome__title">Giro rechazado</p>
        <p>{failureMessage(spin.failureReason)}</p>
      </div>
    );
  }
  if (spin.status === "Voided") {
    return (
      <div className="outcome">
        <p className="outcome__title">Giro anulado</p>
        <p>Tus fichas volvieron a tu saldo.</p>
      </div>
    );
  }

  const payout = spin.payout ?? 0;
  const won = payout > spin.stake;
  return (
    <div className={`outcome ${won ? "outcome--win" : "outcome--loss"}`}>
      <p className="outcome__title">{won ? `Ganaste ${formatChips(payout - spin.stake)} fichas` : payout === spin.stake ? "Recuperaste tus fichas" : "No hubo suerte esta vez"}</p>
      <p className="muted">Apostaste {formatChips(spin.stake)} y cobraste {formatChips(payout)}.</p>
    </div>
  );
}
