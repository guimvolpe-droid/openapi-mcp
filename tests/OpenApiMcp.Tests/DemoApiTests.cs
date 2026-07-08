using System.Net;
using System.Net.Http.Json;
using OpenApiMcp.DemoApi;

namespace OpenApiMcp.Tests;

/// <summary>
/// Sobe a DemoApi in-process na porta 0 e exercita a superfície que o servidor MCP consome.
/// É o upstream offline de todos os testes E2E — nada aqui toca a rede externa.
/// </summary>
public sealed class DemoApiTests : IAsyncLifetime
{
    private Microsoft.AspNetCore.Builder.WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = DemoApp.Build();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetPost_ReturnsSeededPost()
    {
        var res = await _client.GetAsync("/posts/1");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var post = await res.Content.ReadFromJsonAsync<Post>();
        Assert.NotNull(post);
        Assert.Equal(1, post!.Id);
        Assert.Contains("synthetic", post.Title);
    }

    [Fact]
    public async Task GetPost_UnknownId_Returns404()
    {
        var res = await _client.GetAsync("/posts/999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task ListPosts_FiltersByUserId()
    {
        var posts = await _client.GetFromJsonAsync<List<Post>>("/posts?userId=1");
        Assert.NotNull(posts);
        Assert.NotEmpty(posts!);
        Assert.All(posts!, p => Assert.Equal(1, p.UserId));
    }

    [Fact]
    public async Task ListPosts_Paginated_EmitsLinkNextHeader()
    {
        var res = await _client.GetAsync("/posts?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var posts = await res.Content.ReadFromJsonAsync<List<Post>>();
        Assert.Equal(5, posts!.Count);

        Assert.True(res.Headers.TryGetValues("Link", out var links));
        var link = Assert.Single(links!);
        Assert.Contains("rel=\"next\"", link);
        Assert.Contains("page=2", link);
    }

    [Fact]
    public async Task ListPosts_LastPage_HasNoLinkHeader()
    {
        var res = await _client.GetAsync($"/posts?page=1&pageSize={DemoApp.SeedCount}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(res.Headers.Contains("Link"));
    }
}
