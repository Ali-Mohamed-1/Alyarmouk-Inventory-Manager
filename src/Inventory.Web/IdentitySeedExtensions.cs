using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web;

public static class IdentitySeedExtensions
{
    public static async Task SeedIdentityAsync(this WebApplication app)
    {
        // Keep seeding behind a flag so production can't accidentally create test users.
        var seedEnabled = app.Configuration.GetValue<bool>("IdentitySeed:Enabled");
        if (!seedEnabled)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        var users = app.Configuration.GetSection("IdentitySeed:Users").Get<List<SeedUser>>() ?? [];
        foreach (var u in users)
        {
            if (string.IsNullOrWhiteSpace(u.UserName) || string.IsNullOrWhiteSpace(u.Password))
                continue;

            var existing = await userManager.FindByNameAsync(u.UserName);
            if (existing is null)
            {
                var created = new AppUser
                {
                    UserName = u.UserName,
                    Email = u.UserName,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(created, u.Password);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
                    throw new InvalidOperationException($"Failed to create seeded user '{u.UserName}'. {errors}");
                }

                existing = created;
            }

            if (!string.IsNullOrWhiteSpace(u.DisplayName))
            {
                var existingClaims = await userManager.GetClaimsAsync(existing);
                if (!existingClaims.Any(c => c.Type == "display_name"))
                {
                    await userManager.AddClaimAsync(existing, new System.Security.Claims.Claim("display_name", u.DisplayName));
                }
            }
        }
    }

    private sealed class SeedUser
    {
        public string? DisplayName { get; init; }
        public string? UserName { get; init; }
        public string? Password { get; init; }
    }
}

