import { Link } from "react-router-dom";
import { useAccount, useMe, useRounds } from "../api/hooks";
import { BalanceChip } from "../components/BalanceChip";
import { RoundBadge } from "../components/RoundBadge";
import { formatChips, formatDateTime } from "../lib/format";
import { describeRound } from "../lib/roulette";
import { outcomeText } from "../lib/messages";
import { useRealtime } from "../realtime/RealtimeProvider";

interface GameCard {
  id: string;
  name: string;
  tagline: string;
  to?: string;
  glyph: string;
}

const GAMES: GameCard[] = [
  { id: "roulette", name: "Ruleta europea", tagline: "37 casilleros, 15 tipos de apuesta. Cada tirada se puede verificar.", to: "/ruleta", glyph: "◎" },
  { id: "slots", name: "Tragamonedas", tagline: "Tabla de pagos configurable.", glyph: "♣" },
  { id: "blackjack", name: "Blackjack", tagline: "Mesas multijugador en tiempo real.", glyph: "♠" },
  { id: "poker", name: "Poker", tagline: "Torneos con tabla de posiciones.", glyph: "♦" },
];

export function Lobby() {
  const me = useMe();
  const account = useAccount();
  const rounds = useRounds(5);
  const { balance } = useRealtime();

  return (
    <div className="stack">
      <section className="hero card" aria-labelledby="saludo">
        <div>
          <p className="eyebrow">Bienvenido</p>
          <h1 id="saludo" className="display">Hola, {me.data?.displayName ?? "jugador"}</h1>
          {balance.reserved > 0 && <p className="muted">{formatChips(balance.reserved)} fichas en juego ahora mismo.</p>}
        </div>
        <BalanceChip size="xl" />
      </section>

      {account.isLoading && <p className="notice" role="status">Estamos preparando tu cuenta…</p>}
      {account.isError && (
        <p className="notice notice--error" role="alert">
          No pudimos abrir tu cuenta todavía.{" "}
          <button type="button" className="link-btn" onClick={() => void account.refetch()}>Reintentar</button>
        </p>
      )}

      <section aria-labelledby="juegos">
        <h2 id="juegos" className="section-title">Juegos</h2>
        <div className="games">
          {GAMES.map((game) =>
            game.to ? (
              <Link key={game.id} to={game.to} className="game game--open">
                <span className="game__glyph" aria-hidden="true">{game.glyph}</span>
                <span className="game__name">{game.name}</span>
                <span className="game__tag">{game.tagline}</span>
                <span className="game__cta">Jugar →</span>
              </Link>
            ) : (
              <div key={game.id} className="game game--soon" aria-disabled="true">
                <span className="game__glyph" aria-hidden="true">{game.glyph}</span>
                <span className="game__name">{game.name}</span>
                <span className="game__tag">{game.tagline}</span>
                <span className="game__cta">Próximamente</span>
              </div>
            ),
          )}
        </div>
      </section>

      <section aria-labelledby="ultimas" className="card">
        <div className="card__head">
          <h2 id="ultimas" className="section-title">Últimas jugadas</h2>
          <Link to="/historial" className="link-btn">Ver todo</Link>
        </div>
        {rounds.isLoading && <p className="muted">Cargando…</p>}
        {rounds.data?.length === 0 && <p className="muted">Todavía no jugaste. Probá la ruleta.</p>}
        <ul className="list">
          {rounds.data?.map((round) => (
            <li key={round.betId} className="list__row">
              <span>
                <strong>{describeRound(round)}</strong>
                <span className="muted"> · {formatChips(round.stake)} · {formatDateTime(round.placedAt)}</span>
              </span>
              <span className="list__end">
                <RoundBadge status={round.status} />
                <span>{outcomeText(round)}</span>
              </span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
