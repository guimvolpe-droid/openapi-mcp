using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace OpenApiMcp.DemoApi;

public sealed record Post(int Id, int UserId, string Title, string Body);

/// <summary>
/// Fábrica da app de demo: usada pelo Program (porta do ASPNETCORE_URLS) e pelos testes
/// (porta 0 in-process). Seed determinístico — mesmas respostas em qualquer máquina.
/// </summary>
public static class DemoApp
{
    public const int SeedCount = 25;

    public static WebApplication Build(string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        var app = builder.Build();
        var posts = Seed();

        // GET /posts — filtro por userId + paginação page/pageSize com Link rel="next".
        app.MapGet("/posts", (HttpContext ctx, int? userId, int? page, int? pageSize) =>
        {
            IEnumerable<Post> result = posts;
            if (userId is int uid) result = result.Where(p => p.UserId == uid);

            var all = result.ToList();
            if (page is null && pageSize is null) return Results.Ok(all);

            var size = Math.Clamp(pageSize ?? 10, 1, 100);
            var current = Math.Max(page ?? 1, 1);
            var slice = all.Skip((current - 1) * size).Take(size).ToList();

            if (current * size < all.Count)
            {
                var req = ctx.Request;
                var baseUrl = $"{req.Scheme}://{req.Host}{req.Path}";
                var qs = $"page={current + 1}&pageSize={size}" + (userId is int u ? $"&userId={u}" : "");
                ctx.Response.Headers.Append("Link", $"<{baseUrl}?{qs}>; rel=\"next\"");
            }
            return Results.Ok(slice);
        });

        app.MapGet("/posts/{id:int}", (int id) =>
            posts.FirstOrDefault(p => p.Id == id) is { } post
                ? Results.Ok(post)
                : Results.NotFound(new { error = $"post {id} not found" }));

        // Mutante de propósito: existe para o policy gate do servidor MCP ter o que bloquear.
        app.MapPost("/posts", (Post input) =>
            Results.Created($"/posts/{SeedCount + 1}", input with { Id = SeedCount + 1 }));

        return app;
    }

    static List<Post> Seed() =>
        Enumerable.Range(1, SeedCount)
            .Select(i => new Post(
                Id: i,
                UserId: ((i - 1) % 5) + 1,
                Title: $"Post {i}: notes on synthetic data",
                Body: $"Deterministic body for post {i}. Same content on every machine, no network needed."))
            .ToList();
}
