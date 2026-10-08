import { Layout } from "../components/Layout";
import { Landing } from "../pages/Landing";
import { RealtimeProvider } from "../realtime/RealtimeProvider";
import { useAuth } from "./AuthContext";

/** Puerta de las paginas privadas: sin sesion se muestra la pantalla de entrada; con sesion, el saldo en vivo y el menu. */
export function RequireAuth() {
  const { status } = useAuth();

  if (status === "loading") {
    return (
      <main className="landing">
        <p className="splash" role="status">Cargando…</p>
      </main>
    );
  }

  if (status === "anonymous") return <Landing />;

  return (
    <RealtimeProvider>
      <Layout />
    </RealtimeProvider>
  );
}

