# Architecture Skill

When implementing features:

1. First check if the feature belongs in Abstractions.
2. Then define Protocol contracts.
3. Then implement Agent side.
4. Then implement Client side.

Never create Client-only APIs.

The public abstraction must remain identical between local and remote implementations.
