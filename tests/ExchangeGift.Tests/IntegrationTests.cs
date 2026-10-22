using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExchangeGift.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Tests;

public class AuthorizationTests(AppFixture app) : IClassFixture<AppFixture>
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/groups")]
    [InlineData("/admin/groups/create")]
    [InlineData("/admin/groups/1")]
    [InlineData("/admin/groups/1?handler=Recipient&participantId=1")]
    [InlineData("/admin/password")]
    [InlineData("/admin/credits")]
    [InlineData("/admin/themes/default-secret-santa-reveal/preview")]
    public async Task Admin_pages_redirect_anonymous_visitors_to_login(string url)
    {
        var r = await app.NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/admin/login", r.Headers.Location!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_post_without_antiforgery_token_is_rejected()
    {
        var admin = await app.NewAdminClientAsync();
        var r = await admin.PostAsync("/admin/groups/create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Title"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Admin_can_open_every_admin_page_after_login()
    {
        var admin = await app.NewAdminClientAsync();
        foreach (var url in new[] { "/admin", "/admin/groups", "/admin/groups/create", "/admin/password", "/admin/credits",
                     "/admin/themes/default-secret-santa-reveal/preview", "/admin/themes/dubai-family-christmas-2026/preview" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_a_generic_message()
    {
        var c = app.NewClient();
        var r = await c.PostFormAsync("/admin/login", new() { ["Username"] = "Administrator", ["password"] = "nope-Nope-1" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // stays on the form
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", html);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Login_ignores_external_return_urls()
    {
        var c = app.NewClient();
        var r = await c.PostFormAsync("/admin/login?ReturnUrl=https://evil.example/", new()
        {
            ["Username"] = "Administrator", ["password"] = AppFixture.AdminPassword, ["returnUrl"] = "https://evil.example/",
        }, tokenFrom: "/admin/login");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/admin", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Sign_out_ends_the_session()
    {
        var admin = await app.NewAdminClientAsync();
        var r = await admin.PostFormAsync("/admin/logout", new(), tokenFrom: "/admin/password");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/admin/groups")).StatusCode);
    }

    [Fact]
    public async Task Admin_cookie_is_http_only_and_antiforgery_is_enforced()
    {
        var c = app.NewClient();
        var token = await c.GetTokenAsync("/admin/login");
        var r = await c.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Username"] = "Administrator", ["password"] = AppFixture.AdminPassword, ["__RequestVerificationToken"] = token }));
        var cookie = r.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("ExchangeGift.Admin"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_files_and_app_data_are_not_downloadable()
    {
        var c = app.NewClient();
        foreach (var url in new[] { "/exchangegift.db", "/App_Data/exchangegift.db", "/keys", "/appsettings.json" })
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var r = await app.NewClient().GetAsync("/");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", r.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Password_change_requires_the_current_password_and_takes_effect()
    {
        using var own = new AppFixture();
        var admin = await own.NewAdminClientAsync();

        var bad = await admin.PostFormAsync("/admin/password", new() { ["current"] = "wrong-Wrong-1", ["newPassword"] = "Brand-New-Pw2", ["confirm"] = "Brand-New-Pw2" });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("current password is not correct", await bad.Content.ReadAsStringAsync());

        var weak = await admin.PostFormAsync("/admin/password", new() { ["current"] = AppFixture.AdminPassword, ["newPassword"] = "short", ["confirm"] = "short" });
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);

        var ok = await admin.PostFormAsync("/admin/password", new() { ["current"] = AppFixture.AdminPassword, ["newPassword"] = "Brand-New-Pw2", ["confirm"] = "Brand-New-Pw2" });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        var fresh = own.NewClient();
        var oldLogin = await fresh.PostFormAsync("/admin/login", new() { ["Username"] = "Administrator", ["password"] = AppFixture.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        var newLogin = await fresh.PostFormAsync("/admin/login", new() { ["Username"] = "Administrator", ["password"] = "Brand-New-Pw2" });
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        using var own = new AppFixture();
        var c = own.NewClient();
        for (var i = 0; i < 5; i++)
            await c.PostFormAsync("/admin/login", new() { ["Username"] = "Administrator", ["password"] = "bad-Bad-1" });
        var r = await c.PostFormAsync("/admin/login", new() { ["Username"] = "Administrator", ["password"] = AppFixture.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // even the right password is refused while locked
        Assert.Contains("Too many failed attempts", await r.Content.ReadAsStringAsync());
    }
}

public class GroupManagementTests(AppFixture app) : IClassFixture<AppFixture>
{
    private static readonly string[] Names = ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    [Fact]
    public async Task Group_is_created_with_slug_theme_and_participants_with_codes()
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, "Family Christmas 2026", "dubai-family-christmas-2026", Names);

        app.WithDb(db =>
        {
            var g = db.Groups.Include(x => x.Participants).Single(x => x.Id == id);
            Assert.Equal("family-christmas-2026", g.Slug);          // generated from the title
            Assert.Equal("dubai-family-christmas-2026", g.ThemeKey); // stable internal key persisted
            Assert.Equal(GroupStatus.Draft, g.Status);
            Assert.Equal(5, g.Participants.Count);
            Assert.All(g.Participants, p => Assert.Matches("^[A-Z0-9]{6}$", p.ParticipantCode));
            Assert.Equal(5, g.Participants.Select(p => p.ParticipantCode).Distinct().Count());
            return 0;
        });
    }

    [Fact]
    public async Task Duplicate_slug_is_rejected_even_with_different_case()
    {
        var admin = await app.NewAdminClientAsync();
        await app.CreateGroupAsync(admin, "Dup Test", "default-secret-santa-reveal", Names, slug: "dup-test");
        var r = await admin.PostFormAsync("/admin/groups/create", new()
        { ["Title"] = "Another", ["Slug"] = "DUP-TEST", ["ThemeKey"] = "default-secret-santa-reveal", ["ParticipantsText"] = "A\nB" }, tokenFrom: "/admin/groups/create");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("already used by another group", await r.Content.ReadAsStringAsync());
        Assert.Equal(1, app.WithDb(db => db.Groups.Count(g => g.Slug == "dup-test")));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("themes")]
    [InlineData("Bad Slug!")]
    public async Task Reserved_or_malformed_slugs_are_rejected(string slug)
    {
        var admin = await app.NewAdminClientAsync();
        var r = await admin.PostFormAsync("/admin/groups/create", new()
        { ["Title"] = "X Group", ["Slug"] = slug, ["ThemeKey"] = "default-secret-santa-reveal", ["ParticipantsText"] = "A\nB" }, tokenFrom: "/admin/groups/create");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, app.WithDb(db => db.Groups.Count(g => g.Title == "X Group")));
    }

    [Fact]
    public async Task Unknown_theme_and_blank_or_duplicate_names_are_rejected()
    {
        var admin = await app.NewAdminClientAsync();
        var badTheme = await admin.PostFormAsync("/admin/groups/create", new()
        { ["Title"] = "Bad Theme", ["ThemeKey"] = "third-theme", ["ParticipantsText"] = "A\nB" }, tokenFrom: "/admin/groups/create");
        Assert.Equal(HttpStatusCode.OK, badTheme.StatusCode);
        Assert.Contains("Choose one of the available themes", await badTheme.Content.ReadAsStringAsync());

        var dup = await admin.PostFormAsync("/admin/groups/create", new()
        { ["Title"] = "Dup Names", ["ThemeKey"] = "default-secret-santa-reveal", ["ParticipantsText"] = "Sam\n\n  sam  \nAlex" }, tokenFrom: "/admin/groups/create");
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        Assert.Contains("appears more than once", await dup.Content.ReadAsStringAsync());
        Assert.Equal(0, app.WithDb(db => db.Groups.Count(g => g.Title == "Dup Names")));
    }

    [Fact]
    public async Task Participants_can_be_added_renamed_and_removed_before_the_draw_only()
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, "Edit Before Draw", "default-secret-santa-reveal", ["One", "Two"]);
        var page = $"/admin/groups/{id}";

        await admin.PostFormAsync($"{page}?handler=Add", new() { ["name"] = "Three" }, tokenFrom: page);
        await admin.PostFormAsync($"{page}?handler=Add", new() { ["name"] = "three" }, tokenFrom: page);      // duplicate: ignored
        await admin.PostFormAsync($"{page}?handler=Bulk", new() { ["bulk"] = "Four\nFive\n\nFour" }, tokenFrom: page);
        Assert.Equal(["Five", "Four", "One", "Three", "Two"], app.WithDb(db => db.Participants.Where(p => p.GroupId == id).Select(p => p.Name).OrderBy(n => n).ToList()));

        var two = app.WithDb(db => db.Participants.Single(p => p.GroupId == id && p.Name == "Two").Id);
        await admin.PostFormAsync($"{page}?handler=Rename", new() { ["participantId"] = two.ToString(), ["name"] = "Zwei" }, tokenFrom: page);
        await admin.PostFormAsync($"{page}?handler=Rename", new() { ["participantId"] = two.ToString(), ["name"] = "  " }, tokenFrom: page); // blank: rejected
        await admin.PostFormAsync($"{page}?handler=Remove", new() { ["participantId"] = app.WithDb(db => db.Participants.Single(p => p.GroupId == id && p.Name == "Five").Id).ToString() }, tokenFrom: page);
        Assert.Equal(["Four", "One", "Three", "Zwei"], app.WithDb(db => db.Participants.Where(p => p.GroupId == id).Select(p => p.Name).OrderBy(n => n).ToList()));

        await app.DrawAsync(admin, id);
        await admin.PostFormAsync($"{page}?handler=Add", new() { ["name"] = "Late" }, tokenFrom: page);
        await admin.PostFormAsync($"{page}?handler=Remove", new() { ["participantId"] = two.ToString() }, tokenFrom: page);
        Assert.Equal(4, app.WithDb(db => db.Participants.Count(p => p.GroupId == id)));
    }

    [Fact]
    public async Task Draw_needs_two_participants()
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, "Too Small", "default-secret-santa-reveal", ["Solo"]);
        await app.DrawAsync(admin, id);
        app.WithDb(db =>
        {
            Assert.Equal(GroupStatus.Draft, db.Groups.Single(g => g.Id == id).Status);
            Assert.Empty(db.Assignments.Where(a => a.GroupId == id));
            return 0;
        });
    }

    [Fact]
    public async Task Draw_is_a_valid_derangement_and_cannot_be_generated_twice()
    {
        var admin = await app.NewAdminClientAsync();
        var names = Enumerable.Range(1, 12).Select(i => "Person" + i).ToArray();
        var id = await app.CreateGroupAsync(admin, "Big Draw", "default-secret-santa-reveal", names);
        await app.DrawAsync(admin, id);

        var first = app.WithDb(db => db.Assignments.Where(a => a.GroupId == id).OrderBy(a => a.GiverParticipantId).Select(a => new { a.GiverParticipantId, a.RecipientParticipantId }).ToList());
        Assert.Equal(12, first.Count);
        Assert.All(first, a => Assert.NotEqual(a.GiverParticipantId, a.RecipientParticipantId));
        Assert.Equal(12, first.Select(a => a.GiverParticipantId).Distinct().Count());
        Assert.Equal(12, first.Select(a => a.RecipientParticipantId).Distinct().Count());
        Assert.Equal(first.Select(a => a.GiverParticipantId).OrderBy(x => x), first.Select(a => a.RecipientParticipantId).OrderBy(x => x));

        await app.DrawAsync(admin, id); // second attempt must not reshuffle
        var second = app.WithDb(db => db.Assignments.Where(a => a.GroupId == id).OrderBy(a => a.GiverParticipantId).Select(a => new { a.GiverParticipantId, a.RecipientParticipantId }).ToList());
        Assert.Equal(first, second);
    }

    [Fact]
    public void Database_rejects_self_assignment_and_duplicate_recipients()
    {
        var gid = app.WithDb(db =>
        {
            var g = new ExchangeGiftGroup { Title = "Constraint", Slug = "constraint-test", ThemeKey = "default-secret-santa-reveal", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            g.Participants.AddRange([new() { Name = "A", ParticipantCode = "AAAAAA" }, new() { Name = "B", ParticipantCode = "BBBBBB" }, new() { Name = "C", ParticipantCode = "CCCCCC" }]);
            db.Groups.Add(g); db.SaveChanges(); return g.Id;
        });

        Assert.Throws<DbUpdateException>(() => app.WithDb(db =>
        {
            var ps = db.Participants.Where(p => p.GroupId == gid).OrderBy(p => p.Id).ToList();
            db.Assignments.Add(new ExchangeGiftAssignment { GroupId = gid, GiverParticipantId = ps[0].Id, RecipientParticipantId = ps[0].Id, CreatedAtUtc = DateTime.UtcNow });
            db.SaveChanges(); return 0;
        }));
        Assert.Throws<DbUpdateException>(() => app.WithDb(db =>
        {
            var ps = db.Participants.Where(p => p.GroupId == gid).OrderBy(p => p.Id).ToList();
            db.Assignments.AddRange(
                new ExchangeGiftAssignment { GroupId = gid, GiverParticipantId = ps[0].Id, RecipientParticipantId = ps[2].Id, CreatedAtUtc = DateTime.UtcNow },
                new ExchangeGiftAssignment { GroupId = gid, GiverParticipantId = ps[1].Id, RecipientParticipantId = ps[2].Id, CreatedAtUtc = DateTime.UtcNow });
            db.SaveChanges(); return 0;
        }));
        Assert.Throws<DbUpdateException>(() => app.WithDb(db =>
        {
            db.Participants.Add(new ExchangeGiftParticipant { GroupId = gid, Name = "Dup code", ParticipantCode = "AAAAAA" });
            db.SaveChanges(); return 0;
        }));
    }

    [Fact]
    public async Task Changing_theme_never_touches_the_draw()
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, "Theme Swap", "default-secret-santa-reveal", Names);
        await app.DrawAsync(admin, id);
        string Snapshot() => app.WithDb(db => string.Join("|", db.Assignments.Where(a => a.GroupId == id).OrderBy(a => a.Id).Select(a => $"{a.Id}:{a.GiverParticipantId}>{a.RecipientParticipantId}@{a.CreatedAtUtc:O}")));
        var before = Snapshot();

        var page = $"/admin/groups/{id}";
        var r = await admin.PostFormAsync($"{page}?handler=Theme", new() { ["themeKey"] = "dubai-family-christmas-2026" }, tokenFrom: page);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("dubai-family-christmas-2026", app.WithDb(db => db.Groups.Single(g => g.Id == id).ThemeKey));
        Assert.Equal(before, Snapshot());

        await admin.PostFormAsync($"{page}?handler=Theme", new() { ["themeKey"] = "not-a-theme" }, tokenFrom: page);
        Assert.Equal("dubai-family-christmas-2026", app.WithDb(db => db.Groups.Single(g => g.Id == id).ThemeKey));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Admin_page_masks_recipients_and_serves_them_only_on_request()
    {
        var admin = await app.NewAdminClientAsync();
        var id = await app.CreateGroupAsync(admin, "Eye Toggle", "default-secret-santa-reveal", Names);
        await app.DrawAsync(admin, id);
        var html = await admin.GetStringAsync($"/admin/groups/{id}");
        Assert.Equal(Names.Length, Regex.Matches(html, "••••••••").Count);          // every recipient masked
        Assert.DoesNotContain("data-recipient", html);

        var (pid, expected) = app.WithDb(db =>
        {
            var a = db.Assignments.Include(x => x.Recipient).First(x => x.GroupId == id);
            return (a.GiverParticipantId, a.Recipient.Name);
        });
        var json = await admin.GetFromJsonAsync<JsonElement>($"/admin/groups/{id}?handler=Recipient&participantId={pid}");
        Assert.Equal(expected, json.GetProperty("name").GetString());

        var missing = await admin.GetAsync($"/admin/groups/{id}?handler=Recipient&participantId=999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // a participant id that belongs to a different group is not served through this group's page
        var otherGroup = await app.CreateGroupAsync(admin, "Other Eye", "default-secret-santa-reveal", Names);
        await app.DrawAsync(admin, otherGroup);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/groups/{otherGroup}?handler=Recipient&participantId={pid}")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_counts_groups_participants_and_reveals()
    {
        using var own = new AppFixture();
        var admin = await own.NewAdminClientAsync();
        var id = await own.CreateGroupAsync(admin, "Counted", "default-secret-santa-reveal", Names);
        await own.DrawAsync(admin, id);
        var (slug, code) = own.WithDb(db => ("counted", db.Participants.First(p => p.GroupId == id).ParticipantCode));
        var guest = own.NewClient();
        var t = Regex.Match(await guest.GetStringAsync($"/{slug}/{code}"), "data-token=\"([^\"]+)\"").Groups[1].Value;
        var req = new HttpRequestMessage(HttpMethod.Post, $"/{slug}/{code}?handler=Reveal");
        req.Headers.Add("RequestVerificationToken", WebUtility.HtmlDecode(t));
        Assert.Equal(HttpStatusCode.OK, (await guest.SendAsync(req)).StatusCode);

        var html = await admin.GetStringAsync("/admin");
        Assert.Matches(@"<b>1</b><span>Exchange-gift groups</span>", html);
        Assert.Matches(@"<b>5</b><span>Participants</span>", html);
        Assert.Matches(@"<b>1</b><span>Have revealed</span>", html);
        Assert.Matches(@"<b>4</b><span>Not revealed yet</span>", html);
    }
}
