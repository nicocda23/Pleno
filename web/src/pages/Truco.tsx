import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type CSSProperties } from "react";
import { queryKeys, useTableAction } from "../api/hooks";
import type { TrucoAction, TrucoActionType, TrucoView } from "../api/types";
import { Confetti } from "../components/Confetti";
import { TablesLobby, type LiveTable } from "../components/TablesLobby";
import { sha256Hex } from "../lib/crash";
import { formatChips } from "../lib/format";
import { detailedErrorMessage } from "../lib/messages";
import { SUIT_CSS, actionLabel, byStrength, decodeCard, eventText, groupOf, GROUP_LABELS, pendingText, playBody, trucoLevelName, trucoWorth, type ActionGroup } from "../lib/truco";
import { playWinSound } from "../lib/winSound";

const GAME_ID = "truco";

/** Truco: mesas de 2 jugadores (o jugador contra bot). El primero que llega a los puntos de la partida se lleva todo lo que se puso en la mesa. */
export function Truco() {
  return (
    <div className="stack">
      <header>
        <p className="eyebrow">Truco</p>
        <h1 className="display">Truco, quiero retruco</h1>
        <p className="muted">Jugá al truco argentino contra otra persona o contra un bot, con la entrada que quieras. Quien llega primero a 15 puntos se lleva todo. El mazo se baraja con una semilla comprometida de antemano y se revela al terminar.</p>
      </header>
      <TablesLobby<TrucoView> gameId={GAME_ID} gameName="Truco" renderGame={(table, leave) => <TrucoTable table={table} onLeave={leave} />} />
      <Rules />
    </div>
  );
}

function Rules() {
  return (
    <section className="card" aria-labelledby="tru-reglas">
      <h2 id="tru-reglas" className="section-title">Reglas</h2>
      <ul className="tru-rules">
        <li>Baraja española de 40 cartas, sin flor. Cada ronda se reparten 3 cartas y se juegan hasta 3 bazas: gana la ronda quien gana 2 (la mano desempata una parda).</li>
        <li>De mayor a menor: 1 de espadas, 1 de bastos, 7 de espadas, 7 de oros, los 3, los 2, los 1 de copas y oros, 12, 11, 10, los 7 de copas y bastos, 6, 5 y 4.</li>
        <li>Envido, en la primera baza: se cuentan 20 más las dos mejores cartas del mismo palo (las figuras valen 0); sin dos del mismo palo, vale la carta más alta. Se puede subir con real envido y falta envido.</li>
        <li>Truco: vale 2 puntos la ronda; se sube con retruco (3) y vale cuatro (4). Si no querés, el otro suma los puntos que valía antes del canto.</li>
        <li>"Al mazo" abandona la ronda y el otro se lleva los puntos que estén en juego.</li>
        <li>Gana quien llega a 15 puntos y se lleva la suma de las entradas.</li>
        <li>Cada turno dura 45 segundos. Si no jugás 3 turnos seguidos quedás ausente y un bot juega por vos.</li>
      </ul>
    </section>
  );
}

/** Una carta con CSS: sin imagenes. Con `onClick` es un boton; sin el, una imagen para lectores de pantalla. */
function TrucoCard({ card, label, onClick, disabled, playable, dimmed }: { card: number; label?: string; onClick?: () => void; disabled?: boolean; playable?: boolean; dimmed?: boolean }) {
  const info = decodeCard(card);
  const style = { "--tru-suit": SUIT_CSS[info.suit] } as CSSProperties;
  const classes = ["tru-card", playable ? "tru-card--playable" : "", dimmed ? "tru-card--dim" : ""].filter(Boolean).join(" ");
  const face = (
    <>
      <span className="tru-card__corner" aria-hidden="true">{info.number}</span>
      <span className="tru-card__symbol" aria-hidden="true">{info.symbol}</span>
      <span className="tru-card__number" aria-hidden="true">{info.number}</span>
    </>
  );
  if (onClick) {
    return (
      <button type="button" className={classes} style={style} aria-label={label ?? info.label} disabled={disabled} onClick={onClick}>
        {face}
      </button>
    );
  }
  return (
    <span className={classes} style={style} role="img" aria-label={label ?? info.label}>
      {face}
    </span>
  );
}

const secondsUntil = (iso: string | null, serverNowMs: number): number => (iso === null ? 0 : Math.max(0, Math.ceil((Date.parse(iso) - serverNowMs) / 1000)));

