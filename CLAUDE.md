# openapi-mcp — Regras do repo

Peça de portfólio do carreira-os, em **C# / .NET 8** (SDK oficial `ModelContextProtocol`):
transforma qualquer documento OpenAPI/Swagger em tools MCP tipadas, com o servidor como
**fronteira de segurança** — policy gate (allowlist de operações; verbos mutantes OFF por
default, ADR `docs/adr/0001-mcp-as-a-boundary.md`), auth injetada server-side (API key/bearer/
OAuth2 client-credentials, nunca no schema nem devolvida ao modelo), HTTP resiliente (Polly:
retry/timeout/circuit-breaker) e response shaping (cap de body + hint de paginação).

## Comandos

- `dotnet test` (unit + E2E in-process do transporte HTTP) · `dotnet build OpenApiMcp.sln`.
- Demo E2E offline: `docker compose up` (demo API sintética embutida, sem dependência externa).
- **WSL**: SDK em `~/.dotnet` sem libicu → exportar `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

## Gates (só o dono decide)

- Publicação/divulgação, uso em candidatura, qualquer credencial real de API upstream.
- Idioma do repo é EN (portfólio) — manter README/docs/commits deste repo em inglês.

## Artefatos: 3 destinos <!-- origem: ~/projects/CLAUDE.md · v1 · copiado 2026-07-28 -->

- Arquivo gerado (screenshot, dump, export, peça em rascunho) NUNCA na raiz: lixo → `descarte/`
  (gitignored, só o dono apaga) · reutilizável fora de uso → `bkp/AAAA-MM-<slug>/` (gitignored,
  indexado em `bkp/LEIA-ME.md`) · versão FINAL → caminho canônico, nome estável (sem -v2/-final).
- MDs de estado guardam SÓ estado final (sem "era X virou Y"); contradição = corrigir na hora.
