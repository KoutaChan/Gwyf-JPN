namespace GwyfJpn.Core;

/// <summary>
/// Generates intentionally long ASCII pseudo-localized text for runtime replacement checks.
/// </summary>
public static class PseudoLocalizer
{
    /// <summary>
    /// Repeats the source inside a marker so UI replacement and layout stress are visible.
    /// </summary>
    public static string ToLongPseudo(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        return $"[JP-LONG {source} {source}]";
    }
}
