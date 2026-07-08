using System.Text.Json;

namespace OpenApiMcp;

/// <summary>
/// Adquire e cacheia o token OAuth2 (grant client_credentials) server-side. O token nunca
/// aparece em schema/list/call — extensão natural do ADR 0001 (MCP como boundary).
/// Cache até expires_in menos uma folga; renovação serializada por semáforo (sem corrida).
/// </summary>
public sealed class OAuth2TokenProvider
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly AuthOptions _auth;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OAuth2TokenProvider(HttpClient http, AuthOptions auth, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _auth = auth;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && _clock() < _expiresAt) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && _clock() < _expiresAt) return _token; // outro caller já renovou

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = AuthOptions.Resolve(_auth.ClientId) ?? "",
                ["client_secret"] = AuthOptions.Resolve(_auth.ClientSecret) ?? "",
            };
            var scope = AuthOptions.Resolve(_auth.Scope);
            if (!string.IsNullOrEmpty(scope)) form["scope"] = scope;

            var tokenUrl = _auth.TokenUrl
                ?? throw new InvalidOperationException("auth.type=oauth2 requires auth.tokenUrl");
            using var res = await _http.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"token endpoint answered HTTP {(int)res.StatusCode}: {body}");

            using var json = JsonDocument.Parse(body);
            var token = json.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("token endpoint returned no access_token");
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

            _token = token;
            _expiresAt = _clock() + TimeSpan.FromSeconds(expiresIn) - Skew;
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }
}
