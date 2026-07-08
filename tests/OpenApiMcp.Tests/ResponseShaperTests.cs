using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using OpenApiMcp.DemoApi;
using OpenApiMcp.Hosting;

namespace OpenApiMcp.Tests;

public sealed class ResponseShaperTests
{
    [Fact]
    public void BodyUnderTheCap_PassesUntouched()
    {
        var shaper = new ResponseShaper(maxBodyChars: 100);
        var text = shaper.Shape(200, "OK", "small body");
        Assert.Equal("HTTP 200 OK\nsmall body", text);
    }

    [Fact]
    public void BodyAtTheExactBoundary_IsNotTruncated()
    {
        var body = new string('x', 50);
        var text = new ResponseShaper(50).Shape(200, "OK", body);
        Assert.DoesNotContain("[truncated", text);
        Assert.EndsWith(body, text);
    }

    [Fact]
    public void BodyOverTheCap_TruncatesWithExplicitMarker()
    {
        var body = new string('x', 120);
        var text = new ResponseShaper(50).Shape(200, "OK", body);
        Assert.Contains(new string('x', 50), text);
        Assert.DoesNotContain(new string('x', 51), text);
        Assert.EndsWith("[truncated: showing 50 of 120 chars]", text);
    }

    [Fact]
    public void NonJsonBody_IsJustText()
    {
        var text = new ResponseShaper(100).Shape(500, "Internal Server Error", "<html>boom</html>");
        Assert.Equal("HTTP 500 Internal Server Error\n<html>boom</html>", text);
    }

    [Fact]
    public void LinkNext_BecomesAMoreResultsHint()
    {
        var text = new ResponseShaper(100).Shape(200, "OK", "[]",
            new[] { "<http://api.local/posts?page=2&pageSize=5>; rel=\"next\"" });
        Assert.EndsWith("[more results: http://api.local/posts?page=2&pageSize=5]", text);
    }

    [Fact]
    public void MultipleRels_OnlyNextIsPicked()
    {
        var next = ResponseShaper.ParseNextLink(new[]
        {
            "<http://api.local/x?page=1>; rel=\"prev\", <http://api.local/x?page=3>; rel=\"next\"",
        });
        Assert.Equal("http://api.local/x?page=3", next);
    }

    [Fact]
    public void NoLinkHeader_NoHint()
    {
        var text = new ResponseShaper(100).Shape(200, "OK", "[]");
        Assert.DoesNotContain("[more results:", text);
    }
}

/// <summary>E2E offline: a paginação do DemoApi vira hint visível na resposta da tool.</summary>
public sealed class ResponseShapingEndToEndTests
{
    [Fact]
    public async Task ListPosts_Paginated_ToolResponseCarriesTheNextPageHint()
    {
        var upstream = DemoApp.Build();
        upstream.Urls.Add("http://127.0.0.1:0");
        await upstream.StartAsync();

        var server = ServerHost.BuildHttp(new ServerOptions
        {
            OpenApiPath = Path.Combine(AppContext.BaseDirectory, "demo", "demoapi.openapi.json"),
            BaseUrl = upstream.Urls.First(),
            Policy = new PolicyOptions { AllowMutating = false, AllowedOperations = ["*"] },
        }, "http://127.0.0.1:0", "shaping-token");
        await server.StartAsync();

        try
        {
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Urls.First() + "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer shaping-token" },
            }));

            var result = await client.CallToolAsync("listPosts",
                new Dictionary<string, object?> { ["page"] = 1, ["pageSize"] = 5 });
            var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]).Text;

            Assert.StartsWith("HTTP 200", text);
            Assert.Contains("[more results:", text);
            Assert.Contains("page=2", text);
        }
        finally
        {
            await server.DisposeAsync();
            await upstream.DisposeAsync();
        }
    }
}
