# ADR 0001 — The MCP server is a security boundary, not a transparent proxy

Status: accepted · Date: 2026-07-05

## Context

The obvious way to "connect an agent to a REST API" is to generate one MCP tool per OpenAPI operation
and forward calls straight through. That is fast to build and dangerous in production: it hands an LLM
every operation the API exposes — including destructive ones — passes credentials through a component
the model can influence, and offers no protection when the downstream is slow, flaky, or rate-limited.

## Decision

`openapi-mcp` generates tools from the OpenAPI document but routes every call through a **policy gate**
before it reaches the downstream API. Concretely:

1. **Allowlist of operations.** Only operations explicitly enabled in config are exposed as tools.
2. **Mutating verbs off by default.** `POST` / `PUT` / `PATCH` / `DELETE` are disabled unless opted in
   per operation — the safe default is read-only.
3. **Auth lives in the server, never in the agent.** API keys / OAuth2 tokens are injected into the
   outbound HTTP request by the server; they are never part of a tool's input schema and never returned
   to the model. Secrets are redacted from logs and traces.
4. **Input is validated against the operation schema** before any request is made.
5. **The downstream is treated as unreliable:** timeouts, retries with backoff, and a circuit-breaker
   (Polly); idempotency keys on non-safe operations.

## Consequences

- Safe by default; a client opts into risk deliberately, per operation.
- The agent cannot exfiltrate credentials or reach operations outside the allowlist, even under prompt
  injection, because the boundary — not the model — decides what is callable and what is sent.
- Slightly more configuration than a pass-through wrapper. That is the point: the configuration *is* the
  security policy.

## Alternatives considered

- **Transparent pass-through proxy** — rejected: no safety, credentials exposed to the model's blast
  radius, no resilience.
- **Read-only-only server** — rejected as too narrow; real workflows need guarded writes, so the design
  supports them behind explicit opt-in rather than forbidding them.
