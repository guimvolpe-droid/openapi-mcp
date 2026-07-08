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
using OpenApiMcp;

// openapi-mcp — servidor MCP que expõe uma API REST (via OpenAPI) como ferramentas MCP seguras.
// Dois transportes, MESMO catálogo/policy/invoker:
//   default            → stdio (Claude Desktop/CLI spawna o processo)
//   OPENAPI_MCP_HTTP=1 → Streamable HTTP em http://127.0.0.1:$OPENAPI_MCP_HTTP_PORT/mcp
//                        (loopback only; Bearer OPENAPI_MCP_HTTP_TOKEN fail-closed — sem o
//                        token no env, TODA rota responde 503; errado → 401)

// 1) Config + documento OpenAPI
var configPath = Environment.GetEnvironmentVariable("OPENAPI_MCP_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "demo", "config.json");
var options = ConfigLoader.Load(configPath);
var openApiText = File.ReadAllText(options.OpenApiPath);
var doc = new OpenApiStringReader().Read(openApiText, out _);

// 2) Gera as tools aplicando o policy gate (allowlist + verbos mutantes off por padrão)
var generated = new OpenApiToolFactory().Create(doc, options.Policy);
var index = generated.ToDictionary(t => t.Name, StringComparer.Ordinal);
var mcpTools = generated
    .Select(t => new Tool { Name = t.Name, Description = t.Description, InputSchema = t.InputSchema })
    .ToList();

ApiToolInvoker invoker = null!;

// Handlers compartilhados entre stdio e HTTP (fonte única do comportamento MCP).
IMcpServerBuilder WithHandlers(IMcpServerBuilder mcp) => mcp
    .WithListToolsHandler((ctx, ct) =>
        ValueTask.FromResult(new ListToolsResult { Tools = mcpTools }))
    .WithCallToolHandler(async (ctx, ct) =>
    {
        var name = ctx.Params?.Name ?? "";
        if (!index.TryGetValue(name, out var tool))
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Unknown or disallowed tool: '{name}'." }],
            };
        }

        var text = await invoker.InvokeAsync(tool, ctx.Params?.Arguments, ct);
        return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
    });

if (Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP") == "1")
{
    // ---- Streamable HTTP (consumido p. ex. pelo Hub do an internal orchestration system como `a bridge component`) ----
    var web = WebApplication.CreateBuilder(args);
    var port = Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP_PORT") ?? "8410";
    // Loopback ONLY: exposição além da máquina é decisão explícita de deploy (túnel/proxy com TLS).
    web.WebHost.UseUrls($"http://127.0.0.1:{port}");

    web.Services.AddSingleton(options);
    web.Services.AddHttpClient("api").AddStandardResilienceHandler();
    web.Services.AddSingleton<ApiToolInvoker>();
    // Stateless: sem session id — requests independentes (cliente simples; nada de estado por sessão aqui).
    WithHandlers(web.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true));

    var app = web.Build();
    invoker = app.Services.GetRequiredService<ApiToolInvoker>();

    var httpToken = Environment.GetEnvironmentVariable("OPENAPI_MCP_HTTP_TOKEN");
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
    await app.RunAsync();
}
else
{
    // ---- stdio (default; comportamento original intocado) ----
    var builder = Host.CreateApplicationBuilder(args);

    // stdio: NADA vai para stdout além do protocolo MCP. Logs -> stderr.
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddSingleton(options);
    builder.Services.AddHttpClient("api").AddStandardResilienceHandler();
    builder.Services.AddSingleton<ApiToolInvoker>();

    WithHandlers(builder.Services.AddMcpServer().WithStdioServerTransport());

    var host = builder.Build();
    invoker = host.Services.GetRequiredService<ApiToolInvoker>();
    await host.RunAsync();
}

// Comparação em tempo constante sobre digests (comprimentos sempre iguais → sem vazamento por tamanho).
static bool TokensMatch(string a, string b)
{
    var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
    var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
    return CryptographicOperations.FixedTimeEquals(ha, hb);
}
