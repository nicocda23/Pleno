# ADR 0001 — Monolito modular primero

- **Estado:** aceptada
- **Fecha:** 2026-10-08

## Contexto
El proyecto busca practicar arquitectura distribuida, pero separar servicios desde el inicio multiplica la complejidad (despliegue, trazas, consistencia) antes de conocer los limites reales del dominio.

## Decision
Se arranca con un **monolito modular**: un host (`Casino.Api`) y modulos con limites claros (`Wallet`, `Games`, `Realtime`, `Users`, `Promotions`). Cada modulo es un proyecto con carpetas `Domain`, `Application`, `Infrastructure` y `Api`, y solo se comunica con otros por contratos o eventos. En la fase 4 se extraen **Wallet** y **Game Engine** como servicios.

## Consecuencias
- Iteracion rapida en las fases 0 a 3, con una sola unidad de despliegue.
- La extraccion posterior queda documentada y se puede defender: se separa porque hay un motivo concreto, no por moda.
- Exige disciplina: ningun modulo accede al modelo interno de otro.
