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

async function ready() {
  await waitFor(() => expect(screen.getByRole("button", { name: /Apostar/ })).toBeEnabled());
}

describe("Roulette table", () => {
  it("places the chosen bet with an idempotency key and shows the result from the live notice", async () => {
    const api = apiWith();
    const { hub } = renderApp(<Roulette />, { api });
    await ready();

    await userEvent.click(screen.getByRole("radio", { name: /Negro/ }));
    await userEvent.click(screen.getByRole("radio", { name: "50" }));
    await userEvent.click(screen.getByRole("button", { name: /Apostar 50 fichas/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(1));
    const call = betCalls(api)[0]!;
    expect(call.body).toEqual({ betType: "Black", selection: [], stake: 50 });
    expect(call.headers?.["Idempotency-Key"]).toMatch(/^[0-9a-f-]{36}$/);
    expect(await screen.findByText(/La ruleta está girando/)).toBeInTheDocument();

    act(() => hub.emit("roundClosed", { betId: "bet-1", game: "Roulette", status: "Settled", winningNumber: 17, stake: 50, payout: 100, failureReason: null }));

    expect(await screen.findByText("Ganaste 100 fichas")).toBeInTheDocument();
    expect(screen.getAllByText(/Salió el 17/).length).toBeGreaterThanOrEqual(1); // aviso emergente y panel de resultado
    expect(screen.queryByText(/La ruleta está girando/)).not.toBeInTheDocument();
  });

  it("shows a loss without celebrating", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await ready();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", { betId: "bet-1", game: "Roulette", status: "Settled", winningNumber: 2, stake: 10, payout: 0, failureReason: null }));

    expect(await screen.findByText("No hubo suerte esta vez")).toBeInTheDocument();
  });

  it("explains a rejected bet in plain language", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await ready();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", { betId: "bet-1", game: "Roulette", status: "Rejected", winningNumber: null, stake: 10, payout: 0, failureReason: "InsufficientFunds" }));

    expect(await screen.findByText("Apuesta rechazada")).toBeInTheDocument();
    expect(screen.getAllByText(/No te alcanzan las fichas/).length).toBeGreaterThan(0);
  });

  it("says the stake came back when a round is voided", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await ready();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", { betId: "bet-1", game: "Roulette", status: "Voided", winningNumber: null, stake: 10, payout: 0, failureReason: "ReservationExpired" }));

    expect(await screen.findByText("Ronda anulada")).toBeInTheDocument();
  });

  it("ignores notices of other bets", async () => {
    const { hub } = renderApp(<Roulette />, { api: apiWith() });
    await ready();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);

    act(() => hub.emit("roundClosed", { betId: "otra-apuesta", game: "Roulette", status: "Settled", winningNumber: 5, stake: 10, payout: 20, failureReason: null }));

    expect(screen.getByText(/La ruleta está girando/)).toBeInTheDocument();
  });

  it("will not let the player bet more than they have", async () => {
    renderApp(<Roulette />, { api: apiWith({}, 30) });
    await waitFor(() => expect(screen.getByRole("button", { name: /Apostar/ })).toBeEnabled());

    await userEvent.click(screen.getByRole("radio", { name: "100" }));

    expect(screen.getByRole("button", { name: /Apostar 100 fichas/ })).toBeDisabled();
    expect(screen.getByText(/No te alcanzan las fichas/)).toBeInTheDocument();
  });

  it("validates the straight-up number (0 to 36)", async () => {
    const api = apiWith();
    renderApp(<Roulette />, { api });
    await ready();

    await userEvent.click(screen.getByRole("radio", { name: /Pleno/ }));
    const input = screen.getByLabelText(/Número/);
    await userEvent.clear(input);
    await userEvent.type(input, "40");
    expect(screen.getByRole("button", { name: /Apostar/ })).toBeDisabled();

    await userEvent.clear(input);
    await userEvent.type(input, "17");
    expect(screen.getByRole("button", { name: /Apostar/ })).toBeEnabled();

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await waitFor(() => expect(betCalls(api)).toHaveLength(1));
    expect(betCalls(api)[0]!.body).toEqual({ betType: "Straight", selection: [17], stake: 10 });
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
    await ready();

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    expect(await screen.findByText(/No hay conexión con el servidor/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(2));
    const [first, second] = betCalls(api);
    expect(second!.headers?.["Idempotency-Key"]).toBe(first!.headers?.["Idempotency-Key"]);
  });

  it("uses a fresh key for the next bet once the previous one was accepted", async () => {
    const api = apiWith();
    const { hub } = renderApp(<Roulette />, { api });
    await ready();

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));
    await screen.findByText(/La ruleta está girando/);
    act(() => hub.emit("roundClosed", { betId: "bet-1", game: "Roulette", status: "Settled", winningNumber: 1, stake: 10, payout: 20, failureReason: null }));
    await ready();
    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    await waitFor(() => expect(betCalls(api)).toHaveLength(2));
    const [first, second] = betCalls(api);
    expect(second!.headers?.["Idempotency-Key"]).not.toBe(first!.headers?.["Idempotency-Key"]);
  });

  it("shows a definitive server rejection and does not reuse its key", async () => {
    const api = apiWith({ "POST /games/roulette/bets": () => { throw new ApiError(400, "InvalidBet", "x"); } });
    renderApp(<Roulette />, { api });
    await ready();

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
    await ready();

    await userEvent.click(screen.getByRole("button", { name: /Apostar/ }));

    expect(await screen.findByText("Ganaste 20 fichas", {}, { timeout: 5_000 })).toBeInTheDocument();
  }, 10_000);
});
