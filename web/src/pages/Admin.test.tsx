import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AuditEntry, Me } from "../api/types";
import { fakeApi, renderApp } from "../test/harness";
import { Admin } from "./Admin";

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

describe("Admin", () => {
  it("refuses entry to someone without the backoffice role and never asks for the player list", async () => {
    const api = fakeApi({ "GET /me": () => me(["player"]), "GET /wallet/me": () => account });
    renderApp(<Admin />, { api });

    expect(await screen.findByText("No tenés permisos de administrador.")).toBeInTheDocument();
    expect(api.calls.some((c) => c.path.startsWith("/backoffice"))).toBe(false);
  });

  it("credits chips to the chosen player with an idempotency key", async () => {
    const api = fakeApi({
      "GET /me": () => me(["backoffice", "player"]),
      "GET /wallet/me": () => account,
      "GET /backoffice/users": () => users,
      [`GET /backoffice/wallet/users/${OTHER_ID}`]: () => account,
      [`POST /backoffice/wallet/users/${OTHER_ID}/credit`]: () => ({ transactionId: "t1", isDuplicate: false }),
    });
    renderApp(<Admin />, { api });

    await userEvent.click(await screen.findByRole("button", { name: /0a1b2c3d/ }));
    expect(await screen.findByText("500 fichas")).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Fichas a cargar"), "250");
    await userEvent.click(screen.getByRole("button", { name: "Cargar fichas" }));

    expect(await screen.findByText("Se cargaron 250 fichas.")).toBeInTheDocument();
    const post = api.calls.find((c) => c.method === "POST")!;
    expect(post.body).toEqual({ amount: 250 });
    expect(post.headers?.["Idempotency-Key"]).toMatch(/^[0-9a-f-]{36}$/);
  });

  it("shows the audit log with who credited whom and how much", async () => {
    const api = fakeApi({
      "GET /me": () => me(["backoffice", "player"]),
      "GET /wallet/me": () => account,
      "GET /backoffice/users": () => users,
      "GET /backoffice/wallet/audit": () => [
        entry({ transactionId: "t2", amount: 1_000 }),
        entry({ transactionId: "t1", actorUserId: "99999999-0000-4000-8000-000000000000", amount: 50 }),
      ],
    });
    renderApp(<Admin />, { api });

    expect(await screen.findByText("+1.000")).toBeInTheDocument();
    expect(screen.getByText("+50")).toBeInTheDocument();
    expect(screen.getByText(/5078cc64….*\(vos\)/)).toBeInTheDocument();
    expect(screen.getByText("99999999…0000")).toBeInTheDocument();
  });

  it("says so when there are no credits yet", async () => {
    const empty = fakeApi({ "GET /me": () => me(["backoffice", "player"]), "GET /wallet/me": () => account, "GET /backoffice/users": () => users, "GET /backoffice/wallet/audit": () => [] });
    renderApp(<Admin />, { api: empty });
    expect(await screen.findByText("Todavía no hay cargas.")).toBeInTheDocument();
  });

  it("does not allow loading zero, negative or decimal amounts", async () => {
    const api = fakeApi({
      "GET /me": () => me(["backoffice", "player"]),
      "GET /wallet/me": () => account,
      "GET /backoffice/users": () => users,
      [`GET /backoffice/wallet/users/${OTHER_ID}`]: () => account,
    });
    renderApp(<Admin />, { api });

    await userEvent.click(await screen.findByRole("button", { name: /0a1b2c3d/ }));
    const input = screen.getByLabelText("Fichas a cargar");
    const submit = screen.getByRole("button", { name: "Cargar fichas" });
    for (const bad of ["0", "-5", "2.5"]) {
      await userEvent.clear(input);
      await userEvent.type(input, bad);
      expect(submit).toBeDisabled();
    }
  });
});
