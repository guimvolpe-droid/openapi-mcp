using System.Text;
using System.Text.Json;
using Microsoft.OpenApi.Models;

namespace OpenApiMcp;

/// <summary>
/// Despacha uma chamada de ferramenta para a API downstream: monta a requisição
/// (path/query/header + body), injeta auth e envia por um HttpClient resiliente.
/// A segurança (allowlist/verbo) já foi aplicada na geração da tool; o invoker é o transporte.
/// </summary>
public sealed class ApiToolInvoker
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ServerOptions _options;
    private readonly IAuthApplier _auth;
    private readonly ResponseShaper _shaper;

    public ApiToolInvoker(IHttpClientFactory httpFactory, ServerOptions options, IAuthApplier auth)
    {
        _httpFactory = httpFactory;
        _options = options;
        _auth = auth;
        _shaper = new ResponseShaper(options.Response.MaxBodyChars);
    }

    public async Task<string> InvokeAsync(
        GeneratedTool tool,
        IDictionary<string, JsonElement>? args,
        CancellationToken ct)
    {
        var path = tool.PathTemplate;
        var query = new List<string>();
        var headers = new List<(string Name, string Value)>();

        foreach (var p in tool.Parameters)
        {
            if (args is null || !args.TryGetValue(p.Name, out var val)) continue;
            var s = ValueToString(val);
            switch (p.In)
            {
                case ParameterLocation.Path:
                    path = path.Replace("{" + p.Name + "}", Uri.EscapeDataString(s));
                    break;
                case ParameterLocation.Query:
                    query.Add($"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(s)}");
                    break;
                case ParameterLocation.Header:
                    headers.Add((p.Name, s));
                    break;
            }
        }

        var url = _options.BaseUrl.TrimEnd('/') + path;
        if (query.Count > 0) url += "?" + string.Join("&", query);

        using var request = new HttpRequestMessage(new HttpMethod(tool.Method), url);
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);

        if (tool.HasBody && args is not null && args.TryGetValue("body", out var body))
            request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");

        // auth de saída — nunca chega ao agente (oauth2 busca/renova token aqui, server-side)
        await _auth.ApplyAsync(request, ct);

        var client = _httpFactory.CreateClient("api");
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.Headers.TryGetValues("Link", out var linkHeaders);
        return _shaper.Shape((int)response.StatusCode, response.ReasonPhrase, text, linkHeaders);
    }

    static string ValueToString(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText();
}
