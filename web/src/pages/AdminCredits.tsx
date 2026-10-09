import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { BACKOFFICE_ROLE, useAdminCredits, useAdminUsers, useMe } from "../api/hooks";
import type { CreditFilter } from "../api/types";
import { formatChips, formatDateTime } from "../lib/format";
import { errorMessage } from "../lib/messages";
import { endOfLocalDayIso, startOfLocalDayIso } from "../lib/movements";

const shortId = (id: string) => `${id.slice(0, 8)}…${id.slice(-4)}`;

/** Historial general de cargas de fichas: quien cargo, a quien, cuanto y cuando, con filtros y totales. Solo para `backoffice`. */
export function AdminCredits() {
  const me = useMe();
  const isAdmin = me.data?.roles.includes(BACKOFFICE_ROLE) ?? false;
  const users = useAdminUsers(isAdmin);
  const [userId, setUserId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const fromIso = from ? startOfLocalDayIso(from) : null;
  const toIso = to ? endOfLocalDayIso(to) : null;
  const backwards = fromIso !== null && toIso !== null && fromIso >= toIso;
  const filter = useMemo<CreditFilter>(() => ({ userId: userId || null, from: fromIso, to: toIso }), [userId, fromIso, toIso]);
  const credits = useAdminCredits(filter, isAdmin && !backwards);

  if (me.isLoading) return <p className="muted" role="status">Cargando…</p>;
  if (!isAdmin) return <p className="notice notice--error" role="alert">No tenés permisos de administrador.</p>;

  const items = credits.data?.pages.flatMap((page) => page.items) ?? [];
  const summary = credits.data?.pages[0];

  return (
    <div className="stack">
      <section className="card" aria-labelledby="cargas-titulo">
        <p className="eyebrow">Administración</p>
        <h1 id="cargas-titulo" className="display">Historial de cargas</h1>
        <p className="muted">Todas las cargas de fichas. Solo se agrega: no se edita ni se borra. <Link to="/admin">Volver al panel</Link></p>
      </section>

      <section className="card" aria-labelledby="cargas-filtros">
        <h2 id="cargas-filtros" className="section-title">Filtros</h2>
        <div className="filters">
          <label className="field">
            <span>Jugador</span>
            <select value={userId} onChange={(e) => setUserId(e.target.value)}>
              <option value="">Todos</option>
              {users.data?.map((u) => (
                <option key={u.userId} value={u.userId}>{shortId(u.userId)}{u.userId === me.data?.userId ? " (vos)" : ""}</option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Desde</span>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="field">
            <span>Hasta</span>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
          <button type="button" className="btn btn--ghost" disabled={!userId && !from && !to} onClick={() => { setUserId(""); setFrom(""); setTo(""); }}>
            Limpiar filtros
          </button>
        </div>
        {backwards && <p className="notice notice--error" role="alert">La fecha "desde" tiene que ser anterior a "hasta".</p>}
      </section>

      <section className="card" aria-labelledby="cargas-lista">
        <h2 id="cargas-lista" className="section-title">Cargas</h2>
        {credits.isLoading && !backwards && <p className="muted" role="status">Cargando…</p>}
        {credits.isError && <p className="notice notice--error" role="alert">{errorMessage(credits.error)}</p>}
        {summary && (
          <p className="credit-total" data-testid="credit-total">
            <strong>{formatChips(summary.count)}</strong> {summary.count === 1 ? "carga" : "cargas"} · <strong>{formatChips(summary.totalAmount)}</strong> fichas en total
          </p>
        )}
        {summary && summary.count === 0 && <p className="muted">No hay cargas con ese filtro.</p>}

        {items.length > 0 && (
          <div className="admin-audit-scroll">
            <table className="admin-audit" aria-label="Cargas de fichas">
              <thead>
                <tr><th>Cuándo</th><th>Administrador</th><th>Jugador</th><th className="num">Fichas</th></tr>
              </thead>
              <tbody>
                {items.map((e) => (
                  <tr key={e.transactionId}>
                    <td>{formatDateTime(e.occurredAt)}</td>
                    <td className="mono">{shortId(e.actorUserId)}{e.actorUserId === me.data?.userId && " (vos)"}</td>
                    <td className="mono">{shortId(e.targetUserId)}</td>
                    <td className="num">+{formatChips(e.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {credits.hasNextPage && (
          <button type="button" className="btn btn--ghost" disabled={credits.isFetchingNextPage} onClick={() => void credits.fetchNextPage()}>
            {credits.isFetchingNextPage ? "Cargando…" : "Ver cargas anteriores"}
          </button>
        )}
      </section>
    </div>
  );
}
