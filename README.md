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
| OAuth2 client-credentials auth flow | 🔜 next |
| Streamable HTTP transport (remote, multi-client) | 🔜 next |
| Richer response shaping / pagination helpers | 🔜 next |
| Screen recording (Loom) + a bundled synthetic demo API | 🔜 next |

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

Run the tests:

```bash
dotnet test
```

### Docker

```bash
docker build -t openapi-mcp .
docker run -i --rm openapi-mcp   # speaks MCP over stdio
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
- **`auth.type`** — `none` · `apiKey` (with `headerName`) · `bearer`. `value` may reference an environment
  variable as `${VAR_NAME}` so secrets stay out of the config file and out of the agent.

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
