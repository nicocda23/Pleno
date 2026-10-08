import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Puerto fijo: Keycloak solo acepta redirecciones a http://localhost:5173/* (ver realms/casino-realm.json).
const port = Number(process.env.PORT ?? 5173);

export default defineConfig({
  plugins: [react()],
  server: { port, strictPort: true },
  preview: { port, strictPort: true },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: "./src/test/setup.ts",
    css: false,
    exclude: ["e2e/**", "node_modules/**"],
  },
});
