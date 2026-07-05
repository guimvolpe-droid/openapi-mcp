namespace OpenApiMcp;

/// <summary>Configuração do servidor: onde está o OpenAPI, a API alvo, a política e o auth.</summary>
public sealed class ServerOptions
{
    public string OpenApiPath { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public PolicyOptions Policy { get; set; } = new();
    public AuthOptions Auth { get; set; } = new();
}

/// <summary>O policy gate: o que o agente pode chamar. Padrão seguro = só leitura.</summary>
public sealed class PolicyOptions
{
    /// <summary>Habilita verbos mutantes (POST/PUT/PATCH/DELETE). Padrão: false.</summary>
    public bool AllowMutating { get; set; }

    /// <summary>Allowlist de operações por nome. "*" libera todas (ainda sujeitas a AllowMutating).</summary>
    public List<string> AllowedOperations { get; set; } = new() { "*" };

    public bool IsAllowed(string operationName, bool isMutating)
    {
        if (isMutating && !AllowMutating) return false;
        if (AllowedOperations is null || AllowedOperations.Count == 0) return true;
        if (AllowedOperations.Contains("*")) return true;
        return AllowedOperations.Contains(operationName, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Auth injetada na requisição de saída — nunca exposta ao agente.</summary>
public sealed class AuthOptions
{
    /// <summary>none | apiKey | bearer</summary>
    public string Type { get; set; } = "none";

    /// <summary>Nome do header (para apiKey).</summary>
    public string? HeaderName { get; set; }

    /// <summary>Valor literal ou referência a env var no formato ${NOME}.</summary>
    public string? Value { get; set; }

    public string? ResolveValue()
    {
        if (string.IsNullOrEmpty(Value)) return Value;
        if (Value.StartsWith("${") && Value.EndsWith("}"))
            return Environment.GetEnvironmentVariable(Value[2..^1]);
        return Value;
    }

    public void Apply(HttpRequestMessage request)
    {
        var v = ResolveValue();
        if (string.IsNullOrEmpty(v)) return;
        switch (Type?.ToLowerInvariant())
        {
            case "apikey":
                if (!string.IsNullOrEmpty(HeaderName))
                    request.Headers.TryAddWithoutValidation(HeaderName, v);
                break;
            case "bearer":
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + v);
                break;
        }
    }
}
