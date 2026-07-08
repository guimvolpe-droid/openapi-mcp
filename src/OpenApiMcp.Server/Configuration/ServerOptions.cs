namespace OpenApiMcp;

/// <summary>Configuração do servidor: onde está o OpenAPI, a API alvo, a política e o auth.</summary>
public sealed class ServerOptions
{
    public string OpenApiPath { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public PolicyOptions Policy { get; set; } = new();
    public AuthOptions Auth { get; set; } = new();
    public ResponseOptions Response { get; set; } = new();
}

/// <summary>Moldagem da resposta devolvida ao modelo (ver ResponseShaper).</summary>
public sealed class ResponseOptions
{
    /// <summary>Teto do corpo em caracteres; acima disso trunca com marcador explícito.</summary>
    public int MaxBodyChars { get; set; } = ResponseShaper.DefaultMaxBodyChars;
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
    /// <summary>none | apiKey | bearer | oauth2 (client-credentials)</summary>
    public string Type { get; set; } = "none";

    /// <summary>Nome do header (para apiKey).</summary>
    public string? HeaderName { get; set; }

    /// <summary>Valor literal ou referência a env var no formato ${NOME}.</summary>
    public string? Value { get; set; }

    /// <summary>oauth2: endpoint de token (grant client_credentials, form-urlencoded).</summary>
    public string? TokenUrl { get; set; }

    /// <summary>oauth2: client id (aceita ${ENV}).</summary>
    public string? ClientId { get; set; }

    /// <summary>oauth2: client secret (aceita ${ENV} — mantenha fora do arquivo).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>oauth2: scope opcional.</summary>
    public string? Scope { get; set; }

    /// <summary>Resolve referências ${ENV} em qualquer campo de credencial.</summary>
    public static string? Resolve(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("${") && value.EndsWith("}"))
            return Environment.GetEnvironmentVariable(value[2..^1]);
        return value;
    }

    public string? ResolveValue() => Resolve(Value);

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
