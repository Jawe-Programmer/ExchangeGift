using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExchangeGift.Web.Services;

public static partial class SlugService
{
    public const int MaxLength = 60;

    /// <summary>First URL segments owned by the application; a group may never use them.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "error", "index", "themes", "css", "js", "img", "lib", "fonts",
        "account", "login", "logout", "static", "assets", "favicon.ico", "robots.txt",
    };

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public static string Slugify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastDash = true;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (ch < 128 && char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > MaxLength) slug = slug[..MaxLength].Trim('-');
        return slug;
    }

    public static bool IsWellFormed(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxLength && SlugPattern().IsMatch(slug);

    public static bool IsReserved(string? slug) => slug is not null && Reserved.Contains(slug);
}
