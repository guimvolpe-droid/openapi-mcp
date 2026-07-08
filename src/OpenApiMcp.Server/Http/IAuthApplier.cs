namespace OpenApiMcp;

/// <summary>
/// Aplica a credencial de saída na requisição HTTP — sempre server-side, nunca no agente.
/// Assíncrono porque o oauth2 pode precisar buscar/renovar token antes de aplicar.
/// </summary>
public interface IAuthApplier
{
    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct);
}

/// <summary>apiKey/bearer/none — aplica o valor estático resolvido (${ENV} suportado).</summary>
public sealed class StaticAuthApplier : IAuthApplier
{
    private readonly AuthOptions _auth;

    public StaticAuthApplier(AuthOptions auth) => _auth = auth;

    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _auth.Apply(request);
        return ValueTask.CompletedTask;
    }
}

/// <summary>oauth2 client-credentials — Bearer com token adquirido/cacheado pelo provider.</summary>
public sealed class OAuth2AuthApplier : IAuthApplier
{
    private readonly OAuth2TokenProvider _provider;

    public OAuth2AuthApplier(OAuth2TokenProvider provider) => _provider = provider;

    public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _provider.GetTokenAsync(ct);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
    }
}
