using ExchangeGift.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Services;

/// <summary>Small key/value settings kept in the local database (currently only the public base URL).</summary>
public class SettingsService(AppDbContext db)
{
    private const string PublicBaseUrlKey = "PublicBaseUrl";

    public async Task<string?> GetPublicBaseUrlAsync(CancellationToken ct = default) =>
        (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == PublicBaseUrlKey, ct))?.Value;

    /// <summary>The URL shared with participants: the saved public base URL, or the address the admin is using right now.</summary>
    public async Task<string> GetShareBaseUrlAsync(HttpRequest request, CancellationToken ct = default)
    {
        var saved = await GetPublicBaseUrlAsync(ct);
        return string.IsNullOrWhiteSpace(saved) ? $"{request.Scheme}://{request.Host}" : saved;
    }

    /// <summary>Validates and stores the base URL (origin only). An empty value clears it.</summary>
    public async Task<ServiceResult> SetPublicBaseUrlAsync(string? raw, CancellationToken ct = default)
    {
        var value = (raw ?? "").Trim();
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == PublicBaseUrlKey, ct);

        if (value.Length == 0)
        {
            if (row is not null) { db.Settings.Remove(row); await db.SaveChangesAsync(ct); }
            return ServiceResult.Success();
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return ServiceResult.Fail("Enter a full address such as https://example.trycloudflare.com");
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            return ServiceResult.Fail("A public address must use https.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return ServiceResult.Fail("Enter only the address (scheme and host), without a path.");

        var normalized = uri.GetLeftPart(UriPartial.Authority);
        if (row is null) db.Settings.Add(new AppSetting { Key = PublicBaseUrlKey, Value = normalized });
        else row.Value = normalized;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }
}
