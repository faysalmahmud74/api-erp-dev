using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Data;

/// <summary>
/// Creates roles and the first Admin/Cashier accounts at startup.
/// Users must be created through UserManager (password hashing) —
/// they cannot be seeded statically in a migration.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Dev convenience: applies pending migrations on startup.
        await db.Database.MigrateAsync();

        foreach (var role in new[] { "Admin", "Cashier" })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }

        await EnsureUserAsync(users, logger,
            config["Seed:AdminEmail"]!, config["Seed:AdminPassword"]!, "Store Administrator", "Admin");
        await EnsureUserAsync(users, logger,
            config["Seed:CashierEmail"]!, config["Seed:CashierPassword"]!, "Front Counter", "Cashier");
    }

    private static async Task EnsureUserAsync(UserManager<ApplicationUser> users, ILogger logger,
        string email, string password, string fullName, string role)
    {
        if (await users.FindByEmailAsync(email) is not null)
            return;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Seeding {Email} failed: {Errors}", email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded {Role} user {Email}", role, email);
    }
}
