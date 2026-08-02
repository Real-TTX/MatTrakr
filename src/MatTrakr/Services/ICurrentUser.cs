namespace MatTrakr.Services;

/// <summary>
/// Ambient accessor for the signed-in user's id, used by the DbContext to stamp
/// Create/Update audit columns without every call site passing it explicitly.
/// </summary>
public interface ICurrentUser
{
    long? UserId { get; }
}

/// <summary>Default implementation backed by the authenticated HttpContext claims.</summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http) => _http = http;

    public long? UserId
    {
        get
        {
            var value = _http.HttpContext?.User?.FindFirst(AuthConstants.UserIdClaim)?.Value;
            return long.TryParse(value, out var id) ? id : null;
        }
    }
}

/// <summary>Shared constants for the custom cookie authentication.</summary>
public static class AuthConstants
{
    public const string Scheme = "MatTrakrCookie";
    public const string UserIdClaim = "mt_uid";
    public const string SessionTokenClaim = "mt_token";
    public const string RoleClaim = "mt_role";
    public const string DisplayNameClaim = "mt_name";
    public const string CookieName = "MatTrakr.Session";
}
