import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "../api/client";
import type { PlacedBet } from "../api/types";
import { fakeApi, renderApp, type FakeApi } from "../test/harness";
import { Roulette } from "./Roulette";

const account = (available: number) => ({ accountId: "a1", userId: "u1", available, reserved: 0, version: 2, openReservations: {} });
const placed = (betId: string): PlacedBet => ({ betId, status: "Placed", nonce: 0, commitment: "c".repeat(64), clientSeed: "abc", alreadyPlaced: false });

function apiWith(extra: Record<string, (body?: unknown, headers?: Record<string, string>) => unknown> = {}, available = 1000): FakeApi {
  return fakeApi({
    "GET /wallet/me": () => account(available),
    "GET /games/roulette/rounds": () => [],
    "POST /games/roulette/bets": () => placed("bet-1"),
    ...extra,
  });
}

const betCalls = (api: FakeApi) => api.calls.filter((c) => c.method === "POST" && c.path === "/games/roulette/bets");

/** Simula la preferencia del sistema "reducir movimiento": la rueda se asienta al instante y las pruebas no esperan la animacion. */
function reduceMotion(reduce: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: reduce && query.includes("prefers-reduced-motion"),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    onchange: null,
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
}

const spot = (name: string) => screen.getByRole("button", { name });

/** Elige un lugar del tapete y espera a que el saldo este cargado (si no, el boton queda deshabilitado). */
async function bet(name: string) {
  await userEvent.click(spot(name));
  await waitFor(() => expect(screen.getByRole("button", { name: /Apostar \d/ })).toBeEnabled());
}

const closedNotice = (extra: Record<string, unknown>) => ({ betId: "bet-1", game: "Roulette", status: "Settled", winningNumber: 17, stake: 50, payout: 100, failureReason: null, ...extra });

