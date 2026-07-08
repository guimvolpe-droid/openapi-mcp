using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using OpenApiMcp.DemoApi;
using OpenApiMcp.Hosting;

namespace OpenApiMcp.Tests;

/// <summary>
/// Prova o transporte Streamable HTTP de ponta a ponta, 100% in-process e offline:
/// DemoApi (porta 0) ← ApiToolInvoker ← servidor MCP HTTP (porta 0) ← McpClient REAL do SDK.
/// O policy gate e o Bearer fail-closed valem no transporte novo igual valem no stdio.
/// </summary>
public sealed class HttpTransportTests : IAsyncLifetime
{
    private const string Token = "test-token-for-http-transport";

    private WebApplication _upstream = null!;
    private WebApplication _server = null!;
    private string _mcpUrl = null!;

    public async Task InitializeAsync()
    {
        _upstream = DemoApp.Build();
        _upstream.Urls.Add("http://127.0.0.1:0");
        await _upstream.StartAsync();

        _server = ServerHost.BuildHttp(DemoOptions(_upstream.Urls.First()), "http://127.0.0.1:0", Token);
        await _server.StartAsync();
        _mcpUrl = _server.Urls.First() + "/mcp";
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        await _upstream.DisposeAsync();
    }

    static ServerOptions DemoOptions(string baseUrl) => new()
    {
        OpenApiPath = Path.Combine(AppContext.BaseDirectory, "demo", "demoapi.openapi.json"),
        BaseUrl = baseUrl,
        Policy = new PolicyOptions { AllowMutating = false, AllowedOperations = ["*"] },
    };

    async Task<McpClient> ConnectAsync() =>
        await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_mcpUrl),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" },
        }));

    [Fact]
    public async Task ListTools_OverHttp_PolicyGateStillHoldsBackMutations()
    {
        await using var client = await ConnectAsync();
        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("getPost", names);
        Assert.Contains("listPosts", names);
        Assert.DoesNotContain("createPost", names); // POST + allowMutating=false ⇒ nunca exposto
    }

    [Fact]
    public async Task CallTool_OverHttp_ReachesTheRealUpstream()
    {
        await using var client = await ConnectAsync();
        var result = await client.CallToolAsync("getPost", new Dictionary<string, object?> { ["id"] = 1 });

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]).Text;
        Assert.StartsWith("HTTP 200", text);
        Assert.Contains("synthetic", text);
    }

    [Fact]
    public async Task WrongBearerToken_Answers401()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        var res = await http.PostAsync(_mcpUrl, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task NoTokenConfigured_FailsClosedWith503()
    {
        var closed = ServerHost.BuildHttp(DemoOptions(_upstream.Urls.First()), "http://127.0.0.1:0", httpToken: null);
        await closed.StartAsync();
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            var res = await http.PostAsync(closed.Urls.First() + "/mcp",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        }
        finally
        {
            await closed.DisposeAsync();
        }
    }
}
