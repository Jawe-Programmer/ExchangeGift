using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExchangeGift.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Tests;

public class ParticipantFlowTests(AppFixture app) : IClassFixture<AppFixture>
{
    private static readonly string[] Names = ["Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot"];

    private const string DefaultCss = "/themes/default-secret-santa-reveal/theme.css";
    private const string DubaiCss = "/themes/dubai-family-christmas-2026/theme.css";

    private async Task<(int Id, string Slug)> DrawnGroupAsync(string title, string theme, string? slug = null)
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, title, theme, Names, slug);
        await app.DrawAsync(admin, id);
        return (id, app.WithDb(db => db.Groups.Single(g => g.Id == id).Slug));
    }

    private (string Name, string Code, string RecipientName) Person(int groupId, int index = 0) => app.WithDb(db =>
    {
        var p = db.Participants.Where(x => x.GroupId == groupId).OrderBy(x => x.Id).Skip(index).First();
        var r = db.Assignments.Where(a => a.GiverParticipantId == p.Id).Select(a => a.Recipient.Name).Single();
        return (p.Name, p.ParticipantCode, r);
    });

    private static async Task<HttpResponseMessage> RevealAsync(HttpClient c, string slug, string code, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/{slug}/{code}?handler=Reveal");
        req.Headers.Add("RequestVerificationToken", token);
        return await c.SendAsync(req);
    }

    private static string TokenIn(string html) => WebWebUtility(Regex.Match(html, "data-token=\"([^\"]+)\"").Groups[1].Value);
    private static string WebWebUtility(string s) => WebUtility.HtmlDecode(s);

    [Fact]
    public async Task Each_group_serves_its_own_selected_theme()
    {
        var (a, slugA) = await DrawnGroupAsync("Theme A", "default-secret-santa-reveal");
        var (b, slugB) = await DrawnGroupAsync("Theme B", "dubai-family-christmas-2026");

        var guest = app.NewClient();
        var pa = Person(a); var pb = Person(b);
        var htmlA = await guest.GetStringAsync($"/{slugA}/{pa.Code}");
        var htmlB = await guest.GetStringAsync($"/{slugB}/{pb.Code}");

        Assert.Contains(DefaultCss, htmlA); Assert.DoesNotContain(DubaiCss, htmlA);
        Assert.Contains("Secret Santa", htmlA);
        Assert.Contains(DubaiCss, htmlB); Assert.DoesNotContain(DefaultCss, htmlB);
        Assert.Contains("Press and hold to light up the Burj Khalifa", htmlB);
        Assert.Contains("data-theme-root", htmlA); Assert.Contains("data-theme-root", htmlB);
    }

    [Fact]
    public async Task Switching_a_groups_theme_changes_what_participants_see()
    {
        var (id, slug) = await DrawnGroupAsync("Switcher", "default-secret-santa-reveal");
        var code = Person(id).Code;
        var guest = app.NewClient();
        Assert.Contains(DefaultCss, await guest.GetStringAsync($"/{slug}/{code}"));

        var admin = await app.NewAdminClientAsync();
        await admin.PostFormAsync($"/admin/groups/{id}?handler=Theme", new() { ["themeKey"] = "dubai-family-christmas-2026" }, tokenFrom: $"/admin/groups/{id}");
        Assert.Contains(DubaiCss, await guest.GetStringAsync($"/{slug}/{code}"));
    }

    [Fact]
    public async Task Participant_page_never_contains_any_recipient_or_other_participant_name()
    {
        var (id, slug) = await DrawnGroupAsync("No Leaks", "dubai-family-christmas-2026");
        var guest = app.NewClient();
        for (var i = 0; i < Names.Length; i++)
        {
            var p = Person(id, i);
            var html = await guest.GetStringAsync($"/{slug}/{p.Code}");
            foreach (var other in Names.Where(n => n != p.Name))
                Assert.DoesNotContain(other, html);   // covers the recipient and everyone else
            Assert.Contains(p.Name, html);            // only the visitor's own name is present
        }
    }

    [Fact]
    public async Task Public_group_landing_page_does_not_disclose_the_roster()
    {
        var (id, slug) = await DrawnGroupAsync("Landing Roster", "default-secret-santa-reveal");
        var html = await app.NewClient().GetStringAsync($"/{slug}");
        Assert.Contains("Landing Roster", html);
        foreach (var n in Names) Assert.DoesNotContain(n, html);
        foreach (var i in Enumerable.Range(0, Names.Length)) Assert.DoesNotContain(Person(id, i).Code, html);
    }

    [Fact]
    public async Task Landing_page_code_box_redirects_to_the_personal_link()
    {
        var (id, slug) = await DrawnGroupAsync("Code Box", "default-secret-santa-reveal");
        var code = Person(id).Code;
        var guest = app.NewClient();
        var token = await guest.GetTokenAsync($"/{slug}");
        var r = await guest.PostAsync($"/{slug}", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code.ToLowerInvariant(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal($"/{slug}/{code}", r.Headers.Location!.OriginalString);

        var bad = await guest.PostAsync($"/{slug}", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "no", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
    }

    [Fact]
    public async Task Reveal_requires_the_antiforgery_token()
    {
        var (id, slug) = await DrawnGroupAsync("Csrf", "default-secret-santa-reveal");
        var code = Person(id).Code;
        var guest = app.NewClient();
        await guest.GetStringAsync($"/{slug}/{code}");
        var r = await guest.PostAsync($"/{slug}/{code}?handler=Reveal", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, app.WithDb(db => db.Reveals.Count(x => x.GroupId == id)));
    }

    [Fact]
    public async Task Reveal_returns_the_assigned_recipient_and_records_it_once()
    {
        var (id, slug) = await DrawnGroupAsync("Reveal Once", "default-secret-santa-reveal");
        var p = Person(id);
        var guest = app.NewClient();
        var token = TokenIn(await guest.GetStringAsync($"/{slug}/{p.Code}"));
        Assert.Equal(0, app.WithDb(db => db.Reveals.Count(x => x.GroupId == id)));   // visiting alone reveals nothing

        var first = await RevealAsync(guest, slug, p.Code, token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        Assert.Equal(p.RecipientName, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());

        var firstStamp = app.WithDb(db => db.Reveals.Single(x => x.GroupId == id).FirstRevealedAtUtc);
        app.Clock.Set(DateTimeOffset.UtcNow.AddHours(2));
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var again = await RevealAsync(guest, slug, p.Code, token);
                Assert.Equal(p.RecipientName, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
            }
        }
        finally { app.Clock.Reset(); }

        app.WithDb(db =>
        {
            var reveal = db.Reveals.Single(x => x.GroupId == id);                // still one record
            Assert.Equal(firstStamp, reveal.FirstRevealedAtUtc);                 // first timestamp preserved
            Assert.True(reveal.LastVisitedAtUtc > reveal.FirstRevealedAtUtc);
            return 0;
        });
    }

    [Fact]
    public async Task Returning_participants_get_the_same_result_from_a_fresh_browser()
    {
        var (id, slug) = await DrawnGroupAsync("Returning", "dubai-family-christmas-2026");
        var p = Person(id, 2);
        string? seen = null;
        for (var visit = 0; visit < 3; visit++)
        {
            var browser = app.NewClient();   // new cookies each time, as on another phone
            var token = TokenIn(await browser.GetStringAsync($"/{slug}/{p.Code}"));
            var json = await (await RevealAsync(browser, slug, p.Code, token)).Content.ReadFromJsonAsync<JsonElement>();
            seen ??= json.GetProperty("name").GetString();
            Assert.Equal(seen, json.GetProperty("name").GetString());
        }
        Assert.Equal(p.RecipientName, seen);
    }

    [Fact]
    public async Task Simultaneous_reveal_requests_are_safe_and_idempotent()
    {
        var (id, slug) = await DrawnGroupAsync("Parallel", "default-secret-santa-reveal");
        var p = Person(id, 1);
        var guest = app.NewClient();
        var token = TokenIn(await guest.GetStringAsync($"/{slug}/{p.Code}"));

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            var r = await RevealAsync(guest, slug, p.Code, token);
            return (r.StatusCode, Name: (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
        }));
        Assert.All(results, r => { Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Equal(p.RecipientName, r.Name); });
        Assert.Equal(1, app.WithDb(db => db.Reveals.Count(x => x.GroupId == id)));
    }

    [Fact]
    public async Task Participants_cannot_reach_each_others_results_by_editing_the_url()
    {
        var (id, slug) = await DrawnGroupAsync("Isolation", "default-secret-santa-reveal");
        var (id2, slug2) = await DrawnGroupAsync("Isolation Two", "default-secret-santa-reveal");
        var mine = Person(id, 0); var theirs = Person(id, 1); var otherGroup = Person(id2, 0);
        var guest = app.NewClient();
        var token = TokenIn(await guest.GetStringAsync($"/{slug}/{mine.Code}"));

        // my token + someone else's code => that person's own result is returned only for THEIR code, never mixed
        var viaTheirCode = await (await RevealAsync(guest, slug, theirs.Code, token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(theirs.RecipientName, viaTheirCode.GetProperty("name").GetString());   // a code IS the credential...
        Assert.NotEqual(mine.Code, theirs.Code);

        // ...but a code from another group is useless under this group's slug, and vice versa
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/{slug}/{otherGroup.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/{slug2}/{mine.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RevealAsync(guest, slug, otherGroup.Code, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RevealAsync(guest, slug2, mine.Code, token)).StatusCode);
    }

    [Theory]
    [InlineData("ZZZZZZ")]
    [InlineData("12345")]
    [InlineData("TOOLONG1")]
    [InlineData("AB-DEF")]
    public async Task Invalid_or_unknown_codes_get_the_same_generic_404(string code)
    {
        var (_, slug) = await DrawnGroupAsync("Generic " + code, "default-secret-santa-reveal");
        var guest = app.NewClient();
        var r = await guest.GetAsync($"/{slug}/{code}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("This link isn't available", body);

        var unknownSlug = await guest.GetAsync($"/no-such-group/{code}");
        Assert.Equal(HttpStatusCode.NotFound, unknownSlug.StatusCode);
        Assert.Equal(body, await unknownSlug.Content.ReadAsStringAsync());   // indistinguishable from a bad code
    }

    [Fact]
    public async Task Draft_groups_show_a_not_ready_notice_and_cannot_reveal()
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, "Not Ready", "default-secret-santa-reveal", Names);
        var code = app.WithDb(db => db.Participants.First(p => p.GroupId == id).ParticipantCode);
        var guest = app.NewClient();
        var html = await guest.GetStringAsync($"/not-ready/{code}");
        Assert.Contains("draw hasn't happened yet", html);
        Assert.DoesNotContain("data-theme-root", html);
        var r = await guest.PostAsync($"/not-ready/{code}?handler=Reveal", null);
        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        Assert.Equal(0, app.WithDb(db => db.Reveals.Count(x => x.GroupId == id)));
    }

    [Fact]
    public async Task Assignments_persist_across_an_application_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "exchangegift-restart-" + Guid.NewGuid().ToString("N"));
        string slug, code, expected;
        try
        {
            using (var first = AppFixture.Reopen(dir))
            {
                var admin = await first.NewAdminClientAsync();
                var id = await first.CreateGroupAsync(admin, "Restart Test", "dubai-family-christmas-2026", Names);
                await first.DrawAsync(admin, id);
                (slug, code, expected) = first.WithDb(db =>
                {
                    var p = db.Participants.Where(x => x.GroupId == id).OrderBy(x => x.Id).First();
                    return ("restart-test", p.ParticipantCode, db.Assignments.Where(a => a.GiverParticipantId == p.Id).Select(a => a.Recipient.Name).Single());
                });
                var browser = first.NewClient();
                var t = TokenIn(await browser.GetStringAsync($"/{slug}/{code}"));
                Assert.Equal(HttpStatusCode.OK, (await RevealAsync(browser, slug, code, t)).StatusCode);
            }

            using var second = AppFixture.Reopen(dir);   // brand-new host on the same database file
            var guest = second.NewClient();
            var html = await guest.GetStringAsync($"/{slug}/{code}");
            Assert.Contains(DubaiCss, html);
            var json = await (await RevealAsync(guest, slug, code, TokenIn(html))).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expected, json.GetProperty("name").GetString());
            Assert.Equal(1, second.WithDb(db => db.Reveals.Count()));
            Assert.Equal(6, second.WithDb(db => db.Assignments.Count()));

            // the administrator account also survives, with the same password
            var admin2 = await second.NewAdminClientAsync();
            Assert.Equal(HttpStatusCode.OK, (await admin2.GetAsync("/admin")).StatusCode);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }


    [Fact]
    public async Task Reveal_includes_only_the_recipients_wish_list_and_it_is_capped_at_three()
    {
        var (id, slug) = await DrawnGroupAsync("Wishes", "dubai-family-christmas-2026");
        var giver = Person(id);
        var (recipientId, giverId) = app.WithDb(db =>
        {
            var g = db.Participants.Single(p => p.GroupId == id && p.ParticipantCode == giver.Code);
            var r = db.Assignments.Where(a => a.GiverParticipantId == g.Id).Select(a => a.RecipientParticipantId).Single();
            return (r, g.Id);
        });
        var admin = await app.NewAdminClientAsync();
        var page = $"/admin/groups/{id}";
        await admin.PostFormAsync($"{page}?handler=Wishes", new() { ["participantId"] = recipientId.ToString(), ["wishes"] = string.Join(Environment.NewLine, "A scarf", "", "A book", "Tea", "Fourth ignored") }, tokenFrom: page);
        await admin.PostFormAsync($"{page}?handler=Wishes", new() { ["participantId"] = giverId.ToString(), ["wishes"] = "MyOwnSecretWish" }, tokenFrom: page);

        var guest = app.NewClient();
        var html = await guest.GetStringAsync($"/{slug}/{giver.Code}");
        Assert.DoesNotContain("A scarf", html);                    // wishes are not in the page before the reveal
        var json = await (await RevealAsync(guest, slug, giver.Code, TokenIn(html))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "A scarf", "A book", "Tea" }, json.GetProperty("wishes").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.DoesNotContain("MyOwnSecretWish", json.ToString());  // never the giver's own list
    }

    [Fact]
    public async Task Reset_requires_slug_confirmation_after_reveals_and_clears_everything()
    {
        var (id, slug) = await DrawnGroupAsync("Reset Me", "default-secret-santa-reveal");
        var p = Person(id);
        var guest = app.NewClient();
        await RevealAsync(guest, slug, p.Code, TokenIn(await guest.GetStringAsync($"/{slug}/{p.Code}")));
        var admin = await app.NewAdminClientAsync();
        var page = $"/admin/groups/{id}";

        await admin.PostFormAsync($"{page}?handler=Reset", new() { ["confirm"] = "" }, tokenFrom: page);
        Assert.Equal(GroupStatus.Drawn, app.WithDb(db => db.Groups.Single(g => g.Id == id).Status));   // refused
        await admin.PostFormAsync($"{page}?handler=Reset", new() { ["confirm"] = "wrong" }, tokenFrom: page);
        Assert.Equal(6, app.WithDb(db => db.Assignments.Count(a => a.GroupId == id)));

        await admin.PostFormAsync($"{page}?handler=Reset", new() { ["confirm"] = slug }, tokenFrom: page);
        app.WithDb(db =>
        {
            Assert.Equal(GroupStatus.Draft, db.Groups.Single(g => g.Id == id).Status);
            Assert.Empty(db.Assignments.Where(a => a.GroupId == id));
            Assert.Empty(db.Reveals.Where(r => r.GroupId == id));
            return 0;
        });
        Assert.Contains("draw hasn't happened yet", await guest.GetStringAsync($"/{slug}/{p.Code}"));
    }

    [Fact]
    public async Task Reset_without_reveals_only_needs_the_dialog_confirmation()
    {
        var (id, _) = await DrawnGroupAsync("Reset Easy", "default-secret-santa-reveal");
        var admin = await app.NewAdminClientAsync();
        await admin.PostFormAsync($"/admin/groups/{id}?handler=Reset", new(), tokenFrom: $"/admin/groups/{id}");
        Assert.Equal(GroupStatus.Draft, app.WithDb(db => db.Groups.Single(g => g.Id == id).Status));
    }

    [Fact]
    public async Task Archived_groups_close_every_link_until_restored()
    {
        var (id, slug) = await DrawnGroupAsync("Archive Me", "default-secret-santa-reveal");
        var p = Person(id);
        var guest = app.NewClient();
        var token = TokenIn(await guest.GetStringAsync($"/{slug}/{p.Code}"));
        var admin = await app.NewAdminClientAsync();
        var page = $"/admin/groups/{id}";

        await admin.PostFormAsync($"{page}?handler=Archive", new(), tokenFrom: page);
        var closed = await guest.GetStringAsync($"/{slug}/{p.Code}");
        Assert.Contains("has finished", closed);
        Assert.DoesNotContain("data-theme-root", closed);
        Assert.Equal(HttpStatusCode.NotFound, (await RevealAsync(guest, slug, p.Code, token)).StatusCode);

        await admin.PostFormAsync($"{page}?handler=Unarchive", new(), tokenFrom: page);
        Assert.Equal(GroupStatus.Drawn, app.WithDb(db => db.Groups.Single(g => g.Id == id).Status));
        Assert.Equal(HttpStatusCode.OK, (await RevealAsync(guest, slug, p.Code, token)).StatusCode);
    }

    [Fact]
    public async Task Expired_groups_close_their_links_and_the_dashboard_says_so()
    {
        var admin = await app.NewAdminClientAsync();
        var soon = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd");
        var id = await app.CreateGroupAsync(admin, "Expires Soon", "default-secret-santa-reveal", Names, expiresOn: soon);
        await app.DrawAsync(admin, id);
        var p = Person(id);
        var guest = app.NewClient();
        var token = TokenIn(await guest.GetStringAsync($"/expires-soon/{p.Code}"));
        Assert.Equal(HttpStatusCode.OK, (await RevealAsync(guest, "expires-soon", p.Code, token)).StatusCode);

        app.Clock.Set(DateTimeOffset.UtcNow.AddDays(10));
        try
        {
            Assert.Contains("has finished", await guest.GetStringAsync($"/expires-soon/{p.Code}"));
            Assert.Equal(HttpStatusCode.NotFound, (await RevealAsync(guest, "expires-soon", p.Code, token)).StatusCode);
            var admin2 = await app.NewAdminClientAsync();   // sessions are time-based too, so sign in again at the new time
            Assert.Contains("chip-expired", await admin2.GetStringAsync("/admin/groups"));
        }
        finally { app.Clock.Reset(); }
        Assert.Equal(HttpStatusCode.OK, (await RevealAsync(guest, "expires-soon", p.Code, token)).StatusCode);
    }

    [Fact]
    public async Task Admin_group_page_reports_reveal_status_and_time()
    {
        var (id, slug) = await DrawnGroupAsync("Status Page", "default-secret-santa-reveal");
        var p = Person(id);
        var admin = await app.NewAdminClientAsync();
        var before = await admin.GetStringAsync($"/admin/groups/{id}");
        Assert.Equal(6, Regex.Matches(before, "Not revealed yet").Count);

        var guest = app.NewClient();
        await RevealAsync(guest, slug, p.Code, TokenIn(await guest.GetStringAsync($"/{slug}/{p.Code}")));
        var after = await admin.GetStringAsync($"/admin/groups/{id}");
        Assert.Equal(5, Regex.Matches(after, "Not revealed yet").Count);
        Assert.Contains("chip-yes", after);
        Assert.Contains("1 of 6 revealed", after);
        Assert.Contains($"/{slug}/{p.Code}", after);   // personal link shown for copying
    }

    [Fact]
    public async Task Sharing_address_is_used_for_generated_links_and_validated()
    {
        var (id, slug) = await DrawnGroupAsync("Share Base", "default-secret-santa-reveal");
        var p = Person(id);
        var admin = await app.NewAdminClientAsync();
        var bad = await admin.PostFormAsync("/admin?handler=BaseUrl", new() { ["baseUrl"] = "http://example.com" }, tokenFrom: "/admin");
        Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
        Assert.DoesNotContain("http://example.com", await admin.GetStringAsync($"/admin/groups/{id}"));

        await admin.PostFormAsync("/admin?handler=BaseUrl", new() { ["baseUrl"] = "https://quiet-river-1234.trycloudflare.com/" }, tokenFrom: "/admin");
        var html = await admin.GetStringAsync($"/admin/groups/{id}");
        Assert.Contains($"https://quiet-river-1234.trycloudflare.com/{slug}/{p.Code}", html);
        await admin.PostFormAsync("/admin?handler=BaseUrl", new() { ["baseUrl"] = "" }, tokenFrom: "/admin");
    }
}

public class RateLimitTests
{
    [Fact]
    public async Task Hammering_the_participant_endpoint_is_throttled()
    {
        using var app = new AppFixture { ParticipantLimit = 8 };
        var guest = app.NewClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 14; i++) codes.Add((await guest.GetAsync("/some-group/ABC12" + (char)('A' + i))).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.Equal(HttpStatusCode.NotFound, codes[0]);
    }
}
