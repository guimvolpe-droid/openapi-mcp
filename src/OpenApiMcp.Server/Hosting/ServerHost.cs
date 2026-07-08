using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Readers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenApiMcp.Hosting;

/// <summary>
/// Montagem única do servidor — catálogo (OpenAPI→tools sob o policy gate), handlers MCP e
/// invoker resiliente — nos dois transportes. Mesmo comportamento do Program de sempre;
/// fatorado para o transporte HTTP ser testável in-process (porta 0) com um cliente MCP real.
/// </summary>
public static class ServerHost
{
    /// <summary>stdio (default): NADA vai para stdout além do protocolo MCP. Logs → stderr.</summary>
    public static IHost BuildStdio(ServerOptions options)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(options);
        builder.Services.AddHttpClient("api").AddStandardResilienceHandler();
        builder.Services.AddSingleton<ApiToolInvoker>();

        var catalog = BuildCatalog(options);
        ApiToolInvoker invoker = null!;
        WithHandlers(builder.Services.AddMcpServer().WithStdioServerTransport(), catalog, () => invoker);

        var host = builder.Build();
        invoker = host.Services.GetRequiredService<ApiToolInvoker>();
        return host;
    }

    /// <summary>
    /// Streamable HTTP (consumido p. ex. pelo Hub do an internal orchestration system como `a bridge component`).
    /// Loopback por padrão (decisão do chamador via <paramref name="url"/>); Bearer fail-closed:
    /// <paramref name="httpToken"/> nulo/vazio → TODA rota responde 503; token errado → 401.
    /// </summary>
    public static WebApplication BuildHttp(ServerOptions options, string url, string? httpToken)
    {
        var web = WebApplication.CreateBuilder();
        web.WebHost.UseUrls(url);

        web.Services.AddSingleton(options);
        web.Services.AddHttpClient("api").AddStandardResilienceHandler();
        web.Services.AddSingleton<ApiToolInvoker>();

        var catalog = BuildCatalog(options);
        ApiToolInvoker invoker = null!;
        // Stateless: sem session id — requests independentes (cliente simples; nada de estado por sessão aqui).
        WithHandlers(web.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true), catalog, () => invoker);

        var app = web.Build();
        invoker = app.Services.GetRequiredService<ApiToolInvoker>();

        app.Use(async (context, next) =>
        {
            // Fail-closed: sem token configurado, a superfície HTTP fica DESLIGADA (503).
            if (string.IsNullOrEmpty(httpToken))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { ok = false, error = "http_token_not_configured" });
                return;
            }
            var auth = context.Request.Headers.Authorization.ToString();
            var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth["Bearer ".Length..] : "";
            if (token.Length == 0 || !TokensMatch(token, httpToken))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { ok = false, error = "unauthorized" });
                return;
            }
            await next();
        });

        app.MapMcp("/mcp");
        return app;
    }

    /// <summary>OpenAPI → tools MCP, já sob o policy gate (allowlist + verbos mutantes off por padrão).</summary>
    static (Dictionary<string, GeneratedTool> Index, List<Tool> McpTools) BuildCatalog(ServerOptions options)
    {
        var openApiText = File.ReadAllText(options.OpenApiPath);
        var doc = new OpenApiStringReader().Read(openApiText, out _);
        var generated = new OpenApiToolFactory().Create(doc, options.Policy);
        var index = generated.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var mcpTools = generated
            .Select(t => new Tool { Name = t.Name, Description = t.Description, InputSchema = t.InputSchema })
            .ToList();
        return (index, mcpTools);
    }

    /// <summary>Handlers compartilhados entre stdio e HTTP (fonte única do comportamento MCP).</summary>
    static IMcpServerBuilder WithHandlers(
        IMcpServerBuilder mcp,
        (Dictionary<string, GeneratedTool> Index, List<Tool> McpTools) catalog,
        Func<ApiToolInvoker> invoker) => mcp
        .WithListToolsHandler((ctx, ct) =>
            ValueTask.FromResult(new ListToolsResult { Tools = catalog.McpTools }))
        .WithCallToolHandler(async (ctx, ct) =>
        {
            var name = ctx.Params?.Name ?? "";
            if (!catalog.Index.TryGetValue(name, out var tool))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"Unknown or disallowed tool: '{name}'." }],
                };
            }

            var text = await invoker().InvokeAsync(tool, ctx.Params?.Arguments, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
        });

    // Comparação em tempo constante sobre digests (comprimentos sempre iguais → sem vazamento por tamanho).
    static bool TokensMatch(string a, string b)
    {
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}
