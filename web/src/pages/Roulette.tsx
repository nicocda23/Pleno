import { useEffect, useRef, useState } from "react";
import { useRound, usePlaceBet, useRounds } from "../api/hooks";
import type { PlaceBetBody, Round, RoundClosedNotice } from "../api/types";
import { Pocket } from "../components/Pocket";
import { useToasts } from "../components/Toasts";
import { formatChips } from "../lib/format";
import { errorMessage, failureMessage } from "../lib/messages";
import { OUTSIDE_BETS, pocketColor, straightBet, type BetOption } from "../lib/roulette";
import { useRealtime } from "../realtime/RealtimeProvider";

const STAKES = [10, 50, 100, 500];

/** Lo que se muestra al terminar una apuesta, venga del aviso en vivo o de la consulta de respaldo. */
interface Result {
  betId: string;
  status: "Settled" | "Rejected" | "Voided";
  winningNumber: number | null;
  stake: number;
  payout: number;
  failureReason: string | null;
}

const fromNotice = (n: RoundClosedNotice): Result => ({ ...n });
const fromRound = (r: Round): Result => ({
  betId: r.betId,
  status: r.status as Result["status"],
  winningNumber: r.winningNumber,
  stake: r.stake,
  payout: r.status === "Settled" ? (r.payout ?? 0) : 0,
  failureReason: r.failureReason,
});

