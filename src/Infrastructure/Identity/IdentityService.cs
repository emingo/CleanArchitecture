using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Identity;

public class IdentityService(
    ApplicationDbContext context,
    IPasswordHasher<ApplicationUser> passwordHasher) : IIdentityService
{
    public async Task<string?> GetUserNameAsync(string userId)
    {
        return await context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync();
    }

    public async Task<(Result Result, string UserId)> CreateUserAsync(string userName, string password)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        user.PasswordHash = passwordHasher.HashPassword(user, password);

        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync();
            return (Result.Success(), user.Id);
        }
        catch (DbUpdateException)
        {
            return (Result.Failure(["A user with this name already exists."]), user.Id);
        }
    }

    public async Task<Result> DeleteUserAsync(string userId)
    {
        var user = await context.Users.FindAsync(userId);

        if (user == null)
        {
            return Result.Success();
        }

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        return Result.Success();
    }
}
