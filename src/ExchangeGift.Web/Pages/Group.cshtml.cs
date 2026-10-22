using ExchangeGift.Web.Data;
using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Pages;

/// <summary>Public landing page of a group. It shows only the title: never the roster or any assignment.</summary>
[EnableRateLimiting("participant")]
public class GroupModel(AppDbContext db) : PageModel
{
    public string GroupTitle { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string groupSlug)
    {
        Response.Headers.CacheControl = "no-store";
        return await LoadAsync(groupSlug) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(string groupSlug, string? code)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await LoadAsync(groupSlug)) return NotFound();

        var normalized = ParticipantCode.Normalize(code);
        if (normalized is null)
        {
            Error = "That code should be six letters or numbers.";
            return Page();
        }
        return Redirect($"/{Uri.EscapeDataString(groupSlug.ToLowerInvariant())}/{normalized}");
    }

    private async Task<bool> LoadAsync(string groupSlug)
    {
        var slug = groupSlug.ToLowerInvariant();
        if (!SlugService.IsWellFormed(slug)) return false;
        var title = await db.Groups.AsNoTracking().Where(g => g.Slug == slug).Select(g => g.Title).FirstOrDefaultAsync();
        if (title is null) return false;
        GroupTitle = title;
        return true;
    }
}
