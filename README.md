# openapi-mcp

**Turn any REST API into MCP tools an agent can use *safely*** — auth, rate-limits, retries and an
allowlist, not a naïve wrapper. Written in **C# / .NET 8** on the official
[`ModelContextProtocol`](https://github.com/modelcontextprotocol/csharp-sdk) SDK.

Point it at an **OpenAPI / Swagger** document and it exposes each operation as a typed
[Model Context Protocol](https://modelcontextprotocol.io) tool that Claude (or any MCP client) can call.
The hard part of integration is handled for you, and — critically — the server is a **security boundary**,
not a transparent proxy: mutating and out-of-scope calls are off by default, and credentials never reach
the model.

## Why

"Connect the AI to my system" is the most common integration ask right now, and most MCP servers are thin,
unsafe wrappers around a whole API. This one is the design a senior integration engineer would actually
ship to production.

## What it does

- **Generates tools from OpenAPI.** Each operation → one MCP tool, with a JSON input schema derived from
  its parameters and request body.
- **Policy gate (safe by default).** An **allowlist** of operations, and **mutating verbs
  (POST/PUT/PATCH/DELETE) disabled unless you opt in** — per the [ADR](docs/adr/0001-mcp-as-a-boundary.md).
- **Auth stays out of the agent.** API key / bearer tokens are injected into the outbound HTTP request by
  the server; they are never part of a tool's input schema and never returned to the model.
- **Treats the downstream as unreliable.** Requests go through a resilient HTTP pipeline
  (`Microsoft.Extensions.Http.Resilience` / Polly: retries, timeout, circuit-breaker).

## Status

Honest, incremental build. What runs today vs. what's next:

| Area | Status |
|---|---|
| MCP stdio server (initialize / tools/list / tools/call) | ✅ working |
| Generate MCP tools from an OpenAPI document (params + body → JSON schema) | ✅ working |
| Policy gate — operation allowlist · mutating verbs off by default | ✅ working, unit-tested |
| Resilient HTTP dispatch (retries / timeout / circuit-breaker) + path/query/header/body mapping | ✅ working |
| Pluggable auth (API key · bearer) injected server-side, kept out of the agent | ✅ working |
| Runs without ICU (`InvariantGlobalization`) · Dockerfile · GitHub Actions CI | ✅ |
| Streamable HTTP transport (`OPENAPI_MCP_HTTP=1`, loopback-only, Bearer fail-closed) | ✅ integration-tested with the SDK's real client ([ADR 0002](docs/adr/0002-dual-transport.md)) |
| Bundled synthetic demo API — the whole E2E flow runs offline | ✅ tested |
| OAuth2 client-credentials — token acquired/cached server-side, never seen by the agent | ✅ tested ([ADR 0003](docs/adr/0003-oauth2-client-credentials.md)) |
| Richer response shaping / pagination helpers | 🔜 next |
| Screen recording (Loom) | 🔜 next |

Every demo uses **synthetic / public data** and the README shows real HTTP status and error paths
(including the "where it fails" cases), not a perfect-agent story.

## Run it

Requires the .NET 8 SDK. Build and drive the full flow end to end against the bundled demo (which points
at the public, read-only [JSONPlaceholder](https://jsonplaceholder.typicode.com) API):

```bash
dotnet build src/OpenApiMcp.Server
{ cat scripts/smoke.jsonl; sleep 6; } \
  | dotnet src/OpenApiMcp.Server/bin/Debug/net8.0/OpenApiMcp.Server.dll 2>/dev/null
```

You will see, over MCP:

1. `tools/list` returns **`getPost`** and **`listPosts`** — but **not `createPost`**, because it is a
   `POST` and the demo policy has `allowMutating: false`. *That is the policy gate.*
2. `tools/call getPost {"id": 1}` performs the real HTTP `GET /posts/1` and returns `HTTP 200 OK` with the
   post JSON.
3. `tools/call createPost {...}` is rejected — `Unknown or disallowed tool` — because the gate never
   exposed it.

### Run it fully offline (bundled synthetic API)

No network? The repo bundles its own upstream — `OpenApiMcp.DemoApi`, a zero-dependency minimal API
with a deterministic seed (25 posts), userId filtering and `Link rel="next"` pagination:

```bash
ASPNETCORE_URLS=http://localhost:5088 dotnet run --project src/OpenApiMcp.DemoApi &   # the upstream
OPENAPI_MCP_CONFIG=$PWD/src/OpenApiMcp.Server/demo/config.local.json \
  bash -c '{ cat scripts/smoke.jsonl; sleep 3; } | dotnet src/OpenApiMcp.Server/bin/Debug/net8.0/OpenApiMcp.Server.dll 2>/dev/null'
```

Same three outcomes as above — policy gate, real HTTP 200, blocked mutation — with zero external
dependencies. (Port taken? Change `ASPNETCORE_URLS` and `baseUrl` in `demo/config.local.json`.)

### Streamable HTTP mode (remote clients, e.g. an orchestration hub)

Same catalog, policy gate and invoker — served over HTTP instead of stdio:

```bash
OPENAPI_MCP_HTTP=1 OPENAPI_MCP_HTTP_PORT=8410 \
OPENAPI_MCP_HTTP_TOKEN="$(openssl rand -base64 32)" \
dotnet run --project src/OpenApiMcp.Server
# → POST http://127.0.0.1:8410/mcp  (JSON-RPC: initialize / tools/list / tools/call)
```

Security posture: binds to **127.0.0.1 only** (exposing it further is an explicit deploy decision —
tunnel/reverse-proxy with TLS); Bearer auth is **fail-closed** (`OPENAPI_MCP_HTTP_TOKEN` unset ⇒ every
request answers 503; wrong token ⇒ 401; constant-time comparison over SHA-256 digests).

Run the tests:

```bash
dotnet test
```

### Docker

```bash
docker build -t openapi-mcp .
docker run -i --rm openapi-mcp   # speaks MCP over stdio
```

Or run the full containerized demo — synthetic upstream + MCP server in Streamable HTTP mode
(published to the host's loopback only):

```bash
export OPENAPI_MCP_HTTP_TOKEN="$(openssl rand -base64 32)"
docker compose up --build -d
curl -sN -X POST http://127.0.0.1:8410/mcp \
  -H "authorization: Bearer $OPENAPI_MCP_HTTP_TOKEN" \
  -H 'content-type: application/json' -H 'accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"curl","version":"0"}}}'
```

### Wire it to Claude Desktop (or any MCP client)

```json
{
  "mcpServers": {
    "openapi-mcp": {
      "command": "dotnet",
      "args": ["/absolute/path/to/OpenApiMcp.Server.dll"],
      "env": { "OPENAPI_MCP_CONFIG": "/absolute/path/to/your/config.json" }
    }
  }
}
```

## Configuration

The server reads a JSON config (env var `OPENAPI_MCP_CONFIG`, or the bundled `demo/config.json` by
default). `openApiPath` is resolved relative to the config file.

```json
{
  "openApiPath": "your-api.openapi.json",
  "baseUrl": "https://api.your-system.com",
  "policy": {
    "allowMutating": false,
    "allowedOperations": ["getCustomer", "listOrders"]
  },
  "auth": {
    "type": "bearer",
    "value": "${API_TOKEN}"
  }
}
```

- **`policy.allowedOperations`** — operation ids (or `"*"` for all). Combined with `allowMutating`, this is
  your security policy: only what you list, read-only unless you say otherwise.
- **`auth.type`** — `none` · `apiKey` (with `headerName`) · `bearer` · `oauth2`. Any credential field may
  reference an environment variable as `${VAR_NAME}` so secrets stay out of the config file and out of the
  agent.
- **`auth.type: "oauth2"`** (client-credentials) — add `tokenUrl`, `clientId`, `clientSecret` and optional
  `scope`. The server mints and caches the access token (expiry-aware, 30s skew) and attaches it as a
  Bearer header server-side; the model never sees the flow. Try it offline against the bundled demo:

  ```bash
  DEMO_API_REQUIRE_AUTH=1 ASPNETCORE_URLS=http://localhost:5088 dotnet run --project src/OpenApiMcp.DemoApi &
  DEMO_CLIENT_SECRET=demo-secret OPENAPI_MCP_CONFIG=$PWD/src/OpenApiMcp.Server/demo/config.oauth.json \
    bash -c '{ cat scripts/smoke.jsonl; sleep 3; } | dotnet src/OpenApiMcp.Server/bin/Debug/net8.0/OpenApiMcp.Server.dll 2>/dev/null'
  # getPost → HTTP 200 (token minted silently, server-side). Run with demo/config.local.json instead
  # of the oauth config and the upstream answers HTTP 401 — the "where it fails" case, honest and visible.
  ```

## Architecture

The one load-bearing decision — the server is a **security boundary** — is written up in
[`docs/adr/0001-mcp-as-a-boundary.md`](docs/adr/0001-mcp-as-a-boundary.md).

```
OpenAPI doc ──► OpenApiToolFactory ──► [policy gate] ──► MCP tools/list
                                                            │
MCP tools/call ──► policy re-check ──► ApiToolInvoker ──► resilient HTTP ──► your API
                                          (auth injected here, never in the agent)
```

## License

[MIT](LICENSE) © 2026 Guilherme Volpe
