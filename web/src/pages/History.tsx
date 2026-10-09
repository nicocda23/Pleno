import { useState } from "react";
import { useFairness, useRotateSeeds, useRounds } from "../api/hooks";
import type { Round } from "../api/types";
import { Pocket } from "../components/Pocket";
import { RoundBadge } from "../components/RoundBadge";
import { formatChips, formatDateTime } from "../lib/format";
import { errorMessage, outcomeText } from "../lib/messages";
import { describeRound } from "../lib/roulette";
import { verifyUrl } from "../lib/verify";

export function History() {
  const rounds = useRounds(50);
  const fairness = useFairness();
  const rotate = useRotateSeeds();
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const retired = fairness.data?.retired ?? [];
  const pending = fairness.data?.pendingBets ?? 0;
  const activePair = fairness.data?.active.pairId;
  const hasUnverifiable = rounds.data?.some((r) => r.winningNumber !== null && r.pairId === activePair) ?? false;

  const reveal = async () => {
    setError(null);
    try {
      await rotate.mutateAsync();
      setConfirming(false);
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <div className="stack">
      <header>
        <p className="eyebrow">Tus jugadas</p>
        <h1 className="display">Historial</h1>
        <p className="muted">Cada resultado se puede comprobar: recalculalo vos mismo con la herramienta de verificación.</p>
      </header>

      {hasUnverifiable && (
        <section className="card verify-box" aria-labelledby="verificar">
          <h2 id="verificar" className="section-title">Verificar tus últimas jugadas</h2>
          <p>
            Para comprobar una tirada necesitás la <strong>semilla secreta del servidor</strong>. Mientras se usa, es secreta (si no, podrias predecir resultados). Al
            revelarla, se cierra ese grupo de jugadas y se empieza uno nuevo con otra semilla.
          </p>
          {!confirming ? (
            <button type="button" className="btn btn--ghost" onClick={() => setConfirming(true)} disabled={pending > 0}>
              {pending > 0 ? "Hay apuestas en curso" : "Revelar semilla y verificar"}
            </button>
          ) : (
            <div className="confirm" role="alertdialog" aria-label="Confirmar revelado de semilla">
              <p>¿Revelar la semilla actual? Las tiradas hechas hasta ahora quedan verificables y se empieza un grupo nuevo.</p>
              <button type="button" className="btn btn--gold" onClick={() => void reveal()} disabled={rotate.isPending}>
                {rotate.isPending ? "Revelando…" : "Sí, revelar"}
              </button>
              <button type="button" className="btn btn--ghost" onClick={() => setConfirming(false)}>Cancelar</button>
            </div>
          )}
          {error && <p className="notice notice--error" role="alert">{error}</p>}
        </section>
      )}

      <section className="card" aria-label="Jugadas">
        {rounds.isLoading && <p className="muted">Cargando…</p>}
        {rounds.isError && <p className="notice notice--error" role="alert">No pudimos cargar tu historial.</p>}
        {rounds.data?.length === 0 && <p className="muted">Todavía no jugaste.</p>}

        {!!rounds.data?.length && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Fecha</th>
                  <th scope="col">Apuesta</th>
                  <th scope="col">Salió</th>
                  <th scope="col">Resultado</th>
                  <th scope="col">Estado</th>
                  <th scope="col"><span className="sr-only">Verificar</span></th>
                </tr>
              </thead>
              <tbody>
                {rounds.data.map((round) => (
                  <HistoryRow key={round.betId} round={round} link={verifyUrl(round, retired)} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

function HistoryRow({ round, link }: { round: Round; link: string | null }) {
  const showsNumber = round.winningNumber !== null && round.status !== "Rejected";
  return (
    <tr>
      <td>{formatDateTime(round.placedAt)}</td>
      <td>
        {describeRound(round)}
        <span className="muted"> · {formatChips(round.stake)}</span>
      </td>
      <td>{showsNumber ? <Pocket number={round.winningNumber!} size="sm" /> : <span className="muted">—</span>}</td>
      <td>{outcomeText(round)}</td>
      <td><RoundBadge status={round.status} /></td>
      <td>
        {link ? (
          <a className="link-btn" href={link} target="_blank" rel="noreferrer">Verificar</a>
        ) : (
          <span className="muted" title="Revelá la semilla para poder verificar">—</span>
        )}
      </td>
    </tr>
  );
}
