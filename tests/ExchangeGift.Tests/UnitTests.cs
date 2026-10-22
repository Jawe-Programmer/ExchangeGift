using ExchangeGift.Web.Pages;
using ExchangeGift.Web.Services;

namespace ExchangeGift.Tests;

public class DrawAlgorithmTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(60)]
    public void Derangement_has_no_self_assignment_and_uses_every_value_once(int n)
    {
        for (var i = 0; i < 500; i++)
        {
            var p = DrawAlgorithm.GenerateDerangement(n);
            Assert.Equal(n, p.Length);
            Assert.True(DrawAlgorithm.IsDerangement(p), "Not a derangement: " + string.Join(",", p));
            Assert.Equal(Enumerable.Range(0, n), p.OrderBy(x => x));   // every recipient exactly once
            for (var g = 0; g < n; g++) Assert.NotEqual(g, p[g]);      // nobody draws themselves
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-3)]
    public void Fewer_than_two_participants_is_rejected(int n) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawAlgorithm.GenerateDerangement(n));

    [Fact]
    public void Two_participants_always_swap() =>
        Assert.Equal(new[] { 1, 0 }, DrawAlgorithm.GenerateDerangement(2));

    [Fact]
    public void Both_derangements_of_three_people_occur()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 400; i++) seen.Add(string.Join(",", DrawAlgorithm.GenerateDerangement(3)));
        Assert.Equal(new HashSet<string> { "1,2,0", "2,0,1" }, seen);
    }

    [Theory]
    [InlineData(new[] { 0, 1 }, false)]          // fixed points
    [InlineData(new[] { 1, 1 }, false)]          // duplicate recipient
    [InlineData(new[] { 1, 2, 0 }, true)]
    [InlineData(new[] { 1, 0, 2 }, false)]       // one fixed point
    [InlineData(new[] { 3, 0 }, false)]          // out of range
    public void Validator_catches_bad_assignments(int[] perm, bool expected) =>
        Assert.Equal(expected, DrawAlgorithm.IsDerangement(perm));
}

public class ParticipantCodeTests
{
    [Fact]
    public void Codes_are_six_uppercase_alphanumeric_characters()
    {
        for (var i = 0; i < 2000; i++)
        {
            var c = ParticipantCode.Generate();
            Assert.Equal(6, c.Length);
            Assert.Matches("^[A-Z0-9]{6}$", c);
        }
    }

    [Fact]
    public void Codes_are_effectively_unique_and_not_sequential()
    {
        var codes = Enumerable.Range(0, 5000).Select(_ => ParticipantCode.Generate()).ToList();
        // 36^6 ≈ 2.2 billion: a handful of birthday collisions is possible in 5000 draws, a flood would mean bad randomness.
        Assert.True(codes.Distinct().Count() > 4990);
        Assert.True(codes.Select(c => c[0]).Distinct().Count() > 20, "first character should vary widely");
    }

    [Theory]
    [InlineData("dmwsu3", "DMWSU3")]
    [InlineData(" DMWSU3 ", "DMWSU3")]
    [InlineData("DMWSU", null)]
    [InlineData("DMWSU33", null)]
    [InlineData("DMW-U3", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_accepts_only_valid_shapes(string? raw, string? expected) =>
        Assert.Equal(expected, ParticipantCode.Normalize(raw));
}

public class SlugTests
{
    [Theory]
    [InlineData("Family Christmas 2026", "family-christmas-2026")]
    [InlineData("  Ünïcode — Café!! ", "unicode-cafe")]
    [InlineData("A  &  B", "a-b")]
    [InlineData("---", "")]
    [InlineData("", "")]
    public void Slugify_makes_url_friendly_slugs(string title, string expected) =>
        Assert.Equal(expected, SlugService.Slugify(title));

    [Fact]
    public void Slugify_limits_length_without_trailing_hyphen()
    {
        var slug = SlugService.Slugify(new string('a', 59) + " bbbb");
        Assert.True(slug.Length <= SlugService.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("API")]
    [InlineData("error")]
    [InlineData("themes")]
    public void Application_paths_are_reserved(string slug) => Assert.True(SlugService.IsReserved(slug));

    [Theory]
    [InlineData("family-christmas-2026", true)]
    [InlineData("-bad", false)]
    [InlineData("bad--double", false)]
    [InlineData("Upper", false)]
    [InlineData("has space", false)]
    public void Well_formed_slugs(string slug, bool expected) => Assert.Equal(expected, SlugService.IsWellFormed(slug));
}

public class ThemeTests
{
    [Fact]
    public void Exactly_two_themes_with_stable_keys()
    {
        Assert.Equal(
            new[] { "default-secret-santa-reveal", "dubai-family-christmas-2026" },
            ThemeCatalog.All.Select(t => t.Key).OrderBy(x => x).ToArray());
        Assert.Equal("Default Secret Santa Reveal", ThemeCatalog.Find("default-secret-santa-reveal")!.DisplayName);
        Assert.Equal("Dubai Family Christmas 2026", ThemeCatalog.Find("dubai-family-christmas-2026")!.DisplayName);
        Assert.Null(ThemeCatalog.Find("made-up"));
    }

    [Theory]
    [InlineData("Family Christmas 2026", "2026", "Family Christmas")]
    [InlineData("Dubai Christmas", "2031", "Dubai Christmas")]
    [InlineData("2027 - Office Party", "2027", "Office Party")]
    public void Year_and_eyebrow_are_derived_from_the_title(string title, string year, string eyebrow)
    {
        var (y, e) = ThemeViewModel.SplitTitle(title, new DateTime(2031, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(year, y);
        Assert.Equal(eyebrow, e);
    }
}

public class LandingPageTests(AppFixture app) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task Landing_page_uses_the_new_name_and_lists_no_groups()
    {
        var html = await app.NewClient().GetStringAsync("/");
        Assert.Contains("Exchange Gift", html);
        Assert.DoesNotContain("Family Gift Exchange", html);
    }

    [Fact]
    public async Task Landing_form_redirects_to_the_personal_link_or_shows_an_error()
    {
        var c = app.NewClient();
        var token = await c.GetTokenAsync("/");
        var ok = await c.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string> { ["group"] = "My-Group", ["code"] = "abc123", ["__RequestVerificationToken"] = token }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/my-group/ABC123", ok.Headers.Location!.OriginalString);

        var bad = await c.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string> { ["group"] = "x y", ["code"] = "no", ["__RequestVerificationToken"] = token }));
        Assert.Equal(System.Net.HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("Check the group name", await bad.Content.ReadAsStringAsync());
    }
}
