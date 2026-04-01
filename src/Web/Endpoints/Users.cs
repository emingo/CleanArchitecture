using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchitecture.Web.Endpoints;

public class Users : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register")
            .WithSummary("Register")
            .WithDescription("Creates a new user account.");

        groupBuilder.MapPost(Login, "login")
            .WithSummary("Log in")
            .WithDescription("Authenticates a user and returns a JWT token (API) or sets a cookie (SPA).");

        groupBuilder.MapPost(Logout, "logout")
            .RequireAuthorization()
            .WithSummary("Log out")
            .WithDescription("Logs out the current user.");

#if (UseApiOnly)
        groupBuilder.MapPost(Refresh, "refresh")
            .WithSummary("Refresh token")
            .WithDescription("Returns a new access token using a valid refresh token.");
#endif

        groupBuilder.MapPost(ConfirmEmail, "confirmEmail")
            .WithSummary("Confirm email")
            .WithDescription("Confirms a user's email address using the provided token.");

        groupBuilder.MapPost(ResendConfirmationEmail, "resendConfirmationEmail")
            .WithSummary("Resend confirmation email")
            .WithDescription("Sends a new email confirmation link to the specified address.");

        groupBuilder.MapPost(ForgotPassword, "forgotPassword")
            .WithSummary("Forgot password")
            .WithDescription("Sends a password reset link to the specified email address.");

        groupBuilder.MapPost(ResetPassword, "resetPassword")
            .WithSummary("Reset password")
            .WithDescription("Resets a user's password using the provided reset token.");

        groupBuilder.MapPost(Manage2fa, "manage/2fa")
            .RequireAuthorization()
            .WithSummary("Manage two-factor authentication")
            .WithDescription("Enables, disables, or retrieves two-factor authentication settings.");

        groupBuilder.MapGet(GetInfo, "manage/info")
            .RequireAuthorization()
            .WithSummary("Get account info")
            .WithDescription("Returns the current user's email and two-factor authentication status.");

        groupBuilder.MapPost(UpdateInfo, "manage/info")
            .RequireAuthorization()
            .WithSummary("Update account info")
            .WithDescription("Updates the current user's email or password.");
    }

    // --- Request/Response records ---

    public record RegisterRequest(string Email, string Password);
    public record LoginRequest(string Email, string Password);
    public record RefreshRequest(string RefreshToken);
    public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
    public record ConfirmEmailRequest(string UserId, string Token);
    public record ResendConfirmationEmailRequest(string Email);
    public record ForgotPasswordRequest(string Email);
    public record ResetPasswordRequest(string Email, string Token, string NewPassword);
    public record Manage2faRequest(bool? Enable, string? TwoFactorCode, bool ResetSharedKey = false);
    public record Manage2faResponse(bool IsEnabled, bool IsMachineRemembered, int RecoveryCodesLeft);
    public record InfoResponse(string Email, bool IsEmailConfirmed, bool TwoFactorEnabled);
    public record UpdateInfoRequest(string? NewEmail, string? OldPassword, string? NewPassword);

    // --- Endpoint handlers ---

    public static async Task<Results<Ok, ValidationProblem>> Register(
        RegisterRequest request,
        ApplicationDbContext context,
        IPasswordHasher<ApplicationUser> passwordHasher)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Email and password are required."]
            });
        }

        if (await context.Users.AnyAsync(u => u.NormalizedEmail == request.Email.ToUpperInvariant()))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Email"] = ["A user with this email already exists."]
            });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            NormalizedUserName = request.Email.ToUpperInvariant(),
            Email = request.Email,
            NormalizedEmail = request.Email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok<TokenResponse>, SignInHttpResult, UnauthorizedHttpResult>> Login(
        LoginRequest request,
        ApplicationDbContext context,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IConfiguration configuration,
        HttpContext httpContext)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == request.Email.ToUpperInvariant());

        if (user?.PasswordHash == null)
        {
            return TypedResults.Unauthorized();
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            return TypedResults.Unauthorized();
        }

        // Rehash if needed (password hasher versioning)
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await context.SaveChangesAsync();
        }

        var roles = await GetUserRoles(context, user.Id);
        var claims = BuildClaims(user, roles);

#if (UseApiOnly)
        var accessToken = GenerateJwtToken(claims, configuration);
        var refreshToken = GenerateRefreshToken();
        var expiryMinutes = int.Parse(configuration["Jwt:ExpiryInMinutes"] ?? "60");

        // Store refresh token
        user.SecurityStamp = refreshToken;
        await context.SaveChangesAsync();

        return TypedResults.Ok(new TokenResponse(accessToken, refreshToken, expiryMinutes * 60));