export function Roulette() {
  const { balance, onRoundClosed } = useRealtime();
  const toasts = useToasts();
  const placeBet = usePlaceBet();
  const recent = useRounds(10);

  const [bet, setBet] = useState<BetOption>(OUTSIDE_BETS[0]!);
  const [straight, setStraight] = useState("17");
  const [stake, setStake] = useState(10);
  const [waitingFor, setWaitingFor] = useState<string | null>(null);
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Misma apuesta = misma clave de idempotencia: si la respuesta se pierde y el jugador reintenta, el servidor no cobra dos veces.
  const pending = useRef<{ fingerprint: string; key: string } | null>(null);

  const straightNumber = Number(straight);
  const straightValid = Number.isInteger(straightNumber) && straightNumber >= 0 && straightNumber <= 36;
  const selected: BetOption | null = bet.id === "straight" ? (straightValid ? straightBet(straightNumber) : null) : bet;
  const canBet = selected !== null && stake >= 1 && stake <= balance.available && waitingFor === null && !placeBet.isPending;

  const finish = (closed: Result) => {
    setResult((current) => (current?.betId === closed.betId ? current : closed));
    setWaitingFor((current) => (current === closed.betId ? null : current));
    if (closed.status === "Settled") {
      toasts.show(closed.payout > 0 ? "win" : "loss", closed.payout > 0 ? `Salió el ${closed.winningNumber}. Ganaste ${formatChips(closed.payout)} fichas.` : `Salió el ${closed.winningNumber}.`);
    } else if (closed.status === "Rejected") {
      toasts.show("error", failureMessage(closed.failureReason));
    } else {
      toasts.show("info", "La ronda se anuló y tus fichas volvieron a tu saldo.");
    }
  };
  const finishRef = useRef(finish);
  useEffect(() => {
    finishRef.current = finish;
  });

  // Aviso en vivo.
  useEffect(() => onRoundClosed((notice) => {
    if (notice.betId === waitingFor) finishRef.current(fromNotice(notice));
  }), [onRoundClosed, waitingFor]);

  // Respaldo: si el aviso se perdio, la consulta periodica cierra igual la ronda.
  const polled = useRound(waitingFor);
  useEffect(() => {
    const round = polled.data;
    if (round && waitingFor === round.betId && ["Settled", "Rejected", "Voided"].includes(round.status)) {
      finishRef.current(fromRound(round));
    }
  }, [polled.data, waitingFor]);

  const submit = async () => {
    if (!selected) return;
    setError(null);
    const body: PlaceBetBody = { betType: selected.betType, selection: selected.selection, stake };
    const fingerprint = JSON.stringify(body);
    if (pending.current?.fingerprint !== fingerprint) pending.current = { fingerprint, key: crypto.randomUUID() };

    try {
      const placed = await placeBet.mutateAsync({ body, idempotencyKey: pending.current.key });
      pending.current = null; // aceptada: la proxima apuesta usa otra clave
      setResult(null);
      setWaitingFor(placed.betId);
    } catch (e) {
      setError(errorMessage(e));
      // Error definitivo (la API lo rechazo): proxima apuesta con clave nueva. Si fue de red, se conserva para reintentar sin duplicar.
      if (e instanceof Error && "status" in e && (e as { status: number }).status !== 0) pending.current = null;
    }
  };

  return (
    <div className="stack">
      <header>
        <p className="eyebrow">Ruleta europea</p>
        <h1 className="display">Hacé tu apuesta</h1>
        <p className="muted">El servidor asigna el nonce de cada tirada: nadie puede elegir ni influir el resultado.</p>
      </header>

      <div className="table-grid">
        <section className="card" aria-labelledby="apuesta">
          <h2 id="apuesta" className="section-title">1. Elegí tu apuesta</h2>
          <div className="chips" role="radiogroup" aria-label="Tipo de apuesta">
            {OUTSIDE_BETS.map((option) => (
              <button
                key={option.id}
                type="button"
                role="radio"
                aria-checked={bet.id === option.id}
                className={`choice ${bet.id === option.id ? "choice--on" : ""}`}
                onClick={() => setBet(option)}
              >
                <span>{option.label}</span>
                <small>paga x{option.multiplier}</small>
              </button>
            ))}
            <button
              type="button"
              role="radio"
              aria-checked={bet.id === "straight"}
              className={`choice ${bet.id === "straight" ? "choice--on" : ""}`}
              onClick={() => setBet({ ...straightBet(0), id: "straight" })}
            >
              <span>Pleno</span>
              <small>paga x36</small>
            </button>
          </div>

          {bet.id === "straight" && (
            <label className="field">
              <span>Número (0 a 36)</span>
              <input type="number" inputMode="numeric" min={0} max={36} value={straight} onChange={(e) => setStraight(e.target.value)} aria-invalid={!straightValid} />
            </label>
          )}

          <h2 className="section-title">2. Cuántas fichas</h2>
          <div className="chips" role="radiogroup" aria-label="Fichas a apostar">
            {STAKES.map((value) => (
              <button key={value} type="button" role="radio" aria-checked={stake === value} className={`choice choice--stake ${stake === value ? "choice--on" : ""}`} onClick={() => setStake(value)}>
                {formatChips(value)}
              </button>
            ))}
          </div>
          <label className="field">
            <span>Otro monto</span>
            <input type="number" inputMode="numeric" min={1} step={1} value={stake} onChange={(e) => setStake(Math.max(0, Math.trunc(Number(e.target.value))))} />
          </label>

          <div className="summary">
            {selected ? (
              <span>
                {selected.label}: si ganás, cobrás <strong>{formatChips(stake * selected.multiplier)}</strong> fichas (incluye tu apuesta).
              </span>
            ) : (
              <span className="muted">Elegí un número válido.</span>
            )}
          </div>

          {stake > balance.available && balance.ready && <p className="notice notice--error" role="alert">No te alcanzan las fichas para esa apuesta.</p>}
          {error && <p className="notice notice--error" role="alert">{error}</p>}

          <button type="button" className="btn btn--gold btn--lg btn--block" disabled={!canBet} onClick={() => void submit()}>
            {waitingFor ? "Girando…" : placeBet.isPending ? "Enviando…" : `Apostar ${formatChips(stake)} fichas`}
          </button>
        </section>

        <section className="card result" aria-labelledby="resultado" aria-live="polite">
          <h2 id="resultado" className="section-title">Resultado</h2>
          {waitingFor && (
            <div className="spinwheel" role="status">
              <span className="spinwheel__ball" aria-hidden="true" />
              <p>La ruleta está girando…</p>
            </div>
          )}
          {!waitingFor && !result && <p className="muted">Tu próxima tirada aparece acá.</p>}
          {!waitingFor && result && <ResultView result={result} />}

          <h3 className="section-subtitle">Últimos números</h3>
          <div className="strip">
            {recent.data?.filter((r) => r.winningNumber !== null && r.status === "Settled").slice(0, 10).map((r) => (
              <Pocket key={r.betId} number={r.winningNumber!} size="sm" />
            ))}
            {recent.data?.every((r) => r.winningNumber === null) && <span className="muted">Sin tiradas todavía.</span>}
          </div>
        </section>
      </div>
    </div>
  );
}

function ResultView({ result }: { result: Result }) {
  if (result.status === "Rejected") {
    return (
      <div className="outcome outcome--bad">
        <p className="outcome__title">Apuesta rechazada</p>
        <p>{failureMessage(result.failureReason)}</p>
      </div>
    );
  }
  if (result.status === "Voided") {
    return (
      <div className="outcome">
        <p className="outcome__title">Ronda anulada</p>
        <p>Tus fichas volvieron a tu saldo.</p>
      </div>
    );
  }

  const won = result.payout > 0;
  const color = pocketColor(result.winningNumber ?? 0);
  return (
    <div className={`outcome ${won ? "outcome--win" : "outcome--loss"}`}>
      <Pocket number={result.winningNumber ?? 0} size="xl" />
      <p className="outcome__title">{won ? `Ganaste ${formatChips(result.payout)} fichas` : "No hubo suerte esta vez"}</p>
      <p className="muted">Salió el {result.winningNumber} ({color === "red" ? "rojo" : color === "black" ? "negro" : "verde"}). Apostaste {formatChips(result.stake)}.</p>
    </div>
  );
}