describe("Roulette table", () => {
  beforeEach(() => reduceMotion(true));

  it("places the bet chosen on the board with an idempotency key and shows the result from the live notice", async () => {
    const api = apiWith();
    const { hub } = renderApp(<Roulette />, { api });

    await userEvent.click(screen.getByRole("radio", { name: "50" }));
    await bet("Negro");
    await userEvent.click(screen.getByRole("button", { name: /Apostar 50 fichas/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(1));
    const call = betCalls(api)[0]!;
    expect(call.body).toEqual({ betType: "Black", selection: [], stake: 50 });
    expect(call.headers?.["Idempotency-Key"]).toMatch(/^[0-9a-f-]{36}$/);
    expect(await screen.findByText(/La ruleta está girando/)).toBeInTheDocument();

    act(() => hub.emit("roundClosed", closedNotice({})));

    expect(await screen.findByText("Ganaste 100 fichas")).toBeInTheDocument();
    expect(screen.getAllByText(/Salió el 17/).length).toBeGreaterThanOrEqual(1); // aviso emergente y panel de resultado
    expect(screen.queryByText(/La ruleta está girando/)).not.toBeInTheDocument();
    expect(screen.getByTestId("wheel")).toHaveAttribute("data-state", "settled");
  });

  it("sends every kind of bet the board offers exactly as the API expects", async () => {
    const api = apiWith();
    renderApp(<Roulette />, { api });

    await bet("Caballo 17, 20");
    await userEvent.click(screen.getByRole("button", { name: /Apostar 10 fichas/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(1));
    expect(betCalls(api)[0]!.body).toEqual({ betType: "Split", selection: [17, 20], stake: 10 });
  });

  it("adds chips when the same place is tapped again, and a different place starts over", async () => {
    renderApp(<Roulette />, { api: apiWith() });

    await bet("Pleno 7");
    await userEvent.click(spot("Pleno 7"));
    expect(screen.getByRole("button", { name: "Apostar 20 fichas" })).toBeInTheDocument();
    expect(screen.getByText(/cobrás/)).toHaveTextContent("720"); // 20 x 36

    await userEvent.click(spot("Pleno 8"));
    expect(screen.getByRole("button", { name: "Apostar 10 fichas" })).toBeInTheDocument();
    expect(spot("Pleno 8")).toHaveAttribute("aria-pressed", "true");
    expect(spot("Pleno 7")).toHaveAttribute("aria-pressed", "false");
  });

  it("clears the chips from the board", async () => {
    renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 7");

    await userEvent.click(screen.getByRole("button", { name: "Quitar fichas" }));

    expect(screen.getByRole("button", { name: "Apostar" })).toBeDisabled();
    expect(spot("Pleno 7")).toHaveAttribute("aria-pressed", "false");
  });

  it("locks the board while the wheel is spinning", async () => {
    renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 7");

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    expect(spot("Pleno 8")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Quitar fichas" })).toBeDisabled();
  });

  it("shows a loss without celebrating", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 7");
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", closedNotice({ winningNumber: 2, payout: 0, stake: 10 })));

    expect(await screen.findByText("No hubo suerte esta vez")).toBeInTheDocument();
  });

  it("explains a rejected bet in plain language and stops the wheel", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 7");
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", closedNotice({ status: "Rejected", winningNumber: null, payout: 0, failureReason: "InsufficientFunds" })));

    expect(await screen.findByText("Apuesta rechazada")).toBeInTheDocument();
    expect(screen.getAllByText(/No te alcanzan las fichas/).length).toBeGreaterThan(0);
    await waitFor(() => expect(screen.getByTestId("wheel")).toHaveAttribute("data-state", "idle"));
  });

  it("says the stake came back when a round is voided", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 7");
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", closedNotice({ status: "Voided", winningNumber: null, payout: 0, failureReason: "ReservationExpired" })));

    expect(await screen.findByText("Ronda anulada")).toBeInTheDocument();
  });

  it("ignores notices of other bets", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 7");
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", closedNotice({ betId: "otra-apuesta", winningNumber: 5, payout: 20 })));

    expect(screen.getByText(/La ruleta está girando/)).toBeInTheDocument();
  });

  it("will not let the player bet more than they have", async () => {
    renderApp(<Roulette />, { api: apiWith({}, 30) });

    await userEvent.click(screen.getByRole("radio", { name: "100" }));
    await userEvent.click(spot("Pleno 7"));

    expect(screen.getByRole("button", { name: /Apostar 100 fichas/ })).toBeDisabled();
    expect(await screen.findByText(/No te alcanzan las fichas/)).toBeInTheDocument();
  });

  it("reuses the same idempotency key when the player retries after a network failure, so the server never charges twice", async () => {
    let attempt = 0;
    const api = apiWith({
      "POST /games/roulette/bets": () => {
        attempt += 1;
        if (attempt === 1) throw new ApiError(0, "NetworkError", "sin red");
        return placed("bet-1");
      },
    });
    renderApp(<Roulette />, { api });
    await bet("Pleno 7");

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    expect(await screen.findByText(/No hay conexión con el servidor/)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("wheel")).toHaveAttribute("data-state", "idle")); // la rueda no queda girando en falso
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(2));
    const [first, second] = betCalls(api);
    expect(second!.headers?.["Idempotency-Key"]).toBe(first!.headers?.["Idempotency-Key"]);
  });

  it("uses a fresh key for the next bet once the previous one was accepted", async () => {
    const api = apiWith();
    const { hub } = renderApp(<Roulette />, { api });
    await bet("Pleno 7");

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);
    act(() => hub.emit("roundClosed", closedNotice({ winningNumber: 1, stake: 10, payout: 20 })));
    await screen.findByText("Ganaste 20 fichas");
    await waitFor(() => expect(screen.getByRole("button", { name: /Apostar/ })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(2));
    const [first, second] = betCalls(api);
    expect(second!.headers?.["Idempotency-Key"]).not.toBe(first!.headers?.["Idempotency-Key"]);
  });

  it("shows a definitive server rejection and does not reuse its key", async () => {
    const api = apiWith({ "POST /games/roulette/bets": () => { throw new ApiError(400, "InvalidBet", "x"); } });
    renderApp(<Roulette />, { api });
    await bet("Pleno 7");

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    expect(await screen.findByText(/Esa apuesta no es válida/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(2));
    const [first, second] = betCalls(api);
    expect(second!.headers?.["Idempotency-Key"]).not.toBe(first!.headers?.["Idempotency-Key"]);
  });

  it("falls back to polling the round when the live notice never arrives", async () => {
    const api = apiWith({
      "GET /games/roulette/rounds/bet-1": () => ({
        betId: "bet-1", status: "Settled", betType: "Red", selection: [], stake: 10, pairId: "p", nonce: 0,
        winningNumber: 3, payout: 20, failureReason: null, placedAt: "2026-10-08T12:00:00Z",
      }),
    });
    renderApp(<Roulette />, { api });
    await bet("Rojo");

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    expect(await screen.findByText("Ganaste 20 fichas", {}, { timeout: 5_000 })).toBeInTheDocument();
  }, 10_000);

  it("lands the ball as soon as the number is drawn, but waits for the payout before revealing the result", async () => {
    const api = apiWith({
      "GET /games/roulette/rounds/bet-1": () => ({
        betId: "bet-1", status: "Resolved", betType: "Red", selection: [], stake: 10, pairId: "p", nonce: 0,
        winningNumber: 3, payout: null, failureReason: null, placedAt: "2026-10-08T12:00:00Z",
      }),
    });
    const { hub } = renderApp(<Roulette />, { api });
    await bet("Rojo");

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    await waitFor(() => expect(screen.getByTestId("wheel")).toHaveAttribute("data-state", "settled"));
    expect(screen.queryByText(/Ganaste|No hubo suerte/)).not.toBeInTheDocument(); // todavia falta cobrar

    act(() => hub.emit("roundClosed", closedNotice({ winningNumber: 3, stake: 10, payout: 20 })));

    expect(await screen.findByText("Ganaste 20 fichas")).toBeInTheDocument();
  });
});

describe("Roulette animation", () => {
  beforeEach(() => reduceMotion(false));

  it("does not reveal the result until the ball has settled", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await bet("Pleno 17");
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    // El servidor ya decidio, pero la bola todavia esta girando: no se adelanta nada.
    act(() => hub.emit("roundClosed", closedNotice({ winningNumber: 17, stake: 10, payout: 360 })));
    await act(() => new Promise((resolve) => setTimeout(resolve, 600)));

    expect(screen.queryByText(/Ganaste/)).not.toBeInTheDocument();
    expect(screen.getByText(/La ruleta está girando/)).toBeInTheDocument();
    expect(["spinning", "landing"]).toContain(screen.getByTestId("wheel").getAttribute("data-state"));

    // Cuando se asienta, recien ahi aparece.
    expect(await screen.findByText("Ganaste 360 fichas", {}, { timeout: 8_000 })).toBeInTheDocument();
    expect(screen.getByTestId("wheel")).toHaveAttribute("data-state", "settled");
  }, 15_000);
});
