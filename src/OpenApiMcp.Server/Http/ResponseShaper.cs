using System.Text;
using System.Text.RegularExpressions;

namespace OpenApiMcp;

/// <summary>
/// Molda a resposta HTTP para o modelo: status na 1ª linha (formato de sempre), corpo com teto
/// de tamanho (respostas gigantes viram truncamento explícito, nunca contexto estourado em
/// silêncio) e hint de paginação extraído do header Link rel="next" — o modelo descobre que há
/// mais páginas sem precisar adivinhar. Puro e determinístico: testável sem rede.
/// </summary>
public sealed class ResponseShaper
{
    public const int DefaultMaxBodyChars = 16000;

    private static readonly Regex NextLink = new(
        "<(?<url>[^>]+)>\\s*;[^,]*\\brel=\"?next\"?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly int _maxBodyChars;

    public ResponseShaper(int maxBodyChars = DefaultMaxBodyChars) =>
        _maxBodyChars = maxBodyChars > 0 ? maxBodyChars : DefaultMaxBodyChars;

    public string Shape(int statusCode, string? reasonPhrase, string body, IEnumerable<string>? linkHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP ").Append(statusCode).Append(' ').Append(reasonPhrase).Append('\n');

        if (body.Length <= _maxBodyChars)
        {
            sb.Append(body);
        }
        else
        {
            sb.Append(body, 0, _maxBodyChars);
            sb.Append("\n[truncated: showing ").Append(_maxBodyChars).Append(" of ").Append(body.Length).Append(" chars]");
        }

        if (ParseNextLink(linkHeaders) is { } next)
            sb.Append("\n[more results: ").Append(next).Append(']');

        return sb.ToString();
    }

    /// <summary>Primeira URL com rel="next" nos headers Link (múltiplos links por header suportados).</summary>
    public static string? ParseNextLink(IEnumerable<string>? linkHeaders)
    {
        if (linkHeaders is null) return null;
        foreach (var header in linkHeaders)
        {
            var m = NextLink.Match(header);
            if (m.Success) return m.Groups["url"].Value;
        }
        return null;
    }
}
