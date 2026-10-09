import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AuditEntry, CreditHistory, Me } from "../api/types";
import { fakeApi, renderApp } from "../test/harness";
import { AdminCredits } from "./AdminCredits";

const ME_ID = "5078cc64-5113-4eb1-ad1a-cf743f87acbb";
const OTHER_ID = "0a1b2c3d-0001-4000-8000-000000000001";

const me = (roles: string[]): Me => ({ userId: ME_ID, displayName: "nicocda", roles, accountId: "a", registeredAt: "2026-10-01T10:00:00Z" });
const account = { accountId: "a1", userId: OTHER_ID, available: 500, reserved: 0, version: 1, openReservations: {} };
const users = [
  { userId: ME_ID, registeredAt: "2026-10-01T10:00:00Z" },
  { userId: OTHER_ID, registeredAt: "2026-10-02T10:00:00Z" },
];
const entry = (overrides: Partial<AuditEntry>): AuditEntry => ({
  action: "ChipsCredited", actorUserId: ME_ID, targetUserId: OTHER_ID, amount: 250, transactionId: "t1", occurredAt: "2026-10-09T12:00:00Z", ...overrides,
});
const history = (items: AuditEntry[], extra: Partial<CreditHistory> = {}): CreditHistory => ({
  items, nextBefore: null, totalAmount: items.reduce((s, e) => s + e.amount, 0), count: items.length, ...extra,
});

const creditCalls = (api: ReturnType<typeof fakeApi>) => api.calls.filter((c) => c.path.startsWith("/backoffice/wallet/credits")).map((c) => c.path);

function apiFor(handler: () => CreditHistory, roles = ["backoffice", "player"]) {
  return fakeApi({
    "GET /me": () => me(roles),
    "GET /wallet/me": () => account,
    "GET /backoffice/users": () => users,
    "GET /backoffice/wallet/credits": handler,
  });
}

describe("AdminCredits", () => {
  it("refuses entry to someone without the backoffice role and never asks for the history", async () => {
    const api = apiFor(() => history([]), ["player"]);
    renderApp(<AdminCredits />, { api });

    expect(await screen.findByText("No tenés permisos de administrador.")).toBeInTheDocument();
    expect(creditCalls(api)).toEqual([]);
  });

  it("shows the loads with who did them and the total of what matches the filter", async () => {
    const api = apiFor(() => history([entry({ transactionId: "t2", amount: 1_000 }), entry({ transactionId: "t1", amount: 250 })], { totalAmount: 5_250, count: 7 }));
    renderApp(<AdminCredits />, { api });

    const table = await screen.findByRole("table", { name: "Cargas de fichas" });
    expect(within(table).getAllByRole("row")).toHaveLength(3);
    expect(within(table).getByText("+1.000")).toBeInTheDocument();
    expect(within(table).getAllByText(/\(vos\)/)).toHaveLength(2);
    // El total es el del filtro completo (7 cargas), no el de las filas visibles.
    expect(screen.getByTestId("credit-total")).toHaveTextContent("7 cargas · 5.250 fichas en total");
  });

  it("filters by player and by date, sending the range as whole days", async () => {
    const api = apiFor(() => history([entry({})]));
    renderApp(<AdminCredits />, { api });
    await screen.findByRole("table", { name: "Cargas de fichas" });

    await userEvent.selectOptions(screen.getByLabelText("Jugador"), OTHER_ID);
    await userEvent.type(screen.getByLabelText("Desde"), "2026-10-01");
    await userEvent.type(screen.getByLabelText("Hasta"), "2026-10-09");

    await waitFor(() => {
      const last = new URL(creditCalls(api).at(-1)!, "http://x");
      expect(last.searchParams.get("userId")).toBe(OTHER_ID);
      expect(last.searchParams.get("from")).toBe(new Date(2026, 9, 1).toISOString());
      expect(last.searchParams.get("to")).toBe(new Date(2026, 9, 10).toISOString()); // "hasta el 9" incluye todo el 9
    });
  });

  it("does not ask the server for a backwards range and explains why", async () => {
    const api = apiFor(() => history([entry({})]));
    renderApp(<AdminCredits />, { api });
    await screen.findByRole("table", { name: "Cargas de fichas" });

    await userEvent.type(screen.getByLabelText("Desde"), "2026-10-09");
    await userEvent.type(screen.getByLabelText("Hasta"), "2026-10-01");

    expect(await screen.findByText(/tiene que ser anterior/)).toBeInTheDocument();
    const backwards = new Date(2026, 9, 2).toISOString(); // fin exclusivo de "hasta 1/10"
    expect(creditCalls(api).some((path) => path.includes(`to=${encodeURIComponent(backwards)}`) && path.includes("from="))).toBe(false);
  });

  it("clears the filters", async () => {
    const api = apiFor(() => history([entry({})]));
    renderApp(<AdminCredits />, { api });
    await screen.findByRole("table", { name: "Cargas de fichas" });
    await userEvent.selectOptions(screen.getByLabelText("Jugador"), OTHER_ID);

    await userEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));

    expect(screen.getByLabelText("Jugador")).toHaveValue("");
    expect(screen.getByRole("button", { name: "Limpiar filtros" })).toBeDisabled();
  });

  it("loads older loads with the cursor and keeps the total", async () => {
    let call = 0;
    const api = apiFor(() => {
      call += 1;
      return call === 1
        ? history([entry({ transactionId: "t2", amount: 20 })], { nextBefore: "2026-10-09T11:00:00+00:00", totalAmount: 30, count: 2 })
        : history([entry({ transactionId: "t1", amount: 10 })], { totalAmount: 30, count: 2 });
    });
    renderApp(<AdminCredits />, { api });

    await userEvent.click(await screen.findByRole("button", { name: "Ver cargas anteriores" }));

    await waitFor(() => expect(within(screen.getByRole("table", { name: "Cargas de fichas" })).getAllByRole("row")).toHaveLength(3));
    expect(creditCalls(api).at(-1)).toContain("before=2026-10-09T11%3A00%3A00%2B00%3A00");
    expect(screen.getByTestId("credit-total")).toHaveTextContent("2 cargas · 30 fichas en total");
  });

  it("says so when nothing matches", async () => {
    renderApp(<AdminCredits />, { api: apiFor(() => history([], { count: 0, totalAmount: 0 })) });

    expect(await screen.findByText("No hay cargas con ese filtro.")).toBeInTheDocument();
  });
});
