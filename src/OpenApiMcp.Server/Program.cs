using Microsoft.Extensions.Hosting;
using OpenApiMcp;
using OpenApiMcp.Hosting;

// openapi-mcp — servidor MCP que expõe uma API REST (via OpenAPI) como ferramentas MCP seguras.
// Dois transportes, MESMO catálogo/policy/invoker (montagem em Hosting/ServerHost.cs):
//   default            → stdio (Claude Desktop/CLI spawna o processo)
//   OPENAPI_MCP_HTTP=1 → Streamable HTTP em http://127.0.0.1:$OPENAPI_MCP_HTTP_PORT/mcp
//                        (loopback only; Bearer OPENAPI_MCP_HTTP_TOKEN fail-closed — sem o
//                        token no env, TODA rota responde 503; errado → 401)
//   OPENAPI_MCP_HTTP_URL → override completo do bind (ex.: http://0.0.0.0:8410 DENTRO de um
//                          container cujo port-publish já restringe ao host). Default: loopback.

var configPath = Environment.GetEnvironmentVariable("OPENAPI_MCP_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "demo", "config.json");
var options = ConfigLoader.Load(configPath);

if (Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP") == "1")
{
    var port = Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP_PORT") ?? "8410";
    // Loopback ONLY por padrão: exposição além da máquina é decisão explícita de deploy
    // (túnel/reverse-proxy com TLS, ou OPENAPI_MCP_HTTP_URL + port-publish restrito).
    var url = Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP_URL") ?? $"http://127.0.0.1:{port}";
    var token = Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP_TOKEN");
    await ServerHost.BuildHttp(options, url, token).RunAsync();
}
else
{
    await ServerHost.BuildStdio(options).RunAsync();
}
