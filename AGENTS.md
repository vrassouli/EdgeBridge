# EdgeBridge

EdgeBridge is a hardware abstraction and remote device runtime platform.

The primary goal is that application code should not know whether it is interacting with:

- Local hardware
- Remote hardware
- Simulator hardware

All application code must depend only on EdgeBridge.Abstractions.

## Architectural Rules

- Abstractions never depend on implementations.
- Client SDK and Agent must expose identical behavior.
- All APIs are async-first.
- All public APIs accept CancellationToken.
- Transport is replaceable.
- Protocol messages are versionable.
- Avoid Raspberry Pi specific names.
- Design for Raspberry Pi, ESP32, Arduino, Jetson and future devices.

Application code must never know whether the hardware is local, remote, or simulated.

## Project Memory

This repository is the source of truth for project decisions.

Whenever the user provides a new rule, preference, architectural decision, naming decision, protocol requirement, design constraint, or product direction:

- Determine whether it should be persisted.
- If it has long-term value, record it in the appropriate project documentation.
- Keep documentation synchronized with implementation.
- Do not rely solely on chat history.
- Future sessions must be able to understand the project by reading repository documentation.

Documentation should evolve together with the codebase.

## Design Decisions

Every significant design decision must either:

- be implemented in code, or
- be documented as an ADR.

Undocumented decisions are considered temporary.
