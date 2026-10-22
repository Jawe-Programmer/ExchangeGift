namespace ExchangeGift.Web.Services;

public sealed record ThemeInfo(
    string Key,
    string DisplayName,
    string Description,
    string SourceDesign,
    string PartialView,
    string PreviewImage);

/// <summary>
/// The two approved participant themes. Keys are stable and stored on each group;
/// display names can change later without affecting existing groups.
/// </summary>
public static class ThemeCatalog
{
    public const string DefaultKey = "default-secret-santa-reveal";
    public const string DubaiKey = "dubai-family-christmas-2026";

    public static readonly IReadOnlyList<ThemeInfo> All = new[]
    {
        new ThemeInfo(
            DefaultKey,
            "Default Secret Santa Reveal",
            "A snowy night scene: tap the envelope in the snow, then brush the snow off the card to see who you are the Secret Santa of.",
            "Default Secret Santa Reveal.html",
            "Themes/_DefaultSecretSantaReveal",
            "/img/theme-default-secret-santa-reveal.jpg"),
        new ThemeInfo(
            DubaiKey,
            "Dubai Family Christmas 2026",
            "The Dubai skyline at night: press and hold to light up the Burj Khalifa, and fireworks spell out who your gift goes to.",
            "Dubai Family Christmas 2026.html",
            "Themes/_DubaiFamilyChristmas2026",
            "/img/theme-dubai-family-christmas-2026.jpg"),
    };

    public static ThemeInfo? Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    public static bool IsValid(string? key) => Find(key) is not null;
}
