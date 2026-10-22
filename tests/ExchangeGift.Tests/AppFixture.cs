using System.Net;
using System.Text.RegularExpressions;
using ExchangeGift.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExchangeGift.Tests;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset? _fixed;
    public override DateTimeOffset GetUtcNow() => _fixed ?? DateTimeOffset.UtcNow;
    public void Set(DateTimeOffset value) => _fixed = value;
    public void Reset() => _fixed = null;
}

/// <summary>A fully running copy of the app with its own throw-away SQLite database.</summary>
public sealed class AppFixture : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Test-Admin-Pw1";
    public string StorageDir { get; }
    public int ParticipantLimit { get; init; } = 100000;
    public TestClock Clock { get; } = new();
    private readonly bool _ownsDir;

    /// <summary>Re-opens an existing storage folder (used to simulate an application restart).</summary>
    public static AppFixture Reopen(string dir) => new(dir, false);

    public AppFixture() : this(Path.Combine(Path.GetTempPath(), "exchangegift-tests-" + Guid.NewGuid().ToString("N")), true) { }

    private AppFixture(string storageDir, bool ownsDir)
    {
        StorageDir = storageDir;
        _ownsDir = ownsDir;
        Directory.CreateDirectory(StorageDir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:Directory", StorageDir);
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("RateLimit:ParticipantPerMinute", ParticipantLimit.ToString());
        builder.UseSetting("RateLimit:LoginPerMinute", "1000");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> NewAdminClientAsync()
    {
        var c = NewClient();
        var r = await c.PostFormAsync("/admin/login", new() { ["Username"] = "Administrator", ["password"] = AdminPassword });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    public T WithDb<T>(Func<AppDbContext, T> work)
    {
        using var scope = Services.CreateScope();
        return work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Creates a group through the real admin form and returns its id.</summary>
    public async Task<int> CreateGroupAsync(HttpClient admin, string title, string theme, IEnumerable<string> names, string? slug = null, string? expiresOn = null)
    {
        var r = await admin.PostFormAsync("/admin/groups/create", new()
        {
            ["Title"] = title, ["Slug"] = slug ?? "", ["ThemeKey"] = theme,
            ["ParticipantsText"] = string.Join('\n', names), ["ExpiresOn"] = expiresOn ?? "",
        }, tokenFrom: "/admin/groups/create");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var m = Regex.Match(r.Headers.Location!.ToString(), @"/admin/groups/(\d+)$");
        Assert.True(m.Success, "Expected a redirect to the new group but got " + r.Headers.Location);
        return int.Parse(m.Groups[1].Value);
    }

    public async Task DrawAsync(HttpClient admin, int groupId)
    {
        var r = await admin.PostFormAsync($"/admin/groups/{groupId}?handler=Draw", new(), tokenFrom: $"/admin/groups/{groupId}");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDir)
        {
            try { Directory.Delete(StorageDir, true); } catch { /* sqlite may still hold the file briefly */ }
        }
    }
}

public static class HttpExtensions
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<string> GetTokenAsync(this HttpClient c, string url)
    {
        var html = await c.GetStringAsync(url);
        var m = Token.Match(html);
        Assert.True(m.Success, $"No antiforgery token found on {url}");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient c, string url, Dictionary<string, string> fields, string? tokenFrom = null)
    {
        var token = await c.GetTokenAsync(tokenFrom ?? (url.StartsWith("/admin/login") ? "/admin/login" : "/admin/password"));
        fields["__RequestVerificationToken"] = token;
        return await c.PostAsync(url, new FormUrlEncodedContent(fields));
    }
}
