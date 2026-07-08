using System.Net;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using OpenApiMcp.DemoApi;
using OpenApiMcp.Hosting;

namespace OpenApiMcp.Tests;

/// <summary>Unidade do provider: cache, refresh por expiração e resolução ${ENV} — sem rede.</summary>
public sealed class OAuth2TokenProviderTests
{
    /// <summary>Handler fake do token endpoint: conta chamadas e emite tokens sequenciais.</summary>
    sealed class FakeTokenHandler : HttpMessageHandler
    {
        public int Calls;
        public string? LastBody;
        public int ExpiresIn { get; set; } = 3600;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"token-{{Calls}}","token_type":"Bearer","expires_in":{{ExpiresIn}}}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    static AuthOptions OAuth(string? scope = null) => new()
    {
        Type = "oauth2",
        TokenUrl = "http://token.local/oauth/token",
        ClientId = "client-1",
        ClientSecret = "secret-1",
        Scope = scope,
    };

    [Fact]
    public async Task CachesTheToken_TwoCallsOneRequest()
    {
        var handler = new FakeTokenHandler();
        var provider = new OAuth2TokenProvider(new HttpClient(handler), OAuth());

        var a = await provider.GetTokenAsync(CancellationToken.None);
        var b = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", a);
        Assert.Equal(a, b);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RefreshesAfterExpiry_WithSkew()
    {
        var handler = new FakeTokenHandler { ExpiresIn = 60 };
        var now = DateTimeOffset.UtcNow;
        var provider = new OAuth2TokenProvider(new HttpClient(handler), OAuth(), () => now);

        var first = await provider.GetTokenAsync(CancellationToken.None);
        now = now.AddSeconds(29); // ainda dentro de expires_in - 30s de folga
        Assert.Equal(first, await provider.GetTokenAsync(CancellationToken.None));

        now = now.AddSeconds(2); // passou da janela (60 - 30 = 30s)
        var second = await provider.GetTokenAsync(CancellationToken.None);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ResolvesEnvReferences_AndSendsScope()
    {
        Environment.SetEnvironmentVariable("OPENAPI_MCP_TEST_SECRET", "from-env");
        try
        {
            var handler = new FakeTokenHandler();
            var auth = OAuth(scope: "read:posts");
            auth.ClientSecret = "${OPENAPI_MCP_TEST_SECRET}";
            var provider = new OAuth2TokenProvider(new HttpClient(handler), auth);

            await provider.GetTokenAsync(CancellationToken.None);

            Assert.Contains("client_secret=from-env", handler.LastBody);
            Assert.Contains("scope=read%3Aposts", handler.LastBody);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAPI_MCP_TEST_SECRET", null);
        }
    }
}

/// <summary>
/// E2E offline: DemoApi com auth LIGADA como upstream. Sem oauth2 configurado o upstream
/// devolve 401; com oauth2, o servidor adquire o token sozinho e a chamada volta 200 —
/// o agente nunca vê credencial nenhuma.
/// </summary>
public sealed class OAuth2EndToEndTests : IAsyncLifetime
{
    private const string HttpToken = "e2e-http-token";
    private const string ClientSecret = "e2e-client-secret";

    private WebApplication _upstream = null!;

    public async Task InitializeAsync()
    {
        _upstream = DemoApp.Build(requireAuth: true, clientSecret: ClientSecret);
        _upstream.Urls.Add("http://127.0.0.1:0");
        await _upstream.StartAsync();
    }

    public async Task DisposeAsync() => await _upstream.DisposeAsync();

    ServerOptions Options(AuthOptions auth) => new()
    {
        OpenApiPath = Path.Combine(AppContext.BaseDirectory, "demo", "demoapi.openapi.json"),
        BaseUrl = _upstream.Urls.First(),
        Policy = new PolicyOptions { AllowMutating = false, AllowedOperations = ["*"] },
        Auth = auth,
    };

    async Task<string> CallGetPostAsync(ServerOptions options)
    {
        var server = ServerHost.BuildHttp(options, "http://127.0.0.1:0", HttpToken);
        await server.StartAsync();
        try
        {
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Urls.First() + "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {HttpToken}" },
            }));
            var result = await client.CallToolAsync("getPost", new Dictionary<string, object?> { ["id"] = 1 });
            return Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]).Text;
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task WithoutOAuth_UpstreamAnswers401_VisiblyHonest()
    {
        var text = await CallGetPostAsync(Options(new AuthOptions { Type = "none" }));
        Assert.StartsWith("HTTP 401", text); // o caso "where it fails" do README
    }

    [Fact]
    public async Task WithOAuth_ServerAcquiresTokenServerSide_And200s()
    {
        var text = await CallGetPostAsync(Options(new AuthOptions
        {
            Type = "oauth2",
            TokenUrl = _upstream.Urls.First() + "/oauth/token",
            ClientId = "demo-client",
            ClientSecret = ClientSecret,
        }));
        Assert.StartsWith("HTTP 200", text);
        Assert.Contains("synthetic", text);
    }
}
