using ExchangeGift.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Services;

public sealed record GroupRow(
    int Id, string Title, string Slug, string ThemeKey, GroupStatus Status,
    int Participants, int Revealed, DateTime CreatedAtUtc, DateTime? DrawGeneratedAtUtc, DateTime? ExpiresAtUtc)
{
    public string ThemeName => ThemeCatalog.Find(ThemeKey)?.DisplayName ?? ThemeKey;
    public bool HasDraw => DrawGeneratedAtUtc is not null;

    public string StatusKey(DateTime utcNow) =>
        Status == GroupStatus.Archived ? "archived"
        : ExpiresAtUtc is { } e && e <= utcNow ? "expired"
        : Status == GroupStatus.Drawn ? "drawn" : "draft";

    public static string StatusLabel(string key) => key switch
    {
        "archived" => "Archived",
        "expired" => "Expired",
        "drawn" => "Draw ready",
        _ => "Draft",
    };

    public int Percent => Participants == 0 ? 0 : (int)Math.Round(100.0 * Revealed / Participants);
}

public sealed record DashboardStats(int Groups, int Participants, int Revealed, int NotRevealed);

public static class GroupQueries
{
    public static async Task<List<GroupRow>> RowsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.Groups.AsNoTracking()
            .OrderByDescending(g => g.CreatedAtUtc).ThenByDescending(g => g.Id)
            .Select(g => new GroupRow(
                g.Id, g.Title, g.Slug, g.ThemeKey, g.Status,
                g.Participants.Count(p => p.IsActive),
                db.Reveals.Count(r => r.GroupId == g.Id),
                g.CreatedAtUtc, g.DrawGeneratedAtUtc, g.ExpiresAtUtc))
            .ToListAsync(ct);
        return rows;
    }

    public static DashboardStats Stats(IReadOnlyCollection<GroupRow> rows) => new(
        rows.Count,
        rows.Sum(r => r.Participants),
        rows.Sum(r => r.Revealed),
        // "Not revealed" only makes sense once a draw exists for the group.
        rows.Where(r => r.HasDraw).Sum(r => Math.Max(0, r.Participants - r.Revealed)));
}
