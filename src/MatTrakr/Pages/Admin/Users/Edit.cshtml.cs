using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Admin.Users;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly UserService _users;
    private readonly ICurrentUser _currentUser;

    public EditModel(AppDbContext db, AuthService auth, UserService users, ICurrentUser currentUser)
    {
        _db = db;
        _auth = auth;
        _users = users;
        _currentUser = currentUser;
    }

    [BindProperty] public long Id { get; set; }
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string DisplayName { get; set; } = string.Empty;
    [BindProperty] public UserRole Role { get; set; } = UserRole.User;
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public bool MustChangePassword { get; set; } = true;

    public bool IsNew => Id == 0;
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(long? id)
    {
        if (id is null or 0) return Page(); // create mode with defaults

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return RedirectToPage("Index");

        Id = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        Role = user.Role;
        IsActive = user.IsActive;
        MustChangePassword = user.MustChangePassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            Error = "Benutzername ist erforderlich.";
            return Page();
        }

        if (await _users.UsernameExistsAsync(Username.Trim(), IsNew ? null : Id))
        {
            Error = "Der Benutzername ist bereits vergeben.";
            return Page();
        }

        if (IsNew)
        {
            if (string.IsNullOrWhiteSpace(Password) || Password.Length < 6)
            {
                Error = "Das Initialpasswort muss mindestens 6 Zeichen lang sein.";
                return Page();
            }

            var user = await _users.CreateUserAsync(Username, DisplayName, Role, Password, MustChangePassword);
            user.IsActive = IsActive;
            await _db.SaveChangesAsync();
        }
        else
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == Id);
            if (user is null) return RedirectToPage("Index");

            // Never let the last active admin demote or deactivate itself away.
            if (user.Role == UserRole.Admin && (Role != UserRole.Admin || !IsActive))
            {
                var otherAdmins = await _db.Users.CountAsync(u =>
                    u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive);
                if (otherAdmins == 0)
                {
                    Error = "Der letzte aktive Admin kann nicht herabgestuft oder deaktiviert werden.";
                    return Page();
                }
            }

            user.Username = Username.Trim();
            user.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Username.Trim() : DisplayName.Trim();
            user.Role = Role;
            user.IsActive = IsActive;
            user.MustChangePassword = MustChangePassword;

            if (!string.IsNullOrWhiteSpace(Password))
            {
                if (Password.Length < 6)
                {
                    Error = "Das neue Passwort muss mindestens 6 Zeichen lang sein.";
                    return Page();
                }
                user.PasswordHash = _auth.HashPassword(user, Password);
            }

            await _db.SaveChangesAsync();
        }

        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return RedirectToPage("Index");

        if (user.Id == _currentUser.UserId)
        {
            Error = "Du kannst dich nicht selbst löschen.";
            await OnGetAsync(id);
            return Page();
        }

        if (user.Role == UserRole.Admin &&
            !await _db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive))
        {
            Error = "Der letzte aktive Admin kann nicht gelöscht werden.";
            await OnGetAsync(id);
            return Page();
        }

        // Cascades: lists + items + sessions. Shares/assignments referencing the
        // user are restricted, so clear them explicitly first.
        var shares = _db.ListShares.Where(s => s.SharedWithUserId == user.Id);
        _db.ListShares.RemoveRange(shares);
        var assignments = _db.ListAssignments.Where(a => a.AssignedUserId == user.Id);
        _db.ListAssignments.RemoveRange(assignments);
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return RedirectToPage("Index");
    }
}
