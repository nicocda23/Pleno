import { useAuth } from "../auth/AuthContext";

/** Pantalla para quien todavia no inicio sesion. */
export function Landing() {
  const { login } = useAuth();

  return (
    <main className="landing">
      <div className="landing__card">
        <span className="brand__chip brand__chip--lg" aria-hidden="true">P</span>
        <h1 className="landing__title">Pleno</h1>
        <p className="landing__lead">El casino de fichas sin riesgo: jugá, comprobá cada resultado y mirá tu saldo moverse en vivo.</p>

        <ul className="landing__points">
          <li><strong>Sin dinero real.</strong> Las fichas son ficticias.</li>
          <li><strong>Resultados verificables.</strong> Podés recalcular cada tirada vos mismo.</li>
          <li><strong>Saldo exacto.</strong> Cada ficha queda registrada y se puede auditar.</li>
        </ul>

        <button type="button" className="btn btn--gold btn--lg" onClick={() => void login("/")}>
          Entrar o crear cuenta
        </button>
        <p className="landing__fine">Iniciar sesión te lleva a nuestro servidor de identidad (Keycloak). Tu contraseña nunca pasa por esta aplicación.</p>
      </div>
    </main>
  );
}
