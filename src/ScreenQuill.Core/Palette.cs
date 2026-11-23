namespace ScreenQuill.Core;

/// <summary>
/// The 24 quick-pick colours, arranged 3 columns × 8 rows exactly as in the reference flyout
/// (Reference images/images.jfif). Values were sampled from the reference JPEG and lightly
/// de-noised; see docs/reference-audit.md.
/// </summary>
public static class Palette
{
    public const int Columns = 3;
    public const int Rows = 8;

    public static readonly uint[] Quick =
    {
        0xFF27AE60, 0xFF1DD633, 0xFF00FF1B, // greens
        0xFF1B6A9D, 0xFF01ADED, 0xFF02EFFD, // blues
        0xFFC12111, 0xFFE64D3B, 0xFFFE0202, // reds
        0xFF911353, 0xFFDB0C74, 0xFFF35093, // magentas / pinks
        0xFFF4C20E, 0xFFFDF203, 0xFFFDF267, // yellows
        0xFFD45400, 0xFFE67F20, 0xFFFF8A00, // oranges (last cell occluded by cursor in reference; estimated)
        0xFFFFFFFF, 0xFF5C5C5E, 0xFF3F2C57, // white / grey / dark purple
        0xFFBDC3C7, 0xFF000000, 0xFF512D1E, // light grey / black / brown
    };

    /// <summary>Initial recent-colour swatches shown in the toolbar's 2×2 grid (cyan, yellow, magenta, black).</summary>
    public static readonly uint[] DefaultRecent = { 0xFF00B1F1, 0xFFFDF300, 0xFFFE0B81, 0xFF000000 };

    public const uint DefaultColor = 0xFFFE0202;

    /// <summary>Moves <paramref name="color"/> to the front of the recent list, keeping at most 4 unique entries.</summary>
    public static List<uint> PushRecent(IEnumerable<uint> recent, uint color)
    {
        var list = new List<uint> { color };
        list.AddRange(recent.Where(c => c != color));
        while (list.Count < 4) list.Add(DefaultRecent[list.Count % 4]);
        return list.Take(4).ToList();
    }

    public static string ToHex(uint argb) =>
        (argb >> 24) == 0xFF ? $"#{argb & 0xFFFFFF:X6}" : $"#{argb:X8}";

    public static bool TryParseHex(string? s, out uint argb)
    {
        argb = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return false;
        return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out argb);
    }
}
