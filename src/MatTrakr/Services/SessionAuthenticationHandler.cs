using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>
/// Authenticates each request from the session-token cookie by looking the token
/// up in the DB and rebuilding the principal from the current user row (so role
/// or active-flag changes take effect immediately).
/// </summary>
public class SessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;

    public SessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[AuthConstants.CookieName];
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var session = await _db.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Token == token);

        if (session is null
            || session.User is null
            || !session.User.IsActive
            || (session.ExpiresDate is not null && session.ExpiresDate < DateTime.UtcNow))
        {
            return AuthenticateResult.NoResult();
        }

        var user = session.User;
        var claims = new List<Claim>
        {
            new(AuthConstants.UserIdClaim, user.Id.ToString()),
            new(AuthConstants.SessionTokenClaim, token),
            new(AuthConstants.DisplayNameClaim, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AuthConstants.RoleClaim, user.Role.ToString()),
            new(AuthConstants.MustChangePasswordClaim, user.MustChangePassword ? "1" : "0"),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = Request.Path + Request.QueryString;
        Response.Redirect("/Account/Login?returnUrl=" + UrlEncoder.Encode(returnUrl));
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/Account/AccessDenied");
        return Task.CompletedTask;
    }
}
