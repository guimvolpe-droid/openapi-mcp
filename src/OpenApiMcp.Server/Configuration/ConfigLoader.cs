using System.Text.Json;

namespace OpenApiMcp;

public static class ConfigLoader
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lê o config JSON e resolve OpenApiPath relativo ao diretório do config.</summary>
    public static ServerOptions Load(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var options = JsonSerializer.Deserialize<ServerOptions>(json, Json) ?? new ServerOptions();

        if (!string.IsNullOrEmpty(options.OpenApiPath) && !Path.IsPathRooted(options.OpenApiPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
            options.OpenApiPath = Path.Combine(dir, options.OpenApiPath);
        }
        return options;
    }
}
