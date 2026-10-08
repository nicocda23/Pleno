import { Route, Routes } from "react-router-dom";
import { ApiProvider } from "./api/ApiProvider";
import { RequireAuth } from "./auth/RequireAuth";
import { AuthCallback } from "./pages/AuthCallback";
import { History } from "./pages/History";
import { Lobby } from "./pages/Lobby";
import { NotFound } from "./pages/NotFound";
import { Roulette } from "./pages/Roulette";

/** Rutas. Los providers de sesion y tema viven en `main.tsx`; los datos del servidor, aca. */
export function App() {
  return (
    <ApiProvider>
      <Routes>
        <Route path="/auth/callback" element={<AuthCallback />} />
        <Route element={<RequireAuth />}>
          <Route index element={<Lobby />} />
          <Route path="ruleta" element={<Roulette />} />
          <Route path="historial" element={<History />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Routes>
    </ApiProvider>
  );
}
