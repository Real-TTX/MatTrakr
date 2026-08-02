using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>
/// Local login and DB-backed session handling. The auth cookie only ever carries
/// the opaque session token; the <see cref="UserSession"/> row is the source of
/// truth, so logins survive a container restart and can be revoked instantly.
/// </summary>
public class AuthService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthService(AppDbContext db) => _db = db;

    public string HashPassword(User user, string password) => _hasher.HashPassword(user, password);

    /// <summary>Returns the active user if the credentials are valid, otherwise null.</summary>
    public async Task<User?> ValidateCredentialsAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null) return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
            await _db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>Creates a session row and writes the token cookie.</summary>
    public async Task SignInAsync(HttpContext ctx, User user, bool persistent)
    {
        var session = new UserSession
        {
            UserId = user.Id,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresDate = DateTime.UtcNow.Add(SessionLifetime),
        };
        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();

        ctx.Response.Cookies.Append(AuthConstants.CookieName, session.Token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Expires = persistent ? session.ExpiresDate : null,
            Path = "/",
        });
    }

    /// <summary>Revokes the current session row and clears the cookie.</summary>
    public async Task SignOutAsync(HttpContext ctx)
    {
        var token = ctx.Request.Cookies[AuthConstants.CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Token == token);
            if (session is not null)
            {
                _db.UserSessions.Remove(session);
                await _db.SaveChangesAsync();
            }
        }
        ctx.Response.Cookies.Delete(AuthConstants.CookieName, new CookieOptions { Path = "/" });
    }

    public async Task SetPasswordAsync(User user, string newPassword, bool clearMustChange = true)
    {
        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        if (clearMustChange) user.MustChangePassword = false;
        await _db.SaveChangesAsync();
    }
}
