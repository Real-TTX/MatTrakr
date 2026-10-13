using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Profile;

/// <summary>Account hub: display name, display preferences and password change.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly ICurrentUser _currentUser;

    public IndexModel(AppDbContext db, AuthService auth, ICurrentUser currentUser)
    {
        _db = db;
        _auth = auth;
        _currentUser = currentUser;
    }

    public string Username { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    [BindProperty] public string DisplayName { get; set; } = string.Empty;
    [BindProperty] public CardStatusPosition CardStatusPosition { get; set; }

    [BindProperty] public string? CurrentPassword { get; set; }
    [BindProperty] public string? NewPassword { get; set; }
    [BindProperty] public string? ConfirmPassword { get; set; }

    public string? Success { get; set; }
    public string? Error { get; set; }
    public string? PasswordSuccess { get; set; }
    public string? PasswordError { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Redirect("/Account/Login");
        Load(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAccountAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Redirect("/Account/Login");

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            Load(user);
            Error = "Bitte einen Anzeigenamen angeben.";
            return Page();
        }

        user.DisplayName = DisplayName.Trim();
        user.CardStatusPosition = CardStatusPosition;
        await _db.SaveChangesAsync();

        Load(user);
        Success = "Einstellungen gespeichert.";
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Redirect("/Account/Login");
        Load(user);

        if (await _auth.ValidateCredentialsAsync(user.Username, CurrentPassword ?? "") is null)
        {
            PasswordError = "Das aktuelle Passwort ist falsch.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            PasswordError = "Das neue Passwort muss mindestens 6 Zeichen lang sein.";
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            PasswordError = "Die Passwörter stimmen nicht überein.";
            return Page();
        }

        await _auth.SetPasswordAsync(user, NewPassword);
        PasswordSuccess = "Passwort geändert.";
        return Page();
    }

    private void Load(User user)
    {
        Username = user.Username;
        RoleName = user.Role.ToString();
        DisplayName = user.DisplayName;
        CardStatusPosition = user.CardStatusPosition;
    }

    private async Task<User?> CurrentUserAsync()
    {
        var id = _currentUser.UserId;
        return id is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }
}
