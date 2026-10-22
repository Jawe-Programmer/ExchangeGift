using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExchangeGift.Web.Pages.Admin;

public class PasswordModel(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, ILogger<PasswordModel> log) : PageModel
{
    public List<string> Errors { get; } = new();

    public void OnGet() => Response.Headers.CacheControl = "no-store";

    public async Task<IActionResult> OnPostAsync(string? current, string? newPassword, string? confirm)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(newPassword))
            Errors.Add("Fill in every field.");
        else if (newPassword != confirm)
            Errors.Add("The new password and its confirmation do not match.");
        if (Errors.Count > 0) return Page();

        var user = await users.GetUserAsync(User);
        if (user is null) return Redirect("/admin/login");

        var result = await users.ChangePasswordAsync(user, current!, newPassword!);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) Errors.Add(Friendly(e));
            return Page();
        }

        await signIn.RefreshSignInAsync(user);
        log.LogInformation("Administrator changed the password");
        TempData["Flash"] = "Your password was changed.";
        return Redirect("/admin");
    }

    private static string Friendly(IdentityError e) => e.Code switch
    {
        "PasswordMismatch" => "The current password is not correct.",
        "PasswordTooShort" => "The new password must be at least 8 characters.",
        "PasswordRequiresDigit" => "The new password needs at least one number.",
        "PasswordRequiresLower" => "The new password needs a lowercase letter.",
        "PasswordRequiresUpper" => "The new password needs an uppercase letter.",
        _ => "The new password was rejected. Try a different one.",
    };
}
