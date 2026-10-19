using System.Security.Claims;
using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Web.Auth;

public static class AuthExtensions
{
    public const string TeacherIdClaim = "teacher_id";
    public const string OwnerRole = nameof(Role.Owner);

    public static IServiceCollection AddCookieAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/forbidden";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        return services;
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }

    /// <summary>驗證帳密，成功時回傳要寫入 Cookie 的 principal。</summary>
    public static async Task<ClaimsPrincipal?> ValidateAsync(
        AppDbContext db, IPasswordHasher<User> hasher, string userName, string password)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == userName);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        if (user.TeacherId is int teacherId)
        {
            claims.Add(new Claim(TeacherIdClaim, teacherId.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("使用者未登入。"));
        var role = Enum.Parse<Role>(principal.FindFirstValue(ClaimTypes.Role)!);
        var teacherId = principal.FindFirstValue(TeacherIdClaim) is { } t ? int.Parse(t) : (int?)null;
        return new Actor(userId, role, teacherId);
    }
}
