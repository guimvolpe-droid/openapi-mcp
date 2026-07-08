# ADR 0002 — Dual transport: stdio by default, Streamable HTTP opt-in and fail-closed

Status: accepted · Date: 2026-07-08

## Context

The stdio transport covers the "local client spawns the process" case (Claude Desktop, CLI). But an
orchestration hub — a remote MCP client that outlives any one process — needs to reach the same
tools over the network. The transport must not weaken what ADR 0001 established: this server is a
security boundary, and the policy gate + server-side auth hold no matter how a call arrives.

## Decision

One assembly, two transports, **the same catalog, policy gate and invoker** (single assembly point,
`Hosting/ServerHost.cs`):

- **stdio stays the default.** No flags, no listener, minimal surface — one local client.
- **`OPENAPI_MCP_HTTP=1` turns on Streamable HTTP** at `http://127.0.0.1:$OPENAPI_MCP_HTTP_PORT/mcp`
  (SDK's `WithHttpTransport`, stateless mode — requests are independent, no session affinity).
- **Loopback only, by default.** Binding beyond `127.0.0.1` is an explicit deploy decision:
  `OPENAPI_MCP_HTTP_URL` overrides the bind (e.g. `0.0.0.0` *inside a container* whose port-publish
  is itself restricted to the host's loopback — see `docker-compose.yml`).
- **Bearer auth, fail-closed.** `OPENAPI_MCP_HTTP_TOKEN` unset ⇒ **every** request answers 503 — an
  unconfigured server is a disabled server, never an open one. Wrong token ⇒ 401. Comparison is
  constant-time over SHA-256 digests.

## Consequences

- The policy gate is provably transport-independent: `HttpTransportTests` boots the bundled demo
  upstream and the HTTP server in-process (port 0) and drives them with the SDK's own `McpClient` —
  `createPost` stays hidden over HTTP exactly as over stdio, and the 401/503 paths are asserted.
  Everything runs offline.
- stdio consumers see zero change; the HTTP surface only exists when asked for.
- The Docker image base moved `runtime` → `aspnet`: the server now carries a
  `FrameworkReference Microsoft.AspNetCore.App`, without which the published app cannot start.
- Known limitation, stated in the README: no TLS termination here — HTTPS is the reverse-proxy /
  tunnel's job at deploy time.

## Alternatives considered

- **HTTP always on** — rejected: a listener nobody asked for is attack surface; stdio users pay for
  nothing.
- **Fail-open when no token is set** (open on loopback) — rejected: loopback is not trust
  (any local process could call a mutating-enabled config); 503-until-configured makes
  misconfiguration loud and safe.
- **Separate HTTP binary** — rejected: two artifacts drift; one assembly point keeps the boundary
  identical by construction.
