using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Account;

public class ChangePasswordModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly ICurrentUser _currentUser;

    public ChangePasswordModel(AppDbContext db, AuthService auth, ICurrentUser currentUser)
    {
        _db = db;
        _auth = auth;
        _currentUser = currentUser;
    }

    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool Forced { get; set; }
    public string? Error { get; set; }

    public void OnGet()
    {
        Forced = User.FindFirst(AuthConstants.MustChangePasswordClaim)?.Value == "1";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Forced = User.FindFirst(AuthConstants.MustChangePasswordClaim)?.Value == "1";

        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Redirect("/Account/Login");

        if (await _auth.ValidateCredentialsAsync(user.Username, CurrentPassword) is null)
        {
            Error = "Das aktuelle Passwort ist falsch.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            Error = "Das neue Passwort muss mindestens 6 Zeichen lang sein.";
            return Page();
        }

        if (NewPassword != ConfirmPassword)
        {
            Error = "Die Passwörter stimmen nicht überein.";
            return Page();
        }

        await _auth.SetPasswordAsync(user, NewPassword);

        // Refresh the principal so the MustChangePassword claim clears immediately.
        await _auth.SignOutAsync(HttpContext);
        await _auth.SignInAsync(HttpContext, user, persistent: true);

        return Redirect("/");
    }
}
