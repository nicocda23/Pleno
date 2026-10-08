import { act, screen, waitFor } from "@testing-library/react";
import { ApiError } from "../api/client";
import { BalanceChip } from "../components/BalanceChip";
import { fakeApi, FakeHub, renderApp } from "../test/harness";
import { useRealtime } from "./RealtimeProvider";

const account = (version: number, available: number, reserved = 0) => ({
  accountId: "a1", userId: "u1", available, reserved, version, openReservations: {},
});

function Probe() {
  const { balance, connection } = useRealtime();
  return (
    <div>
      <span data-testid="avail">{balance.ready ? balance.available : "loading"}</span>
      <span data-testid="version">{balance.version}</span>
      <span data-testid="conn">{connection}</span>
    </div>
  );
}

describe("RealtimeProvider", () => {
  it("shows the HTTP balance first and never a false zero while loading", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account(2, 1000) });
    renderApp(<Probe />, { api });

    expect(screen.getByTestId("avail")).toHaveTextContent("loading");
    await waitFor(() => expect(screen.getByTestId("avail")).toHaveTextContent("1000"));
  });

  it("applies live balance notices on top of the HTTP state", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account(2, 1000) });
    const { hub } = renderApp(<Probe />, { api });
    await waitFor(() => expect(screen.getByTestId("avail")).toHaveTextContent("1000"));

    act(() => hub.emit("balanceChanged", { available: 900, reserved: 100, version: 3 }));

    expect(screen.getByTestId("avail")).toHaveTextContent("900");
    expect(screen.getByTestId("version")).toHaveTextContent("3");
  });

  it("ignores a stale or repeated notice", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account(5, 800) });
    const { hub } = renderApp(<Probe />, { api });
    await waitFor(() => expect(screen.getByTestId("avail")).toHaveTextContent("800"));

    act(() => hub.emit("balanceChanged", { available: 1000, reserved: 0, version: 3 })); // vieja
    act(() => hub.emit("balanceChanged", { available: 800, reserved: 0, version: 5 })); // repetida

    expect(screen.getByTestId("avail")).toHaveTextContent("800");
    expect(screen.getByTestId("version")).toHaveTextContent("5");
  });

  it("reports the connection state as it changes", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account(2, 1000) });
    const { hub } = renderApp(<Probe />, { api });
    await waitFor(() => expect(screen.getByTestId("conn")).toHaveTextContent("connected"));

    act(() => hub.dropConnection());
    expect(screen.getByTestId("conn")).toHaveTextContent("reconnecting");

    act(() => hub.recoverConnection());
    expect(screen.getByTestId("conn")).toHaveTextContent("connected");

    act(() => hub.closeConnection());
    expect(screen.getByTestId("conn")).toHaveTextContent("disconnected");
  });

  it("asks for the full state again after reconnecting, because notices only cover the time it was connected", async () => {
    let served = account(2, 1000);
    const api = fakeApi({ "GET /wallet/me": () => served });
    const { hub } = renderApp(<Probe />, { api });
    await waitFor(() => expect(screen.getByTestId("avail")).toHaveTextContent("1000"));

    served = account(6, 700, 50); // pasaron cosas mientras no habia conexion
    act(() => hub.dropConnection());
    act(() => hub.recoverConnection());

    await waitFor(() => expect(screen.getByTestId("avail")).toHaveTextContent("700"));
    expect(screen.getByTestId("version")).toHaveTextContent("6");
  });

  it("marks the connection as disconnected when it cannot start", async () => {
    const hub = new FakeHub();
    hub.startError = new Error("no hay servidor");
    renderApp(<Probe />, { hub, api: fakeApi({ "GET /wallet/me": () => account(2, 1000) }) });

    await waitFor(() => expect(screen.getByTestId("conn")).toHaveTextContent("disconnected"));
  });

  it("retries while a brand new account is still being opened (404) and then shows it", async () => {
    let attempts = 0;
    const api = fakeApi({
      "GET /wallet/me": () => {
        attempts += 1;
        if (attempts < 3) throw new ApiError(404, "AccountNotFound");
        return account(2, 1000);
      },
    });
    // El reintento real espera 1 s entre intentos; se acepta ese tiempo en la prueba.
    renderApp(<Probe />, { api });

    await waitFor(() => expect(screen.getByTestId("avail")).toHaveTextContent("1000"), { timeout: 6_000 });
    expect(attempts).toBe(3);
  }, 10_000);

  it("stops the connection when the page unmounts", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account(2, 1000) });
    const { hub, unmount } = renderApp(<Probe />, { api });
    await waitFor(() => expect(hub.started).toBe(true));

    unmount();

    expect(hub.stopped).toBe(true);
  });
});

describe("BalanceChip", () => {
  it("shows a dash until the first real balance arrives, then the formatted amount", async () => {
    const api = fakeApi({ "GET /wallet/me": () => account(2, 1234) });
    renderApp(<BalanceChip />, { api });

    expect(screen.getByTestId("balance-value")).toHaveTextContent("—");
    await waitFor(() => expect(screen.getByTestId("balance-value")).toHaveTextContent("1.234"));
  });
});
