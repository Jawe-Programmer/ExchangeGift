using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExchangeGift.Web.Pages;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;

    public void OnGet(int? statusCode) => Set(statusCode);
    public void OnPost(int? statusCode) => Set(statusCode);

    private void Set(int? statusCode)
    {
        // Keep the original status (e.g. 404) on the re-executed response, but never leak details.
        Code = statusCode is >= 400 and < 600 ? statusCode.Value : 500;
        Response.StatusCode = Code;
    }
}
