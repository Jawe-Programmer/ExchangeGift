using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ExchangeGift.Web.Pages;

[EnableRateLimiting("participant")]
public class IndexModel : PageModel
{
    public string? Error { get; private set; }

    public void OnGet() => Response.Headers.CacheControl = "no-store";

    public IActionResult OnPost(string? group, string? code)
    {
        Response.Headers.CacheControl = "no-store";
        var slug = (group ?? "").Trim().Trim('/').ToLowerInvariant();
        var normalized = ParticipantCode.Normalize(code);
        if (!SlugService.IsWellFormed(slug) || normalized is null)
        {
            Error = "Check the group name and the 6-character code, then try again.";
            return Page();
        }
        // The participant page validates the pair and answers every mismatch with the same generic message.
        return Redirect($"/{slug}/{normalized}");
    }
}
