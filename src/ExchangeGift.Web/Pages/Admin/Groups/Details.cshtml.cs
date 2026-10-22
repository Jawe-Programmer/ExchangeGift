using ExchangeGift.Web.Data;
using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Pages.Admin.Groups;

public sealed record PersonRow(int Id, string Name, string Code, DateTime? FirstRevealedAtUtc, string Link, string Wishes);

public class DetailsModel(AppDbContext db, GroupService groups, SettingsService settings, TimeProvider clock) : PageModel
{
    public ExchangeGiftGroup Group { get; private set; } = null!;
    public IReadOnlyList<PersonRow> People { get; private set; } = [];
    public string BaseUrl { get; private set; } = "";
    public string GroupUrl => $"{BaseUrl}/{Group.Slug}";
    public int RevealedCount => People.Count(p => p.FirstRevealedAtUtc is not null);
    public bool IsDraft => Group.Status == GroupStatus.Draft;
    public bool HasDraw => Group.DrawGeneratedAtUtc is not null;
    public string StatusKey { get; private set; } = "draft";
    public string? ExpiresOnValue =>
        Group.ExpiresAtUtc is { } e ? e.AddSeconds(-1).ToString("yyyy-MM-dd") : null;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Response.Headers.CacheControl = "no-store";
        return await LoadAsync(id) ? Page() : NotFound();
    }

    /// <summary>Returns one recipient name on demand for the admin eye toggle. Never rendered into the page itself.</summary>
    public async Task<IActionResult> OnGetRecipientAsync(int id, int participantId)
    {
        Response.Headers.CacheControl = "no-store";
        var name = await db.Assignments.AsNoTracking()
            .Where(a => a.GroupId == id && a.GiverParticipantId == participantId)
            .Select(a => a.Recipient.Name)
            .FirstOrDefaultAsync();
        return name is null
            ? new JsonResult(new { error = "none" }) { StatusCode = StatusCodes.Status404NotFound }
            : new JsonResult(new { name });
    }

    public async Task<IActionResult> OnPostAddAsync(int id, string? name)
    {
        var r = await groups.AddParticipantsAsync(id, [name ?? ""]);
        return Report(id, r, single: true);
    }

    public async Task<IActionResult> OnPostBulkAsync(int id, string? bulk)
    {
        var r = await groups.AddParticipantsAsync(id, GroupService.ParseNames(bulk));
        return Report(id, r, single: false);
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, int participantId, string? name) =>
        Done(id, await groups.RenameParticipantAsync(id, participantId, name), "Name updated.");

    public async Task<IActionResult> OnPostWishesAsync(int id, int participantId, string? wishes) =>
        Done(id, await groups.SetWishesAsync(id, participantId, wishes), "Wish list saved.");

    public async Task<IActionResult> OnPostRemoveAsync(int id, int participantId) =>
        Done(id, await groups.RemoveParticipantAsync(id, participantId), "Participant removed.");

    public async Task<IActionResult> OnPostDrawAsync(int id) =>
        Done(id, await groups.GenerateDrawAsync(id), "The draw has been generated. Personal links are now live.");

    public async Task<IActionResult> OnPostResetAsync(int id, string? confirm)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();
        var anyRevealed = await db.Reveals.AnyAsync(r => r.GroupId == id);
        // Once somebody has revealed, resetting needs the group's slug typed as an explicit confirmation.
        if (anyRevealed && !string.Equals(confirm?.Trim(), group.Slug, StringComparison.OrdinalIgnoreCase))
        {
            TempData["FlashError"] = "Some participants have already revealed their recipient. Type the group's URL slug to confirm the reset.";
            return Redirect($"/admin/groups/{id}");
        }
        return Done(id, await groups.ResetDrawAsync(id), "The draw was reset and all reveal records were cleared. The group is a draft again.");
    }

    public async Task<IActionResult> OnPostThemeAsync(int id, string? themeKey) =>
        Done(id, await groups.SetThemeAsync(id, themeKey), "Theme updated. Existing assignments were not changed.");

    public async Task<IActionResult> OnPostDetailsAsync(int id, string? title, string? slug, string? expiresOn)
    {
        DateTime? expires = null;
        if (!string.IsNullOrWhiteSpace(expiresOn))
        {
            if (!DateOnly.TryParse(expiresOn, out var d)) { TempData["FlashError"] = "Enter a valid date."; return Redirect($"/admin/groups/{id}"); }
            expires = DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }
        return Done(id, await groups.UpdateDetailsAsync(id, title, slug, expires), "Group details saved.");
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id) =>
        Done(id, await groups.SetArchivedAsync(id, true), "Group archived. Personal links no longer open.");

    public async Task<IActionResult> OnPostUnarchiveAsync(int id) =>
        Done(id, await groups.SetArchivedAsync(id, false), "Group restored.");

    public async Task<IActionResult> OnPostDeleteAsync(int id, string? confirm)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();
        if (!string.Equals(confirm?.Trim(), group.Slug, StringComparison.OrdinalIgnoreCase))
        {
            TempData["FlashError"] = "Type the group's URL slug to confirm deletion.";
            return Redirect($"/admin/groups/{id}");
        }
        var r = await groups.DeleteGroupAsync(id);
        if (!r.Ok) { TempData["FlashError"] = r.Error; return Redirect($"/admin/groups/{id}"); }
        TempData["Flash"] = $"Group \"{group.Title}\" was deleted.";
        return Redirect("/admin/groups");
    }

    private IActionResult Report(int id, AddParticipantsResult r, bool single)
    {
        if (r.Added > 0) TempData["Flash"] = r.Added == 1 ? "1 participant added." : $"{r.Added} participants added.";
        if (r.Problems.Count > 0) TempData["FlashError"] = string.Join(" ", r.Problems.Take(5)) + (r.Problems.Count > 5 ? $" (+{r.Problems.Count - 5} more)" : "");
        else if (r.Added == 0 && single) TempData["FlashError"] = "Enter a name to add.";
        return Redirect($"/admin/groups/{id}");
    }

    private IActionResult Done(int id, ServiceResult r, string okMessage)
    {
        if (r.Ok) TempData["Flash"] = okMessage; else TempData["FlashError"] = r.Error;
        return Redirect($"/admin/groups/{id}");
    }

    private async Task<bool> LoadAsync(int id)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return false;
        Group = group;
        BaseUrl = await settings.GetShareBaseUrlAsync(Request);
        var now = clock.GetUtcNow().UtcDateTime;
        StatusKey = group.Status == GroupStatus.Archived ? "archived"
            : group.ExpiresAtUtc is { } e && e <= now ? "expired"
            : group.Status == GroupStatus.Drawn ? "drawn" : "draft";

        var raw = await db.Participants.AsNoTracking()
            .Where(p => p.GroupId == id)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.ParticipantCode, p.Wishes,
                First = db.Reveals.Where(r => r.ParticipantId == p.Id).Select(r => (DateTime?)r.FirstRevealedAtUtc).FirstOrDefault(),
            })
            .ToListAsync();
        People = raw.Select(p => new PersonRow(p.Id, p.Name, p.ParticipantCode, p.First, $"{BaseUrl}/{group.Slug}/{p.ParticipantCode}", p.Wishes ?? "")).ToList();
        return true;
    }
}
