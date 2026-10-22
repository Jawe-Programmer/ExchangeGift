namespace ExchangeGift.Web.Services;

/// <summary>
/// Bound from the "Admin" configuration section. Supply <c>Admin:Password</c> through .NET User Secrets
/// or the <c>Admin__Password</c> environment variable; it is never read from source files.
/// </summary>
public class AdminOptions
{
    public string UserName { get; set; } = "Administrator";
    public string? Password { get; set; }

    /// <summary>When true, the stored administrator password is replaced with <see cref="Password"/> at startup.</summary>
    public bool ForcePasswordReset { get; set; }
}
