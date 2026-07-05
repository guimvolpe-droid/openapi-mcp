using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// openapi-mcp — servidor MCP que expõe uma API REST (via OpenAPI) como ferramentas MCP seguras.
// Slice inicial: servidor stdio funcional com uma tool de health-check. As tools dinâmicas
// geradas do documento OpenAPI + o policy gate entram nos próximos commits.
var builder = Host.CreateApplicationBuilder(args);

// stdio: NADA pode ir para stdout além do protocolo MCP. Logs vão para stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class HealthTool
{
    [McpServerTool, Description("Health check do servidor openapi-mcp. Retorna 'pong'.")]
    public static string Ping() => "pong";
}
