using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ExchangeGift.Web.Pages;

/// <summary>
/// A participant's private page at /{groupSlug}/{participantCode}. GET renders the group's theme without any
/// assignment data; the recipient is only returned by the POST reveal handler.
/// </summary>
[EnableRateLimiting("participant")]
public class ParticipantModel(RevealService reveals, IAntiforgery antiforgery) : PageModel
{
    public ParticipantLinkState State { get; private set; }
    public string GroupTitle { get; private set; } = "";
    public ThemeViewModel? Theme { get; private set; }

    public async Task<IActionResult> OnGetAsync(string groupSlug, string participantCode)
    {
        Response.Headers.CacheControl = "no-store";
        var lookup = await reveals.LookupAsync(groupSlug, participantCode);
        State = lookup.State;
        // Any mismatch of slug/code looks identical to the caller.
        if (lookup.State == ParticipantLinkState.NotFound) return NotFound();

        var group = lookup.Group!;
        GroupTitle = group.Title;
        if (lookup.State != ParticipantLinkState.Ready) return Page();

        var info = ThemeCatalog.Find(group.ThemeKey) ?? ThemeCatalog.All[0];
        var (year, eyebrow) = ThemeViewModel.SplitTitle(group.Title, group.CreatedAtUtc);
        Theme = new ThemeViewModel
        {
            Info = info,
            RevealUrl = $"/{group.Slug}/{lookup.Participant!.ParticipantCode}?handler=Reveal",
            RequestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken ?? "",
            GroupTitle = group.Title,
            ParticipantName = lookup.Participant.Name,
            Year = year,
            Eyebrow = eyebrow,
        };
        return Page();
    }

    /// <summary>Returns the recipient and records the reveal. Antiforgery-protected and idempotent.</summary>
    public async Task<IActionResult> OnPostRevealAsync(string groupSlug, string participantCode)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await reveals.RevealAsync(groupSlug, participantCode);
        if (result.State == ParticipantLinkState.Ready && result.RecipientName is not null)
            return new JsonResult(new { name = result.RecipientName, wishes = result.Wishes ?? [] });

        // A body is included so the status-code middleware leaves this JSON response alone.
        return new JsonResult(new { error = "unavailable" }) { StatusCode = StatusCodes.Status404NotFound };
    }
}
