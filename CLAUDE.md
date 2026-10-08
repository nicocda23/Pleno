# Pleno — Casino Online (fichas ficticias)

Laboratorio de arquitectura distribuida en .NET. Ver `README.md` y `docs/PLAN.md`.

## Stack
.NET 10 · Aspire 13 (CLI) · PostgreSQL + EF Core + Marten · Redis · RabbitMQ (luego Azure Service Bus) · Wolverine · OpenTelemetry · React + Vite + PixiJS · Keycloak · xUnit + Testcontainers + k6.

## Comandos
- Levantar todo: `aspire run` (requiere Docker Desktop corriendo)
- Build: `dotnet build Casino.slnx`
- Tests: `dotnet test Casino.slnx`

## Reglas
- Ramas siempre desde `dev`; `main` solo recibe merges de `dev`. Commits convencionales (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
- Montos de fichas como **enteros** (`long`); nunca `float`/`double`/`decimal` para saldos.
- El saldo se **deriva** de asientos de partida doble; nunca se edita directamente.
- Toda operacion que mueve fichas lleva `IdempotencyKey`.
- **Nada de secretos** en codigo, `appsettings.json`, `.env` versionados ni pipelines: user-secrets en local, Key Vault en la nube.
- PII (email, nombre, documento) enmascarada en logs y fuera de trazas/metricas. Datos de prueba solo con Bogus.
- Tests obligatorios para todo lo que toque la Wallet (unitarios + integracion con Testcontainers).
- Cada fase cierra con un ADR en `docs/adr/`.

## Estructura
`src/Casino.AppHost`, `src/Casino.ServiceDefaults`, `src/Casino.Api`, `src/Modules/{Wallet,Games,Realtime,Users,Promotions}`, `tests/`, `web/`, `docs/`.
