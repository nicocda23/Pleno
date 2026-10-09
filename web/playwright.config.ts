import { defineConfig, devices } from "@playwright/test";

// Pruebas de extremo a extremo con un navegador REAL contra el sistema levantado (`aspire start`):
// front en :5173, API, Keycloak en :8080, RabbitMQ, Postgres y Redis. No corren en el CI: necesitan toda la infraestructura.
export default defineConfig({
  testDir: "./e2e",
  timeout: 120_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
