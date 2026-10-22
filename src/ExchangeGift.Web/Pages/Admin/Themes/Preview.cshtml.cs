using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExchangeGift.Web.Pages.Admin.Themes;

/// <summary>
/// Administrator-only sandbox of a theme with an obvious sample name. It never talks to the reveal endpoint,
/// so it records nothing and exposes no real assignment.
/// </summary>
public class PreviewModel : PageModel
{
    public ThemeViewModel Theme { get; private set; } = null!;

    public IActionResult OnGet(string key)
    {
        Response.Headers.CacheControl = "no-store";
        var info = ThemeCatalog.Find(key);
        if (info is null) return NotFound();

        var title = "Sample Family 2026";
        var (year, eyebrow) = ThemeViewModel.SplitTitle(title, DateTime.UtcNow);
        Theme = new ThemeViewModel
        {
            Info = info,
            IsPreview = true,
            GroupTitle = title,
            ParticipantName = "Sample Santa",
            Year = year,
            Eyebrow = eyebrow,
        };
        return Page();
    }
}
