using System.Text.RegularExpressions;

namespace STU.Domain.Territories;

public static partial class TerritoryColor
{
    public const string DefaultNeighborhood = "#2e8b72";
    public const string DefaultMicroregion = "#4f9a7d";

    public static string Normalize(string? color, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(color) ? fallback : color.Trim().ToLowerInvariant();
        if (!HexColor().IsMatch(normalized))
        {
            throw new ArgumentException("A cor deve usar o formato hexadecimal #rrggbb.", nameof(color));
        }

        return normalized;
    }

    [GeneratedRegex("^#[0-9a-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
