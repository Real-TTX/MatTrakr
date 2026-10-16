using MatPlatform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatTrakr.Services;

namespace MatTrakr.Pages.Account;

/// <summary>
/// Local sign-in with user name and password. With MatIdentity configured (MATIDENTITY_AUTHORITY) a visit goes straight on
/// to MatIdentity; the local form stays reachable with <c>?local=1</c> (a way in for the administrator when MatIdentity is
/// down), <c>?signedOut=1</c> (back from the sign-out there) and <c>?ssoError=1</c> (the sign-in there failed) stop the
/// automatic sign-in.
/// </summary>
public class LoginModel : PageModel
{
    private readonly AuthService _auth;
    private readonly MatPlatformSettings _platform;
    private readonly ILogger<LoginModel> _log;

    public LoginModel(AuthService auth, MatPlatformSettings platform, ILogger<LoginModel> log)
    {
        _auth = auth;
        _platform = platform;
        _log = log;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }

    /// <summary><c>?local=1</c>: the local form even when MatIdentity is configured.</summary>
    public bool Local { get; private set; }

    /// <summary><c>?signedOut=1</c>: back from the sign-out at MatIdentity, no automatic sign-in.</summary>
    public bool SignedOut { get; private set; }

    /// <summary><c>?ssoError=1</c>: the sign-in at MatIdentity failed or MatIdentity is not reachable.</summary>
    public bool SsoError { get; private set; }

    /// <summary>True when the sign-in through MatIdentity is configured.</summary>
    public bool IdentityEnabled => _platform.IdentityEnabled;

    /// <summary>True when the page shows the local form (always without MatIdentity).</summary>
    public bool ShowLocalForm => !IdentityEnabled || Local;

    /// <summary>The address that starts the sign-in at MatIdentity and comes back to <see cref="ReturnUrl"/>.</summary>
    public string IdentitySignInUrl => string.IsNullOrWhiteSpace(ReturnUrl)
        ? "/Account/Login"
        : "/Account/Login?returnUrl=" + Uri.EscapeDataString(ReturnUrl);

    /// <summary>The address of the local form, with the same <see cref="ReturnUrl"/>.</summary>
    public string LocalSignInUrl => string.IsNullOrWhiteSpace(ReturnUrl)
        ? "/Account/Login?local=1"
        : "/Account/Login?local=1&returnUrl=" + Uri.EscapeDataString(ReturnUrl);

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(SafeReturnUrl(returnUrl));

        ReturnUrl = returnUrl;
        ReadFlags();

        // Auto login: straight on to MatIdentity, no local form in the way.
        if (IdentityEnabled && !Local && !SignedOut && !SsoError)
        {
            try
            {
                await HttpContext.ChallengeAsync(
                    MatIdentitySignIn.Scheme,
                    new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) });
                return new EmptyResult();
            }
            catch (InvalidOperationException ex)
            {
                // MatIdentity cannot be reached (its configuration cannot be read): the local form is the way in.
                _log.LogWarning(ex, "MatIdentity is not reachable, showing the local sign-in");
                Local = true;
                SsoError = true;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        // A posted form is the local sign-in; on an error it shows the form again.
        ReturnUrl = returnUrl;
        ReadFlags();
        Local = true;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Bitte Benutzername und Passwort eingeben.";
            return Page();
        }

        var user = await _auth.ValidateCredentialsAsync(Username, Password);
        if (user is null)
        {
            Error = "Benutzername oder Passwort ist falsch.";
            return Page();
        }

        await _auth.SignInAsync(HttpContext, user, RememberMe);

        if (user.MustChangePassword)
            return Redirect("/Account/ChangePassword");

        return Redirect(SafeReturnUrl(returnUrl));
    }

    private void ReadFlags()
    {
        Local = QueryFlag("local");
        SignedOut = QueryFlag("signedOut");
        SsoError = QueryFlag("ssoError");
    }

    /// <summary>A flag in the query: <c>1</c> or <c>true</c>.</summary>
    private bool QueryFlag(string name) => Request.Query[name].ToString() is "1" or "true" or "True";

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
