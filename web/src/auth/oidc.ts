import { UserManager, WebStorageStateStore, type User } from "oidc-client-ts";
import { config } from "../config";

/** Lo minimo que la app necesita del cliente OIDC. Permite sustituirlo en las pruebas sin levantar Keycloak. */
export interface OidcManager {
  getUser(): Promise<User | null>;
  signinRedirect(args?: { state?: unknown }): Promise<void>;
  signoutRedirect(): Promise<void>;
  signinRedirectCallback(): Promise<User>;
  signinSilent(): Promise<User | null>;
  events: {
    addUserLoaded(cb: (user: User) => void): void;
    removeUserLoaded(cb: (user: User) => void): void;
    addUserUnloaded(cb: () => void): void;
    removeUserUnloaded(cb: () => void): void;
    addSilentRenewError(cb: (error: Error) => void): void;
    removeSilentRenewError(cb: (error: Error) => void): void;
  };
}

/**
 * Cliente OpenID Connect contra Keycloak: Authorization Code + PKCE (cliente publico, sin secreto).
 * La sesion se guarda en sessionStorage: se pierde al cerrar la pestaña. Los tokens duran 5 minutos y se renuevan solos.
 */
export function createOidcManager(): OidcManager {
  return new UserManager({
    authority: config.oidcAuthority,
    client_id: config.oidcClientId,
    redirect_uri: `${window.location.origin}/auth/callback`,
    post_logout_redirect_uri: `${window.location.origin}/`,
    response_type: "code",
    scope: "openid profile",
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  });
}