type Verification = { ok: boolean } | null;
type CallType = Exclude<TrucoActionType, "play">;
const GROUPS: ActionGroup[] = ["envido", "truco", "response"];
/** El servidor ordena las jugadas alfabeticamente: en pantalla van de menor a mayor canto y "Quiero" antes que "No quiero". */
const ORDER: readonly CallType[] = ["envido", "real_envido", "falta_envido", "truco", "retruco", "vale4", "quiero", "no_quiero", "mazo"];

function TrucoTable({ table, onLeave }: { table: LiveTable<TrucoView>; onLeave: () => void }) {
  const queryClient = useQueryClient();
  const act = useTableAction<TrucoAction>(GAME_ID, table.id);
  const game = table.game;
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [sawPlaying, setSawPlaying] = useState(false);
  const [winClosed, setWinClosed] = useState(false);
  const [verification, setVerification] = useState<Verification>(null);

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(id);
  }, []);

  const finished = table.status === "Finished";
  const mySeat = table.mySeat;
  const winner = game?.winner ?? -1;
  const payout = mySeat !== null ? table.payouts?.[mySeat] ?? null : null;
  const net = payout === null ? null : payout - table.buyIn;
  const iWon = finished && mySeat !== null && winner === mySeat;

  // Se festeja solo una victoria que se vio resolverse en esta pantalla (no una vieja que quedo del estado al entrar).
  if (table.status === "Playing" && !sawPlaying) setSawPlaying(true);
  const celebrate = finished && sawPlaying && iWon && net !== null && !winClosed;
  const justFinished = finished && sawPlaying;
  useEffect(() => {
    if (justFinished) void queryClient.invalidateQueries({ queryKey: queryKeys.account });
  }, [justFinished, queryClient]);
  useEffect(() => {
    if (celebrate) playWinSound();
  }, [celebrate]);

  const nameOf = (seat: number): string => table.seats.find((s) => s.seat === seat)?.name ?? `Asiento ${seat + 1}`;
  const serverNow = now + table.offsetMs;

  if (!game) return <p className="muted" role="status">Repartiendo las cartas…</p>;

  const me = mySeat ?? 0;
  const rival = 1 - me;
  const playing = table.status === "Playing";
  const myTurn = playing && mySeat !== null && game.current === mySeat;
  const turnSeconds = secondsUntil(table.turnEndsAt, serverNow);
  const canPlayCard = myTurn && game.actions.includes("play");
  const hand = [...game.hand].sort(byStrength);
  const callActions = game.actions.filter((a): a is CallType => a !== "play").sort((a, b) => ORDER.indexOf(a) - ORDER.indexOf(b));
  const rivalSeat = table.seats.find((s) => s.seat === rival);

  const send = (action: TrucoAction) => {
    setError(null);
    act.mutate(action, { onError: (e) => setError(detailedErrorMessage(e)) });
  };

  // Las cartas de la mesa, agrupadas por baza (de a dos jugadas) con el resultado de cada una.
  const bazaGroups = Array.from({ length: Math.ceil(game.table.length / 2) }, (_, i) => ({ plays: game.table.slice(i * 2, i * 2 + 2), result: game.bazas[i] }));
  const bazaResult = (result: number | undefined): string =>
    result === undefined ? "En juego" : result === -1 ? "Parda" : mySeat === null ? `Ganó ${nameOf(result)}` : result === mySeat ? "Ganaste la baza" : "Perdiste la baza";

  const pendingByRival = game.pending !== null && game.pending.caller !== mySeat;
  const worth = trucoWorth(game.trucoLevel);

  const statusLabel = finished ? (winner < 0 ? "La partida terminó sin ganador" : winner === mySeat ? "¡Ganaste!" : `Ganó ${nameOf(winner)}`)
    : mySeat === null ? `Mirando la partida: juega ${nameOf(game.current)}`
    : myTurn ? (game.pending !== null ? `Te cantaron: respondé (${turnSeconds} s)` : `Es tu turno: jugá una carta o cantá (${turnSeconds} s)`)
    : `Juega ${nameOf(game.current)}${table.seats.find((s) => s.seat === game.current)?.away ? " (ausente: lo juega un bot)" : ""}`;

  const proof = async () => {
    if (table.serverSeed === null) return;
    setVerification({ ok: (await sha256Hex(table.serverSeed)) === table.commitment.toLowerCase() });
  };

  return (
    <div className="stack">
      <section className="card" aria-labelledby="tru-mesa">
        <div className="tru-head">
          <h2 id="tru-mesa" className="section-title">{table.name}</h2>
          <p className="muted">Entrada de {formatChips(table.buyIn)} fichas · en juego {formatChips(table.buyIn * table.seats.length)}</p>
        </div>
        <p role="status" className="crash-status" data-testid="tru-status">{statusLabel}</p>

        <div className="tru-felt">
          <div className="tru-score" data-testid="tru-score" role="group" aria-label={`Marcador, a ${game.target} puntos`}>
            <p className={`tru-score__side ${playing && game.current === me ? "tru-score__side--active" : ""}`}>
              <span className="tru-score__name">{mySeat === null ? nameOf(me) : "Vos"}{game.mano === me ? " · mano" : ""}</span>
              <strong className="tru-score__points" data-testid="tru-score-me">{game.scores[me] ?? 0}</strong>
            </p>
            <p className="muted tru-score__target">a {game.target}</p>
            <p className={`tru-score__side ${playing && game.current === rival ? "tru-score__side--active" : ""}`}>
              <span className="tru-score__name">{nameOf(rival)}{game.mano === rival ? " · mano" : ""}</span>
              <strong className="tru-score__points" data-testid="tru-score-rival">{game.scores[rival] ?? 0}</strong>
            </p>
          </div>
          <p className="tru-info" data-testid="tru-info">
            Ronda {game.handNo} · {game.trucoLevel > 0 ? `${trucoLevelName(game.trucoLevel)} aceptado` : "sin truco"} · vale {worth} {worth === 1 ? "punto" : "puntos"}
          </p>

          <div className="tru-opp" data-testid="tru-opp" aria-current={playing && game.current === rival ? "true" : undefined}>
            <p className="bj-who">
              {nameOf(rival)}
              {playing && game.current === rival ? ` · turno (${turnSeconds} s)` : ""}
              {rivalSeat?.isBot ? " · Bot" : ""}
              {rivalSeat?.away && !rivalSeat.isBot ? " · ausente: lo juega un bot" : ""}
            </p>
            <div className="tru-hand" role="img" aria-label={`${nameOf(rival)} tiene ${game.opponentCards} ${game.opponentCards === 1 ? "carta" : "cartas"}`}>
              {Array.from({ length: game.opponentCards }, (_, i) => (
                <span key={i} className="tru-card tru-card--back" aria-hidden="true" />
              ))}
              {game.opponentCards === 0 && <span className="tru-none">Sin cartas</span>}
            </div>
          </div>

          <div className="tru-table" data-testid="tru-table" role="group" aria-label="Cartas jugadas en esta ronda">
            {bazaGroups.map((group, i) => (
              <div key={i} className="tru-baza" data-testid="tru-baza">
                <p className="tru-baza__title">Baza {i + 1}</p>
                <div className="tru-baza__cards">
                  {group.plays.map((play) => (
                    <div key={`${play.seat}-${play.card}`} className="tru-played">
                      <TrucoCard card={play.card} label={`${play.seat === mySeat ? "Jugaste" : `${nameOf(play.seat)} jugó`} ${decodeCard(play.card).label}`} />
                      <span className="tru-played__who">{play.seat === mySeat ? "Vos" : nameOf(play.seat)}</span>
                    </div>
                  ))}
                </div>
                <p className={`tru-baza__result ${group.result === undefined ? "" : group.result === -1 ? "tru-baza__result--tie" : group.result === mySeat ? "tru-baza__result--won" : "tru-baza__result--lost"}`}>{bazaResult(group.result)}</p>
              </div>
            ))}
            {bazaGroups.length === 0 && <p className="tru-none">Todavía no se jugó ninguna carta.</p>}
          </div>

          {playing && game.pending !== null && (
            <p role="status" className="tru-pending" data-testid="tru-pending">
              {pendingText(game.pending, nameOf)}
              {pendingByRival ? "" : " Esperando la respuesta."}
            </p>
          )}

          {mySeat !== null && (
            <div className="tru-mine dock">
              <p className="bj-who">
                Tu mano ({hand.length} {hand.length === 1 ? "carta" : "cartas"})
                {myTurn ? ` · tu turno (${turnSeconds} s)` : ""}
              </p>
              <div className="tru-hand" data-testid="tru-hand" role="group" aria-label="Tu mano">
                {hand.map((card) => (
                  <TrucoCard
                    key={card}
                    card={card}
                    playable={canPlayCard}
                    dimmed={!canPlayCard && playing}
                    disabled={!canPlayCard || act.isPending}
                    label={canPlayCard ? `Jugar ${decodeCard(card).label}` : decodeCard(card).label}
                    onClick={() => send(playBody(card))}
                  />
                ))}
                {hand.length === 0 && <span className="tru-none">Sin cartas</span>}
              </div>
              {game.envidoPoints !== null && playing && (
                <p className="tru-envido" data-testid="tru-envido">Tus puntos de envido: <strong>{game.envidoPoints}</strong> <span className="tru-none">(solo los ves vos)</span></p>
              )}

              {playing && myTurn && callActions.length > 0 && (
                <div className="tru-calls" role="group" aria-label="Cantos">
                  {GROUPS.map((group) => {
                    const items = callActions.filter((a) => groupOf(a) === group);
                    if (items.length === 0) return null;
                    return (
                      <div key={group} className="tru-calls__group" role="group" aria-label={GROUP_LABELS[group]}>
                        <span className="tru-calls__title">{GROUP_LABELS[group]}</span>
                        {items.map((type) => (
                          <button key={type} type="button" className={`btn ${type === "no_quiero" ? "btn--ghost" : "btn--gold"}`} disabled={act.isPending} onClick={() => send({ type })}>
                            {actionLabel(type, game.pending)}
                          </button>
                        ))}
                      </div>
                    );
                  })}
                  {callActions.includes("mazo") && (
                    <div className="tru-calls__group tru-calls__group--mazo" role="group" aria-label={GROUP_LABELS.mazo}>
                      <button type="button" className="btn btn--ghost" disabled={act.isPending} onClick={() => send({ type: "mazo" })}>{actionLabel("mazo", game.pending)}</button>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
          {error && <p className="notice notice--error" role="alert">{error}</p>}
        </div>

        {finished && (
          <div role="status" className="tru-result" data-testid="tru-result">
            <p><strong>{winner < 0 ? "Nadie ganó: se devolvieron las entradas." : iWon ? `¡Ganaste! ${net !== null && net >= 0 ? "+" : ""}${formatChips(net ?? 0)} fichas` : `Ganó ${nameOf(winner)}.`}</strong></p>
            {payout !== null && (
              <p className="muted">
                Pusiste {formatChips(table.buyIn)} y cobraste {formatChips(payout)}: {net! >= 0 ? "ganancia" : "pérdida"} neta de {formatChips(Math.abs(net!))} fichas.
              </p>
            )}
            <button type="button" className="btn btn--gold" onClick={onLeave}>Volver a las mesas</button>
          </div>
        )}
      </section>

      <section className="card" aria-labelledby="tru-hist">
        <h2 id="tru-hist" className="section-title">Lo último que pasó</h2>
        <ol className="tru-events" role="log" aria-label="Historial de la partida">
          {[...game.events].reverse().slice(0, 12).map((event, i) => (
            <li key={`${game.events.length - i}-${event.kind}-${event.seat}`}>{eventText(event, nameOf)}</li>
          ))}
          {game.events.length === 0 && <li className="muted">Todavía no jugó nadie.</li>}
        </ol>
      </section>

      <section className="card" aria-labelledby="tru-verificar">
        <h2 id="tru-verificar" className="section-title">Verificá la partida</h2>
        <p className="muted">Antes de repartir el servidor publicó el compromiso (el hash de su semilla). Al terminar la partida revela la semilla: si su hash coincide con el compromiso, el mazo no se cambió durante el juego.</p>
        <dl className="crash-proof">
          <dt>Compromiso</dt>
          <dd className="mono">{table.commitment}</dd>
          {table.serverSeed !== null && (
            <>
              <dt>Semilla revelada</dt>
              <dd className="mono">{table.serverSeed}</dd>
            </>
          )}
        </dl>
        {table.serverSeed !== null ? (
          <>
            <button type="button" className="btn btn--ghost" onClick={() => void proof()}>Verificar en mi navegador</button>
            {verification && (
              <p role="status" className={verification.ok ? "notice" : "notice notice--error"}>
                {verification.ok ? "✓ La semilla coincide con el compromiso." : "✗ La semilla NO coincide con el compromiso."}
              </p>
            )}
          </>
        ) : (
          <p className="muted">La semilla se revela cuando termina la partida.</p>
        )}
      </section>

      {celebrate && (
        <div className="win-modal" role="dialog" aria-modal="true" aria-labelledby="win-title" onClick={() => setWinClosed(true)}>
          <Confetti />
          <div className="win-modal__box" onClick={(e) => e.stopPropagation()}>
            <p className="crash-multiplier crash-multiplier--running" aria-hidden="true">TRUCO</p>
            <h2 id="win-title" className="win-modal__title">¡Ganaste!</h2>
            <p className="win-modal__amount">+{formatChips(net)} fichas</p>
            <p className="muted">Pusiste {formatChips(table.buyIn)} y cobraste {formatChips(payout ?? 0)}.</p>
            <button type="button" className="btn btn--gold" autoFocus onClick={() => setWinClosed(true)}>
              Continuar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
