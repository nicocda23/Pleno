import { lazy, Suspense } from "react";
import { Route, Routes } from "react-router-dom";
import { ApiProvider } from "./api/ApiProvider";
import { RequireAuth } from "./auth/RequireAuth";
import { Admin } from "./pages/Admin";
import { AuthCallback } from "./pages/AuthCallback";
import { History } from "./pages/History";
import { Lobby } from "./pages/Lobby";
import { NotFound } from "./pages/NotFound";

// PixiJS pesa bastante: la ruleta se descarga recien cuando el jugador entra a ella.
const Slots = lazy(() => import("./pages/Slots").then((m) => ({ default: m.Slots })));
const Roulette = lazy(() => import("./pages/Roulette").then((m) => ({ default: m.Roulette })));

/** Rutas. Los providers de sesion y tema viven en `main.tsx`; los datos del servidor, aca. */
export function App() {
  return (
    <ApiProvider>
      <Routes>
        <Route path="/auth/callback" element={<AuthCallback />} />
        <Route element={<RequireAuth />}>
          <Route index element={<Lobby />} />
          <Route path="ruleta" element={<Suspense fallback={<p className="muted" role="status">Cargando la ruleta…</p>}><Roulette /></Suspense>} />
          <Route path="tragamonedas" element={<Suspense fallback={<p className="muted" role="status">Cargando la tragamonedas…</p>}><Slots /></Suspense>} />
          <Route path="historial" element={<History />} />
          <Route path="admin" element={<Admin />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Routes>
    </ApiProvider>
  );
}
