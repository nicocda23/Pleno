import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { useRealtime, type ConnectionState } from "../realtime/RealtimeProvider";
import { useTheme } from "../theme/ThemeProvider";
import { BalanceChip } from "./BalanceChip";

const CONNECTION_LABEL: Record<ConnectionState, string> = {
  connecting: "Conectando…",
  connected: "En vivo",
  reconnecting: "Reconectando…",
  disconnected: "Sin conexión en vivo",
};

export function Layout() {
  const { displayName, logout } = useAuth();
  const { connection } = useRealtime();
  const { theme, toggle } = useTheme();

  return (
    <div className="shell">
      <a className="skip-link" href="#contenido">
        Saltar al contenido
      </a>
      <header className="topbar">
        <NavLink to="/" className="brand" aria-label="Pleno, ir al lobby">
          <span className="brand__chip" aria-hidden="true">P</span>
          <span className="brand__name">Pleno</span>
        </NavLink>

        <nav className="topbar__nav" aria-label="Principal">
          <NavLink to="/" end>Lobby</NavLink>
          <NavLink to="/ruleta">Ruleta</NavLink>
          <NavLink to="/historial">Historial</NavLink>
        </nav>

        <div className="topbar__right">
          <span className={`live live--${connection}`} title={CONNECTION_LABEL[connection]} role="status">
            <span className="live__dot" aria-hidden="true" />
            <span className="live__text">{CONNECTION_LABEL[connection]}</span>
          </span>
          <BalanceChip />
          <button type="button" className="icon-btn" onClick={toggle} aria-label={theme === "dark" ? "Cambiar a tema claro" : "Cambiar a tema oscuro"}>
            {theme === "dark" ? "☀" : "☾"}
          </button>
          <div className="user">
            <span className="user__name">{displayName ?? "Jugador"}</span>
            <button type="button" className="link-btn" onClick={() => void logout()}>
              Salir
            </button>
          </div>
        </div>
      </header>

      <main id="contenido" className="content">
        <Outlet />
      </main>

      <footer className="footer">
        Pleno es un laboratorio: las fichas no tienen valor y no hay dinero real.
      </footer>
    </div>
  );
}
