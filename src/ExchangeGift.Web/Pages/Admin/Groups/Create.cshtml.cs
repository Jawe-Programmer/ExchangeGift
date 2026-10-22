using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExchangeGift.Web.Pages.Admin.Groups;

public class CreateModel(GroupService groups) : PageModel
{
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Slug { get; set; }
    [BindProperty] public string ThemeKey { get; set; } = ThemeCatalog.DefaultKey;
    [BindProperty] public string? ParticipantsText { get; set; }
    [BindProperty] public string? ExpiresOn { get; set; }
    public string? Error { get; private set; }

    public void OnGet() => Response.Headers.CacheControl = "no-store";

    public async Task<IActionResult> OnPostAsync()
    {
        DateTime? expires = null;
        if (!string.IsNullOrWhiteSpace(ExpiresOn))
        {
            if (!DateOnly.TryParse(ExpiresOn, out var d)) { Error = "Enter a valid date."; return Page(); }
            // "Stops working after <date>" means the end of that day (UTC).
            expires = DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        var result = await groups.CreateGroupAsync(Title, Slug, ThemeKey, GroupService.ParseNames(ParticipantsText), expires);
        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Flash"] = $"Group \"{result.Group!.Title}\" created. Review the participants, then generate the draw.";
        return Redirect($"/admin/groups/{result.Group.Id}");
    }
}
