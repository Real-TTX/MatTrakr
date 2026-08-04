using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Profile;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public IndexModel(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [BindProperty] public CardStatusPosition CardStatusPosition { get; set; }
    public string? Success { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Redirect("/Account/Login");

        CardStatusPosition = user.CardStatusPosition;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Redirect("/Account/Login");

        user.CardStatusPosition = CardStatusPosition;
        await _db.SaveChangesAsync();

        Success = "Einstellungen gespeichert.";
        return Page();
    }

    private async Task<User?> CurrentUserAsync()
    {
        var userId = _currentUser.UserId;
        return userId is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    }
}
