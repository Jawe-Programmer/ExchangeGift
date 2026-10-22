using System.Text.RegularExpressions;
using ExchangeGift.Web.Services;

namespace ExchangeGift.Web.Pages;

/// <summary>Everything a participant theme needs. It never contains the recipient.</summary>
public partial class ThemeViewModel
{
    public required ThemeInfo Info { get; init; }
    public string RevealUrl { get; init; } = "";
    public bool IsPreview { get; init; }
    public string RequestToken { get; init; } = "";
    public string GroupTitle { get; init; } = "";
    public string ParticipantName { get; init; } = "";
    /// <summary>Year shown in the designs ("2026"): taken from the group title when it contains one.</summary>
    public string Year { get; init; } = "";
    /// <summary>The group title without its year ("Family Christmas"), used for the small caps line in the Dubai design.</summary>
    public string Eyebrow { get; init; } = "";

    [GeneratedRegex(@"\b(20\d{2})\b")]
    private static partial Regex YearPattern();

    public static (string Year, string Eyebrow) SplitTitle(string title, DateTime createdAtUtc)
    {
        var m = YearPattern().Match(title);
        var year = m.Success ? m.Groups[1].Value : createdAtUtc.Year.ToString();
        var eyebrow = m.Success ? (title.Remove(m.Index, m.Length)).Trim(' ', '-', '–', '·', ',') : title;
        eyebrow = string.Join(' ', eyebrow.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (year, eyebrow.Length > 0 ? eyebrow : title);
    }
}
