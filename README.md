# openapi-mcp

**Turn any REST API into MCP tools an agent can use *safely*** — auth, rate-limits, retries and an
allowlist, not a naïve wrapper. Written in **C# / .NET 8** on the official
[`ModelContextProtocol`](https://github.com/modelcontextprotocol/csharp-sdk) SDK.

Point it at an **OpenAPI / Swagger** document and it exposes each operation as a typed
[Model Context Protocol](https://modelcontextprotocol.io) tool that Claude (or any MCP client) can call —
with the hard part of integration handled for you: idempotency, retries with backoff, timeouts,
rate-limits, input validation, and a policy gate that keeps mutating and out-of-scope calls off by
default and secrets out of the agent.

## Why

"Connect the AI to my system" is the most common ask right now, and most MCP servers are thin,
unsafe wrappers. This one treats the server as a **boundary**, not a transparent proxy — the design a
senior integration engineer would ship to production.

## Status

Honest, incremental build. What runs today vs. what's next:

| Area | Status |
|---|---|
| MCP stdio server (initialize / tools/list / tools/call) | ✅ working — see the smoke test below |
| Runs without ICU (`InvariantGlobalization`) / Docker-ready | ✅ |
| Generate MCP tools from an OpenAPI document | 🔜 next |
| Policy gate (operation allowlist · mutating verbs off by default · secret redaction) | 🔜 next |
| Resilient HTTP dispatch (Polly: retries/backoff/timeout/circuit-breaker · idempotency) | 🔜 next |
| Pluggable auth (API key · OAuth2 client-credentials · bearer) — kept out of the agent | 🔜 next |
| Streamable HTTP transport (remote) | 🔜 next |
| xUnit tests (schema→tool · policy · resilience) + CI | 🔜 next |
| Demo: synthetic REST API + Claude Desktop wiring | 🔜 next |

This is a portfolio project; every demo uses synthetic data and the README will show real
latency/errors (including the "where it fails" cases), not a perfect-agent story.

## Run it (current slice)

Requires the .NET 8 SDK.

```bash
dotnet run --project src/OpenApiMcp.Server
```

The server speaks MCP over stdio. Smoke-test the handshake end-to-end:

```bash
dotnet build src/OpenApiMcp.Server
{ cat scripts/smoke.jsonl; sleep 2; } \
  | dotnet src/OpenApiMcp.Server/bin/Debug/net8.0/OpenApiMcp.Server.dll 2>/dev/null
```

You should see `initialize`, a `tools/list` containing the `ping` tool, and a `tools/call` returning
`pong`.

### Wire it to Claude Desktop

Add to your MCP client config (once packaged):

```json
{
  "mcpServers": {
    "openapi-mcp": {
      "command": "dotnet",
      "args": ["/absolute/path/to/OpenApiMcp.Server.dll"]
    }
  }
}
```

## Architecture

The single load-bearing decision — the server is a **security boundary** — is written up in
[`docs/adr/0001-mcp-as-a-boundary.md`](docs/adr/0001-mcp-as-a-boundary.md).

## License

MIT (to be added).
