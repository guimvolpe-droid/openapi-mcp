using System.Text.Json;
using Microsoft.OpenApi.Models;

namespace OpenApiMcp;

/// <summary>Uma operação OpenAPI transformada em ferramenta MCP + o metadata para despachá-la.</summary>
public sealed record GeneratedTool(
    string Name,
    string Description,
    JsonElement InputSchema,
    string Method,
    string PathTemplate,
    IReadOnlyList<ToolParam> Parameters,
    bool HasBody);

/// <summary>Um parâmetro da operação e onde ele entra na requisição HTTP.</summary>
public sealed record ToolParam(string Name, ParameterLocation In);
