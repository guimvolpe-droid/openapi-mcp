# ADR 0003 — OAuth2 client-credentials: token acquired and cached server-side

Status: accepted · Date: 2026-07-08

## Context

Static API keys and bearer tokens (ADR 0001: injected server-side, never visible to the agent)
cover many APIs, but real enterprise upstreams commonly authenticate machine-to-machine clients
with **OAuth2 client-credentials**: short-lived access tokens minted from a client id/secret at a
token endpoint. Making the *agent* deal with that flow would hand it the credential — exactly what
this server exists to prevent.

## Decision

`auth.type: "oauth2"` in the config. The server owns the entire flow:

- `OAuth2TokenProvider` POSTs `grant_type=client_credentials` (form-urlencoded, optional `scope`)
  to `auth.tokenUrl` and caches the token until `expires_in` **minus a 30s skew**; renewal is
  serialized with a semaphore so concurrent tool calls can't stampede the token endpoint.
- Auth application became an interface, `IAuthApplier` (async, because oauth2 may need to fetch
  before applying): `StaticAuthApplier` wraps the existing apiKey/bearer path unchanged;
  `OAuth2AuthApplier` attaches the provider's token. The invoker awaits whichever is configured.
- `clientId` / `clientSecret` / `scope` accept `${ENV}` references like every other credential
  field — secrets stay out of config files.
- The token endpoint is called through a **separate** named HttpClient ("auth"), outside the target
  API's resilience pipeline: a flapping upstream must not retry-storm the identity provider.

The model's view is unchanged: no token, no secret, no auth field in any tool schema — the flow is
invisible upstream of the boundary. This is ADR 0001 extended to expiring credentials.

## Consequences

- The bundled DemoApi doubles as an OAuth2 upstream (`/oauth/token` + `DEMO_API_REQUIRE_AUTH=1`),
  so the whole flow is provable offline: without oauth2 config the tool call honestly returns the
  upstream's `HTTP 401`; with it, the server mints the token silently and returns `HTTP 200`
  (`OAuth2Tests`). Cache, expiry-with-skew and `${ENV}` resolution are unit-tested with a fake
  handler and an injected clock — no sleeps.
- Refresh tokens, auth-code flows and per-user identity are out of scope: this server is a
  machine-to-machine boundary; client-credentials is the machine-to-machine grant.

## Alternatives considered

- **Let the agent pass a token argument** — rejected outright: the credential would live in the
  model's context, the opposite of the boundary.
- **DelegatingHandler on the "api" client** — workable, but ties auth to HttpClient plumbing;
  an explicit `IAuthApplier` keeps request-level auth visible, testable and per-config.
- **No cache (token per call)** — rejected: hammers the identity provider and adds latency to
  every tool call for nothing.
