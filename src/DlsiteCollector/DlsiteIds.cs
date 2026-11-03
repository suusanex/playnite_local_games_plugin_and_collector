using System.Text.RegularExpressions;

namespace DlsiteCollector;

/// <summary>
/// DLsite 作品 ID の判定。英字接頭辞の大文字化以外は元の表記を変えない。
/// </summary>
internal static class DlsiteIds
{
    private static readonly Regex Pattern = new("^(?<prefix>[A-Za-z]{2,})(?<digits>\\d+)$", RegexOptions.CultureInvariant);

    public static bool TryNormalize(string? raw, out string normalizedId)
    {
        normalizedId = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var match = Pattern.Match(raw.Trim());
        if (!match.Success)
        {
            return false;
        }

        normalizedId = match.Groups["prefix"].Value.ToUpperInvariant() + match.Groups["digits"].Value;
        return true;
    }
}
