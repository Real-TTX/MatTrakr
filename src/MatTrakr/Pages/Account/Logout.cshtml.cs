using MatPlatform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatTrakr.Services;

namespace MatTrakr.Pages.Account;

/// <summary>Ends the session; after a sign-in through MatIdentity the session there ends too.</summary>
public class LogoutModel : PageModel
{
    private readonly AuthService _auth;
    private readonly MatPlatformSettings _platform;

    public LogoutModel(AuthService auth, MatPlatformSettings platform)
    {
        _auth = auth;
        _platform = platform;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var identityToken = await _auth.SignOutAsync(HttpContext);

        if (_platform.IdentityEnabled && !string.IsNullOrEmpty(identityToken))
        {
            // Then at MatIdentity as well; it sends the browser back to /signout-callback-oidc and on to /Account/Login?signedOut=1.
            return MatIdentitySignIn.SignOutAtIdentity(HttpContext, identityToken);
        }

        return Redirect(_platform.IdentityEnabled ? "/Account/Login?signedOut=1" : "/Account/Login");
    }

    // Convenience: allow GET logout too (e.g. direct link).
    public Task<IActionResult> OnGetAsync() => OnPostAsync();
}
