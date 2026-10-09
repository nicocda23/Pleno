import { screen, within } from "@testing-library/react";
import type { GameInfo } from "../api/types";
import { fakeApi, renderApp } from "../test/harness";
import { Lobby } from "./Lobby";

const account = { accountId: "a1", userId: "u1", available: 1_000, reserved: 0, version: 1, openReservations: {} };
const me = { userId: "u1", displayName: "nicocda", roles: ["player"], accountId: "a1", registeredAt: "2026-10-01T10:00:00Z" };

const roulette: GameInfo = { id: "roulette", name: "Ruleta europea", tagline: "37 casilleros.", route: "/ruleta", glyph: "◎", resolution: "Server" };
const slots: GameInfo = { id: "slots", name: "Tragamonedas", tagline: "3 rodillos.", route: "/tragamonedas", glyph: "♣", resolution: "Server" };

const apiWith = (catalog: () => GameInfo[]) =>
  fakeApi({
    "GET /me": () => me,
    "GET /wallet/me": () => account,
    "GET /games": catalog,
    "GET /games/roulette/rounds": () => [],
  });

const section = () => within(screen.getByRole("region", { name: "Juegos" }));

describe("Lobby catalog", () => {
  it("builds the cards from the server catalog and links each one to its page", async () => {
    renderApp(<Lobby />, { api: apiWith(() => [roulette, slots]) });

    const link = await section().findByRole("link", { name: /Ruleta europea/ });
    expect(link).toHaveAttribute("href", "/ruleta");
    expect(section().getByRole("link", { name: /Tragamonedas/ })).toHaveAttribute("href", "/tragamonedas");
  });

  it("does not show a game the server no longer has enabled (removing a game does not touch the front)", async () => {
    renderApp(<Lobby />, { api: apiWith(() => [roulette]) });

    await section().findByRole("link", { name: /Ruleta europea/ });
    expect(section().queryByText("Tragamonedas")).not.toBeInTheDocument();
  });

  it("shows a game the server has but this front cannot play yet as coming soon, not as a broken link", async () => {
    const crash: GameInfo = { id: "crash", name: "Crash", tagline: "Un cohete.", route: "/crash", glyph: "▲", resolution: "Server" };
    renderApp(<Lobby />, { api: apiWith(() => [roulette, crash]) });

    await section().findByRole("link", { name: /Ruleta europea/ });
    expect(section().queryByRole("link", { name: /Crash/ })).not.toBeInTheDocument();
    expect(section().getAllByText("Crash")).toHaveLength(1); // una sola tarjeta, aunque tambien este en el roadmap
  });

  it("keeps the roadmap games as coming soon while they do not exist", async () => {
    renderApp(<Lobby />, { api: apiWith(() => [roulette, slots]) });

    await section().findByRole("link", { name: /Ruleta europea/ });
    expect(section().getByText("Blackjack")).toBeInTheDocument();
    expect(section().getByText("Poker")).toBeInTheDocument();
    expect(section().queryByRole("link", { name: /Blackjack/ })).not.toBeInTheDocument();
    expect(section().getAllByText("Próximamente").length).toBeGreaterThanOrEqual(3);
  });

  it("tells the player when the catalog cannot be loaded", async () => {
    const api = fakeApi({ "GET /me": () => me, "GET /wallet/me": () => account, "GET /games/roulette/rounds": () => [] });
    renderApp(<Lobby />, { api });

    expect(await screen.findByText("No pudimos cargar los juegos.")).toBeInTheDocument();
  });
});
