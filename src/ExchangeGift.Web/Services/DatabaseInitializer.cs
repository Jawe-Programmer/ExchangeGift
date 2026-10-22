using ExchangeGift.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExchangeGift.Web.Services;

/// <summary>Applies EF Core migrations and creates the administrator account on first start.</summary>
public class DatabaseInitializer(
    IServiceProvider services,
    IOptions<AdminOptions> adminOptions,
    ILogger<DatabaseInitializer> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var opts = adminOptions.Value;
        var admin = await users.FindByNameAsync(opts.UserName);

        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(opts.Password))
            {
                log.LogWarning(
                    "No administrator exists yet and Admin:Password is not configured. " +
                    "Set it with 'dotnet user-secrets set \"Admin:Password\" \"<password>\"' or the Admin__Password environment variable, then restart.");
                return;
            }
            var created = await users.CreateAsync(new IdentityUser { UserName = opts.UserName }, opts.Password);
            if (created.Succeeded) log.LogInformation("Created administrator account '{UserName}'.", opts.UserName);
            else log.LogError("Could not create the administrator: {Errors}", string.Join("; ", created.Errors.Select(e => e.Code)));
        }
        else if (opts.ForcePasswordReset && !string.IsNullOrWhiteSpace(opts.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(admin);
            var reset = await users.ResetPasswordAsync(admin, token, opts.Password);
            if (reset.Succeeded) log.LogInformation("Administrator password was reset from configuration. Remove Admin:ForcePasswordReset now.");
            else log.LogError("Could not reset the administrator password: {Errors}", string.Join("; ", reset.Errors.Select(e => e.Code)));
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
