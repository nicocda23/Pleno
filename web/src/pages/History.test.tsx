import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { FairnessInfo, Round } from "../api/types";
import { fakeApi, renderApp } from "../test/harness";
import { History } from "./History";

const PAIR_OLD = "11111111-1111-4111-8111-111111111111";
const PAIR_NEW = "22222222-2222-4222-8222-222222222222";

const round = (overrides: Partial<Round>): Round => ({
  betId: "b1", status: "Settled", betType: "Red", selection: [], stake: 10, pairId: PAIR_OLD, nonce: 0,
  winningNumber: 17, payout: 0, failureReason: null, placedAt: "2026-10-08T12:00:00Z", ...overrides,
});

const fairness = (overrides: Partial<FairnessInfo> = {}): FairnessInfo => ({
  userId: "u1",
  active: { pairId: PAIR_NEW, commitment: "n".repeat(64), clientSeed: "nueva", nextNonce: 0 },
  pendingBets: 0,
  retired: [],
  ...overrides,
});

const account = { accountId: "a1", userId: "u1", available: 1000, reserved: 0, version: 2, openReservations: {} };

describe("History", () => {
  it("lists the rounds with their outcome and state", async () => {
    const api = fakeApi({
      "GET /wallet/me": () => account,
      "GET /fairness/me": () => fairness(),
      "GET /games/roulette/rounds": () => [
        round({ betId: "b1", betType: "Straight", selection: [17], stake: 10, payout: 360 }),
        round({ betId: "b2", status: "Rejected", winningNumber: null, payout: null, failureReason: "InsufficientFunds", stake: 5000 }),
      ],
    });
    renderApp(<History />, { api });

    expect(await screen.findByText("Pleno 17")).toBeInTheDocument();
    expect(screen.getByText("Ganaste 360")).toBeInTheDocument();
    expect(screen.getByText("No se jugó")).toBeInTheDocument();
    expect(screen.getByText("Cobrada")).toBeInTheDocument();
    expect(screen.getByText("Rechazada")).toBeInTheDocument();
  });

  it("offers a verification link, with everything prefilled, only for rounds whose server seed was revealed", async () => {
    const api = fakeApi({
      "GET /wallet/me": () => account,
      "GET /fairness/me": () =>
        fairness({ retired: [{ pairId: PAIR_OLD, commitment: "c".repeat(64), serverSeed: "s".repeat(64), clientSeed: "mi-semilla", betsPlayed: 1 }] }),
      "GET /games/roulette/rounds": () => [
        round({ betId: "b1", pairId: PAIR_OLD, nonce: 7, winningNumber: 26 }),
        round({ betId: "b2", pairId: PAIR_NEW, nonce: 0, winningNumber: 5 }),
      ],
    });
    renderApp(<History />, { api });

    const links = await screen.findAllByRole("link", { name: "Verificar" });
    expect(links).toHaveLength(1); // solo la ronda del par ya revelado
    const url = new URL(links[0]!.getAttribute("href")!);
    expect(url.pathname).toBe("/verify/index.html");
    expect(url.searchParams.get("commitment")).toBe("c".repeat(64));
    expect(url.searchParams.get("serverSeed")).toBe("s".repeat(64));
    expect(url.searchParams.get("clientSeed")).toBe("mi-semilla");
    expect(url.searchParams.get("nonce")).toBe("7");
    expect(url.searchParams.get("claimed")).toBe("26");
  });

  it("never offers a link while the seed is still secret", async () => {
    const api = fakeApi({
      "GET /wallet/me": () => account,
      "GET /fairness/me": () => fairness(),
      "GET /games/roulette/rounds": () => [round({ pairId: PAIR_NEW })],
    });
    renderApp(<History />, { api });

    await screen.findByText("Cobrada");
    expect(screen.queryByRole("link", { name: "Verificar" })).not.toBeInTheDocument();
  });

  it("asks for confirmation before revealing the seed, then rotates", async () => {
    let revealed = false;
    const api = fakeApi({
      "GET /wallet/me": () => account,
      "GET /fairness/me": () =>
        revealed
          ? fairness({ retired: [{ pairId: PAIR_NEW, commitment: "c".repeat(64), serverSeed: "s".repeat(64), clientSeed: "x", betsPlayed: 1 }], active: { pairId: "33333333-3333-4333-8333-333333333333", commitment: "z".repeat(64), clientSeed: "y", nextNonce: 0 } })
          : fairness(),
      "GET /games/roulette/rounds": () => [round({ pairId: PAIR_NEW })],
      "POST /fairness/me/rotate": () => {
        revealed = true;
        return fairness({ retired: [{ pairId: PAIR_NEW, commitment: "c".repeat(64), serverSeed: "s".repeat(64), clientSeed: "x", betsPlayed: 1 }], active: { pairId: "33333333-3333-4333-8333-333333333333", commitment: "z".repeat(64), clientSeed: "y", nextNonce: 0 } });
      },
    });
    renderApp(<History />, { api });

    await userEvent.click(await screen.findByRole("button", { name: /Revelar semilla y verificar/ }));
    const dialog = screen.getByRole("alertdialog");
    expect(api.calls.some((c) => c.path === "/fairness/me/rotate")).toBe(false); // todavia no se roto
    await userEvent.click(within(dialog).getByRole("button", { name: /Sí, revelar/ }));

    await waitFor(() => expect(api.calls.some((c) => c.method === "POST" && c.path === "/fairness/me/rotate")).toBe(true));
    expect(await screen.findByRole("link", { name: "Verificar" })).toBeInTheDocument();
  });

  it("can cancel the reveal without rotating", async () => {
    const api = fakeApi({
      "GET /wallet/me": () => account,
      "GET /fairness/me": () => fairness(),
      "GET /games/roulette/rounds": () => [round({ pairId: PAIR_NEW })],
    });
    renderApp(<History />, { api });

    await userEvent.click(await screen.findByRole("button", { name: /Revelar semilla y verificar/ }));
    await userEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(api.calls.some((c) => c.method === "POST")).toBe(false);
  });

  it("disables the reveal while there are bets in progress", async () => {
    const api = fakeApi({
      "GET /wallet/me": () => account,
      "GET /fairness/me": () => fairness({ pendingBets: 2 }),
      "GET /games/roulette/rounds": () => [round({ pairId: PAIR_NEW })],
    });
    renderApp(<History />, { api });

    expect(await screen.findByRole("button", { name: /Hay apuestas en curso/ })).toBeDisabled();
  });

  it("shows an empty state for a player who has not played", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account, "GET /fairness/me": () => fairness(), "GET /games/roulette/rounds": () => [] });
    renderApp(<History />, { api });

    expect(await screen.findByText("Todavía no jugaste.")).toBeInTheDocument();
  });
});
