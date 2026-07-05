using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Readers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenApiMcp;

// openapi-mcp — servidor MCP que expõe uma API REST (via OpenAPI) como ferramentas MCP seguras.
var builder = Host.CreateApplicationBuilder(args);

// stdio: NADA vai para stdout além do protocolo MCP. Logs -> stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

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

// 3) HTTP resiliente (Polly) + invoker (auth injetada aqui, nunca no agente)
builder.Services.AddSingleton(options);
builder.Services.AddHttpClient("api").AddStandardResilienceHandler();
builder.Services.AddSingleton<ApiToolInvoker>();

ApiToolInvoker invoker = null!;

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
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

var host = builder.Build();
invoker = host.Services.GetRequiredService<ApiToolInvoker>();
await host.RunAsync();
