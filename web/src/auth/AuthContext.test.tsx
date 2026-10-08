import { act, render, screen, waitFor } from "@testing-library/react";
import { AuthProvider, useAuth } from "./AuthContext";
import { fakeOidc, fakeUser } from "../test/harness";

function Probe() {
  const auth = useAuth();
  return (
    <div>
      <span data-testid="status">{auth.status}</span>
      <span data-testid="name">{auth.displayName ?? "-"}</span>
      <button onClick={() => void auth.login("/historial")}>login</button>
      <button onClick={() => void auth.logout()}>logout</button>
      <button
        onClick={async () => {
          const results = await Promise.all([auth.completeLogin(), auth.completeLogin()]);
          document.title = results.join("|");
        }}
      >
        callback
      </button>
      <button onClick={async () => (document.title = String(await auth.getAccessToken()))}>token</button>
    </div>
  );
}

const mount = (manager: ReturnType<typeof fakeOidc>) =>
  render(
    <AuthProvider manager={manager}>
      <Probe />
    </AuthProvider>,
  );

describe("AuthProvider", () => {
  it("starts loading and becomes authenticated when there is a valid stored session", async () => {
    mount(fakeOidc(fakeUser({ name: "alicia" })));

    expect(screen.getByTestId("status")).toHaveTextContent("loading");
    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("authenticated"));
    expect(screen.getByTestId("name")).toHaveTextContent("alicia");
  });

  it("is anonymous when there is no session", async () => {
    mount(fakeOidc(null));

    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("anonymous"));
  });

  it("treats an expired stored session as anonymous", async () => {
    mount(fakeOidc(fakeUser({ expired: true })));

    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("anonymous"));
  });

  it("reacts when the session is renewed or removed", async () => {
    const manager = fakeOidc(null);
    mount(manager);
    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("anonymous"));

    act(() => manager.emitUserLoaded(fakeUser()));
    expect(screen.getByTestId("status")).toHaveTextContent("authenticated");

    act(() => manager.emitUserUnloaded());
    expect(screen.getByTestId("status")).toHaveTextContent("anonymous");
  });

  it("sends the player to Keycloak remembering where to come back", async () => {
    const manager = fakeOidc(null);
    mount(manager);

    screen.getByText("login").click();

    await waitFor(() => expect(manager.spies.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: "/historial" } }));
  });

  it("signs out through Keycloak", async () => {
    const manager = fakeOidc();
    mount(manager);

    screen.getByText("logout").click();

    await waitFor(() => expect(manager.spies.signoutRedirect).toHaveBeenCalledOnce());
  });

  it("redeems the authorization code only once even if the callback runs twice (React strict mode)", async () => {
    const manager = fakeOidc(null);
    mount(manager);

    screen.getByText("callback").click();

    await waitFor(() => expect(document.title).toBe("/ruleta|/ruleta"));
    expect(manager.spies.signinRedirectCallback).toHaveBeenCalledOnce();
  });

  it("gives the current token, and null when it has expired", async () => {
    const fresh = fakeOidc(fakeUser());
    const { unmount } = mount(fresh);
    screen.getByText("token").click();
    await waitFor(() => expect(document.title).toBe("token-de-prueba"));
    unmount();

    const expired = fakeOidc(fakeUser({ expired: true }));
    mount(expired);
    screen.getByText("token").click();
    await waitFor(() => expect(document.title).toBe("null"));
  });
});