#else
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return TypedResults.SignIn(principal);
#endif
    }

    public static async Task<Results<Ok, UnauthorizedHttpResult>> Logout(
        HttpContext httpContext)
    {
#if (UseApiOnly)
        // For JWT, the client simply discards the token.
        // Optionally invalidate the refresh token server-side.
        return TypedResults.Ok();
#else
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.Ok();
#endif
    }

#if (UseApiOnly)
    public static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> Refresh(
        RefreshRequest request,
        ApplicationDbContext context,
        IConfiguration configuration)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.SecurityStamp == request.RefreshToken);

        if (user == null)
        {
            return TypedResults.Unauthorized();
        }

        var roles = await GetUserRoles(context, user.Id);
        var claims = BuildClaims(user, roles);

        var accessToken = GenerateJwtToken(claims, configuration);
        var newRefreshToken = GenerateRefreshToken();
        var expiryMinutes = int.Parse(configuration["Jwt:ExpiryInMinutes"] ?? "60");

        user.SecurityStamp = newRefreshToken;
        await context.SaveChangesAsync();

        return TypedResults.Ok(new TokenResponse(accessToken, newRefreshToken, expiryMinutes * 60));
    }
#endif

    public static Ok ConfirmEmail(ConfirmEmailRequest request)
    {
        // TODO: Implement email confirmation with your email service.
        // Verify the token, then set user.EmailConfirmed = true via DbContext.
        return TypedResults.Ok();
    }

    public static Ok ResendConfirmationEmail(ResendConfirmationEmailRequest request)
    {
        // TODO: Implement with your email service.
        // Generate a new confirmation token and send it via email.
        return TypedResults.Ok();
    }

    public static Ok ForgotPassword(ForgotPasswordRequest request)
    {
        // TODO: Implement with your email service.
        // Generate a password reset token, store it, and send via email.
        return TypedResults.Ok();
    }

    public static Ok ResetPassword(ResetPasswordRequest request)
    {
        // TODO: Implement password reset.
        // Validate the token, then update user.PasswordHash via DbContext + PasswordHasher.
        return TypedResults.Ok();
    }

    public static async Task<Results<Ok<Manage2faResponse>, UnauthorizedHttpResult>> Manage2fa(
        Manage2faRequest request,
        HttpContext httpContext,
        ApplicationDbContext context)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return TypedResults.Unauthorized();

        var user = await context.Users.FindAsync(userId);
        if (user == null) return TypedResults.Unauthorized();

        if (request.ResetSharedKey)
        {
            user.TwoFactorEnabled = false;
            await context.SaveChangesAsync();
        }
        else if (request.Enable.HasValue)
        {
            user.TwoFactorEnabled = request.Enable.Value;
            await context.SaveChangesAsync();
        }

        return TypedResults.Ok(new Manage2faResponse(
            user.TwoFactorEnabled,
            false,
            0));
    }

    public static async Task<Results<Ok<InfoResponse>, UnauthorizedHttpResult>> GetInfo(
        HttpContext httpContext,
        ApplicationDbContext context)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return TypedResults.Unauthorized();

        var user = await context.Users.FindAsync(userId);
        if (user == null) return TypedResults.Unauthorized();

        return TypedResults.Ok(new InfoResponse(
            user.Email ?? "",
            user.EmailConfirmed,
            user.TwoFactorEnabled));
    }

    public static async Task<Results<Ok, UnauthorizedHttpResult, ValidationProblem>> UpdateInfo(
        UpdateInfoRequest request,
        HttpContext httpContext,
        ApplicationDbContext context,
        IPasswordHasher<ApplicationUser> passwordHasher)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return TypedResults.Unauthorized();

        var user = await context.Users.FindAsync(userId);
        if (user == null) return TypedResults.Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.NewEmail))
        {
            user.Email = request.NewEmail;
            user.NormalizedEmail = request.NewEmail.ToUpperInvariant();
            user.UserName = request.NewEmail;
            user.NormalizedUserName = request.NewEmail.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(request.OldPassword) || user.PasswordHash == null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["OldPassword"] = ["Current password is required to set a new password."]
                });
            }

            var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.OldPassword);
            if (verify == PasswordVerificationResult.Failed)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["OldPassword"] = ["Incorrect password."]
                });
            }

            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        await context.SaveChangesAsync();
        return TypedResults.Ok();
    }

    // --- Helpers ---

    private static List<Claim> BuildClaims(ApplicationUser user, List<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? ""),
            new(ClaimTypes.Email, user.Email ?? ""),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return claims;
    }

    private static async Task<List<string>> GetUserRoles(ApplicationDbContext context, string userId)
    {
        return await context.Set<IdentityUserRole<string>>()
            .Where(ur => ur.UserId == userId)
            .Join(context.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync();
    }

#if (UseApiOnly)
    private static string GenerateJwtToken(List<Claim> claims, IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = int.Parse(configuration["Jwt:ExpiryInMinutes"] ?? "60");

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
#endif
}
