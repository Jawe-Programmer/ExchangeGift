using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ExchangeGift.Web.Pages.Admin;

[AllowAnonymous]
[EnableRateLimiting("login")]
public class LoginModel(SignInManager<IdentityUser> signIn, ILogger<LoginModel> log) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public string? Username { get; set; }
    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        return User.Identity?.IsAuthenticated == true ? LocalRedirect("/admin") : Page();
    }

    public async Task<IActionResult> OnPostAsync(string? password)
    {
        Response.Headers.CacheControl = "no-store";
        var name = (Username ?? "").Trim();
        if (name.Length == 0 || string.IsNullOrEmpty(password))
        {
            Error = "Enter your username and password.";
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(name, password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            log.LogInformation("Administrator signed in");
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) && ReturnUrl!.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ? ReturnUrl : "/admin");
        }

        log.LogWarning("Failed administrator sign-in attempt (lockedOut={LockedOut})", result.IsLockedOut);
        Error = result.IsLockedOut
            ? "Too many failed attempts. Please wait ten minutes and try again."
            : "Invalid username or password.";
        return Page();
    }
}
