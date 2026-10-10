import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "../api/client";
import type { Me } from "../api/types";
import { fakeApi, renderApp } from "../test/harness";
import { Cashier } from "./Cashier";

const ME_ID = "5078cc64-5113-4eb1-ad1a-cf743f87acbb";
const PLAYER_ID = "0a1b2c3d-0001-4000-8000-000000000001";
const account = { accountId: "a1", userId: ME_ID, available: 1_000, reserved: 0, version: 2, openReservations: {} };

const me = (roles: string[]): Me => ({ userId: ME_ID, displayName: "caja", roles, accountId: "a", registeredAt: "2026-10-01T10:00:00Z" });
const place = (level: string) => ({ userId: ME_ID, level, parentUserId: null });
const members = [{ userId: PLAYER_ID, level: "player", available: 50, reserved: 0 }, { userId: "0a1b2c3d-0002-4000-8000-000000000002", level: "player", available: null, reserved: null }];

describe("Cashier", () => {
  it("refuses entry to someone who is not a cashier and never asks for the jurisdiction", async () => {
    const api = fakeApi({ "GET /me": () => me(["player"]), "GET /wallet/me": () => account });
    renderApp(<Cashier />, { api });

    expect(await screen.findByText("No sos cajero ni jefe de cajeros.")).toBeInTheDocument();
    expect(api.calls.some((c) => c.path.startsWith("/cashier"))).toBe(false);
  });

  it("tells a cashier with no place in the tree to ask the administrator", async () => {
    const api = fakeApi({
      "GET /me": () => me(["player", "cashier"]),
      "GET /wallet/me": () => account,
      "GET /cashier/me": () => {
        throw new ApiError(404, "NotInJurisdiction", "x");
      },
    });
    renderApp(<Cashier />, { api });

    expect(await screen.findByText(/Todavía no te asignaron un lugar/)).toBeInTheDocument();
  });

  it("lists the players with their balance and loads chips from the cashier balance with an idempotency key", async () => {
    const api = fakeApi({
      "GET /me": () => me(["player", "cashier"]),
      "GET /wallet/me": () => account,
      "GET /cashier/me": () => place("cashier"),
      "GET /cashier/members": () => members,
      "GET /cashier/transfers": () => [],
      "POST /cashier/transfers": () => ({ transactionId: "t1", isDuplicate: false }),
    });
    renderApp(<Cashier />, { api });

    expect(await screen.findByText(/Tus jugadores/)).toBeInTheDocument();
    expect(await screen.findByText("sin cuenta todavía")).toBeInTheDocument();
    await userEvent.click(await screen.findByRole("button", { name: /0a1b2c3d…0001/ }));
    await userEvent.type(screen.getByLabelText("Fichas a cargar"), "150");
    await userEvent.click(screen.getByRole("button", { name: "Cargar fichas" }));

    await waitFor(() => expect(api.calls.some((c) => c.method === "POST" && c.path === "/cashier/transfers")).toBe(true));
    const call = api.calls.find((c) => c.method === "POST" && c.path === "/cashier/transfers")!;
    expect(call.body).toEqual({ toUserId: PLAYER_ID, amount: 150 });
    expect(call.headers?.["Idempotency-Key"]).toMatch(/^[0-9a-f-]{36}$/);
    expect(await screen.findByText("Cargaste 150 fichas.")).toBeInTheDocument();
  });

  it("does not let a cashier load more chips than the balance", async () => {
    const api = fakeApi({
      "GET /me": () => me(["player", "cashier"]),
      "GET /wallet/me": () => account,
      "GET /cashier/me": () => place("cashier"),
      "GET /cashier/members": () => members,
      "GET /cashier/transfers": () => [],
    });
    renderApp(<Cashier />, { api });

    await userEvent.click(await screen.findByRole("button", { name: /0a1b2c3d…0001/ }));
    await userEvent.type(screen.getByLabelText("Fichas a cargar"), "5000");

    expect(await screen.findByText("No te alcanzan las fichas para esa carga.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cargar fichas" })).toBeDisabled();
  });

  it("says cashiers instead of players for the head of cashiers", async () => {
    const api = fakeApi({
      "GET /me": () => me(["player", "head_cashier"]),
      "GET /wallet/me": () => account,
      "GET /cashier/me": () => place("head_cashier"),
      "GET /cashier/members": () => [{ userId: PLAYER_ID, level: "cashier", available: 10, reserved: 0 }],
      "GET /cashier/transfers": () => [],
    });
    renderApp(<Cashier />, { api });

    expect(await screen.findByText("Tus cajeros")).toBeInTheDocument();
    expect(screen.getByText("Jefe de cajeros", { selector: "p" })).toBeInTheDocument();
  });
});
