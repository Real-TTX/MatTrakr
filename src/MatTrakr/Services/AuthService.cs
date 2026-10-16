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

    /// <summary>
    /// Creates a session row and writes the token cookie. <paramref name="identityToken"/> is the ID token of MatIdentity
    /// for a sign-in there (kept with the session for the sign-out at MatIdentity), null for a local sign-in.
    /// </summary>
    public async Task SignInAsync(HttpContext ctx, User user, bool persistent, string? identityToken = null)
    {
        var session = new UserSession
        {
            UserId = user.Id,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresDate = DateTime.UtcNow.Add(SessionLifetime),
            IdentityToken = string.IsNullOrEmpty(identityToken) ? null : identityToken,
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

    /// <summary>
    /// Revokes the current session row and clears the cookie. Returns the ID token of MatIdentity when the session
    /// was started there (for the sign-out at MatIdentity), else null.
    /// </summary>
    public async Task<string?> SignOutAsync(HttpContext ctx)
    {
        string? identityToken = null;
        var token = ctx.Request.Cookies[AuthConstants.CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Token == token);
            if (session is not null)
            {
                identityToken = session.IdentityToken;
                _db.UserSessions.Remove(session);
                await _db.SaveChangesAsync();
            }
        }
        ctx.Response.Cookies.Delete(AuthConstants.CookieName, new CookieOptions { Path = "/" });
        return identityToken;
    }

    /// <summary>Deletes the sessions that have expired; returns how many.</summary>
    public Task<int> CleanupExpiredSessionsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.UserSessions
            .Where(s => s.ExpiresDate != null && s.ExpiresDate < now)
            .ExecuteDeleteAsync(ct);
    }

    public async Task SetPasswordAsync(User user, string newPassword, bool clearMustChange = true)
    {
        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        if (clearMustChange) user.MustChangePassword = false;
        await _db.SaveChangesAsync();
    }
}
