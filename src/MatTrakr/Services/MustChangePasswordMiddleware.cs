namespace MatTrakr.Services;

/// <summary>
/// While a user is flagged MustChangePassword, funnel every request to the
/// change-password page (logout and static assets stay reachable).
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var mustChange = ctx.User?.FindFirst(AuthConstants.MustChangePasswordClaim)?.Value == "1";
        if (mustChange)
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            var allowed = path.StartsWith("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase);

            if (!allowed)
            {
                ctx.Response.Redirect("/Account/ChangePassword");
                return;
            }
        }
        await _next(ctx);
    }
}
