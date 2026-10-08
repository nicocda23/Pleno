import { Link } from "react-router-dom";

export function NotFound() {
  return (
    <main className="landing">
      <div className="landing__card">
        <h1 className="landing__title">404</h1>
        <p className="landing__lead">Esa página no existe.</p>
        <Link to="/" className="btn btn--gold">Volver al lobby</Link>
      </div>
    </main>
  );
}
