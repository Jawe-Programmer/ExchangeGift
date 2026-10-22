using ExchangeGift.Web.Data;
using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExchangeGift.Web.Pages.Admin;

public class IndexModel(AppDbContext db, SettingsService settings) : PageModel
{
    public IReadOnlyList<GroupRow> Rows { get; private set; } = [];
    public DashboardStats Stats { get; private set; } = new(0, 0, 0, 0);
    public string? SavedBaseUrl { get; private set; }
    public string EffectiveBaseUrl { get; private set; } = "";

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        var all = await GroupQueries.RowsAsync(db);
        Stats = GroupQueries.Stats(all);
        Rows = all.Take(5).ToList();
        SavedBaseUrl = await settings.GetPublicBaseUrlAsync();
        EffectiveBaseUrl = await settings.GetShareBaseUrlAsync(Request);
    }

    public async Task<IActionResult> OnPostBaseUrlAsync(string? baseUrl)
    {
        var result = await settings.SetPublicBaseUrlAsync(baseUrl);
        if (result.Ok) TempData["Flash"] = string.IsNullOrWhiteSpace(baseUrl) ? "Sharing address cleared." : "Sharing address saved.";
        else TempData["FlashError"] = result.Error;
        return RedirectToPage();
    }
}
