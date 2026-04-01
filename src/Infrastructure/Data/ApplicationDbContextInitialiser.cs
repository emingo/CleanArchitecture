using CleanArchitecture.Domain.Constants;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
        await initialiser.SeedAsync();
    }
}

public class ApplicationDbContextInitialiser(
    ILogger<ApplicationDbContextInitialiser> logger,
    ApplicationDbContext context,
    IPasswordHasher<ApplicationUser> passwordHasher)
{
    public async Task InitialiseAsync()
    {
        try
        {
            // See https://jasontaylor.dev/ef-core-database-initialisation-strategies
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        // Default roles
        if (!await context.Roles.AnyAsync(r => r.Name == Roles.Administrator))
        {
            context.Roles.Add(new IdentityRole
            {
                Name = Roles.Administrator,
                NormalizedName = Roles.Administrator.ToUpperInvariant(),
            });
            await context.SaveChangesAsync();
        }

        // Default users
        if (!await context.Users.AnyAsync(u => u.UserName == "administrator@localhost"))
        {
            var admin = new ApplicationUser
            {
                UserName = "administrator@localhost",
                NormalizedUserName = "ADMINISTRATOR@LOCALHOST",
                Email = "administrator@localhost",
                NormalizedEmail = "ADMINISTRATOR@LOCALHOST",
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            admin.PasswordHash = passwordHasher.HashPassword(admin, "Administrator1!");

            context.Users.Add(admin);
            await context.SaveChangesAsync();

            // Assign admin role
            var roleId = await context.Roles
                .Where(r => r.Name == Roles.Administrator)
                .Select(r => r.Id)
                .FirstAsync();

            context.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string>
            {
                UserId = admin.Id,
                RoleId = roleId,
            });
            await context.SaveChangesAsync();
        }

        // Default data
        // Seed, if necessary
        if (!context.TodoLists.Any())
        {
            context.TodoLists.Add(new TodoList
            {
                Title = "Tasks",
                Colour = Colour.Green,
                Items =
                {
                    new TodoItem { Title = "Make a todo list 📃" },
                    new TodoItem { Title = "Check off the first item ✅" },
                    new TodoItem { Title = "Realise you've already done two things on the list! 🤯"},
                    new TodoItem { Title = "Reward yourself with a nice, long nap 🏆" },
                }
            });

            await context.SaveChangesAsync();
        }
    }
}
