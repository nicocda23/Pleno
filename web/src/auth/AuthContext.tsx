import type { User } from "oidc-client-ts";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { OidcManager } from "./oidc";

export type AuthStatus = "loading" | "authenticated" | "anonymous";

export interface AuthApi {
  status: AuthStatus;
  user: User | null;
  /** Nombre para mostrar: sale del token, nunca se guarda en nuestra base. */
  displayName: string | null;
  /** Lleva al jugador a Keycloak. Al volver, la app lo regresa a `returnTo`. */
  login: (returnTo?: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Token vigente o null. Lo usan el cliente HTTP y SignalR en cada llamada, asi siempre es el mas reciente. */
  getAccessToken: () => Promise<string | null>;
  /** Completa el regreso de Keycloak (`/auth/callback`). Se ejecuta una sola vez aunque React monte dos veces. */
  completeLogin: () => Promise<string>;
}

const AuthContext = createContext<AuthApi | null>(null);

export function AuthProvider({ manager, children }: { manager: OidcManager; children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [status, setStatus] = useState<AuthStatus>("loading");
  const callback = useRef<Promise<string> | null>(null);

  useEffect(() => {
    let active = true;
    const onLoaded = (loaded: User) => {
      if (!active) return;
      setUser(loaded);
      setStatus("authenticated");
    };
    const onUnloaded = () => {
      if (!active) return;
      setUser(null);
      setStatus("anonymous");
    };

    manager.events.addUserLoaded(onLoaded);
    manager.events.addUserUnloaded(onUnloaded);
    manager
      .getUser()
      .then((existing) => {
        if (!active) return;
        if (existing && !existing.expired) onLoaded(existing);
        else onUnloaded();
      })
      .catch(onUnloaded);

    return () => {
      active = false;
      manager.events.removeUserLoaded(onLoaded);
      manager.events.removeUserUnloaded(onUnloaded);
    };
  }, [manager]);

  const login = useCallback(
    (returnTo = window.location.pathname + window.location.search) => manager.signinRedirect({ state: { returnTo } }),
    [manager],
  );

  const logout = useCallback(() => manager.signoutRedirect(), [manager]);

  const getAccessToken = useCallback(async () => {
    const current = await manager.getUser();
    return current && !current.expired ? current.access_token : null;
  }, [manager]);

  const completeLogin = useCallback(() => {
    // El codigo de autorizacion se puede canjear una sola vez: React en desarrollo monta los efectos dos veces,
    // asi que la promesa se comparte para no canjearlo de nuevo (daria "No matching state").
    callback.current ??= manager.signinRedirectCallback().then((signedIn) => {
      const state = signedIn.state as { returnTo?: string } | undefined;
      return state?.returnTo ?? "/";
    });
    return callback.current;
  }, [manager]);

  const value = useMemo<AuthApi>(
    () => ({
      status,
      user,
      displayName: (user?.profile.preferred_username as string | undefined) ?? user?.profile.name ?? null,
      login,
      logout,
      getAccessToken,
      completeLogin,
    }),
    [status, user, login, logout, getAccessToken, completeLogin],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthApi {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth debe usarse dentro de <AuthProvider>.");
  return context;
}
