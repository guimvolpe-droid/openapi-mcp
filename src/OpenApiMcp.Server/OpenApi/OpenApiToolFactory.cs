using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Models;

namespace OpenApiMcp;

/// <summary>
/// Gera ferramentas MCP a partir de um documento OpenAPI, aplicando o policy gate.
/// Cada operação vira uma tool com input schema JSON derivado de parâmetros + requestBody.
/// </summary>
public sealed class OpenApiToolFactory
{
    public IReadOnlyList<GeneratedTool> Create(OpenApiDocument doc, PolicyOptions policy)
    {
        var tools = new List<GeneratedTool>();
        if (doc.Paths is null) return tools;

        foreach (var (path, item) in doc.Paths)
        {
            if (item.Operations is null) continue;
            foreach (var (opType, op) in item.Operations)
            {
                var method = opType.ToString().ToUpperInvariant();
                var isMutating = method is "POST" or "PUT" or "PATCH" or "DELETE";
                var name = BuildName(op, opType, path);

                // policy gate: allowlist + verbos mutantes off por padrão
                if (!policy.IsAllowed(name, isMutating)) continue;

                var (schemaNode, parameters, hasBody) = BuildInput(op, item);
                var schema = JsonSerializer.SerializeToElement(schemaNode);
                var desc = FirstNonEmpty(op.Summary, op.Description) ?? $"{method} {path}";
                tools.Add(new GeneratedTool(name, desc, schema, method, path, parameters, hasBody));
            }
        }
        return tools;
    }

    static string BuildName(OpenApiOperation op, OperationType type, string path)
    {
        if (!string.IsNullOrWhiteSpace(op.OperationId)) return Sanitize(op.OperationId!);
        var slug = path.Trim('/').Replace('/', '_').Replace("{", "").Replace("}", "");
        return Sanitize($"{type.ToString().ToLowerInvariant()}_{slug}");
    }

    static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());

    static (JsonObject schema, List<ToolParam> parameters, bool hasBody) BuildInput(
        OpenApiOperation op, OpenApiPathItem item)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        var parameters = new List<ToolParam>();

        var all = new List<OpenApiParameter>();
        if (item.Parameters is not null) all.AddRange(item.Parameters);
        if (op.Parameters is not null) all.AddRange(op.Parameters);

        foreach (var p in all)
        {
            if (p.In is null || string.IsNullOrEmpty(p.Name)) continue;
            var ps = ConvertSchema(p.Schema);
            if (!string.IsNullOrEmpty(p.Description)) ps["description"] = p.Description;
            props[p.Name] = ps;
            if (p.Required) required.Add(p.Name);
            parameters.Add(new ToolParam(p.Name, p.In.Value));
        }

        var hasBody = false;
        var rb = op.RequestBody;
        if (rb?.Content is { Count: > 0 } content)
        {
            var media = content.TryGetValue("application/json", out var mj) ? mj : content.Values.First();
            if (media?.Schema is not null)
            {
                props["body"] = ConvertSchema(media.Schema);
                if (rb.Required) required.Add("body");
                hasBody = true;
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false,
        };
        if (required.Count > 0) schema["required"] = required;
        return (schema, parameters, hasBody);
    }

    static JsonObject ConvertSchema(OpenApiSchema? s, int depth = 0)
    {
        var o = new JsonObject();
        if (s is null || depth > 6)
        {
            o["type"] = "string";
            return o;
        }
        if (!string.IsNullOrEmpty(s.Type)) o["type"] = s.Type;
        if (!string.IsNullOrEmpty(s.Format)) o["format"] = s.Format;
        if (!string.IsNullOrEmpty(s.Description)) o["description"] = s.Description;

        if (string.Equals(s.Type, "array", StringComparison.OrdinalIgnoreCase) && s.Items is not null)
            o["items"] = ConvertSchema(s.Items, depth + 1);

        if (s.Properties is { Count: > 0 })
        {
            var p = new JsonObject();
            foreach (var (k, v) in s.Properties) p[k] = ConvertSchema(v, depth + 1);
            o["properties"] = p;
            if (string.IsNullOrEmpty(s.Type)) o["type"] = "object";
        }

        if (o.Count == 0) o["type"] = "string";
        return o;
    }

    static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
