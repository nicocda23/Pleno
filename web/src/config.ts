// Configuracion publica del front. Nada de esto es secreto: el navegador es un cliente "publico" de OIDC
// (Authorization Code + PKCE) y no guarda ningun secreto. Los valores por defecto sirven para `aspire run`.
export const config = {
  apiUrl: (import.meta.env.VITE_API_URL ?? "http://localhost:5188").replace(/\/$/, ""),
  oidcAuthority: import.meta.env.VITE_OIDC_AUTHORITY ?? "http://localhost:8080/realms/casino",
  oidcClientId: import.meta.env.VITE_OIDC_CLIENT_ID ?? "casino-web",
} as const;
