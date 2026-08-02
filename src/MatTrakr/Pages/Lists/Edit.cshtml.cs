using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

/// <summary>Create a new own list, or rename/delete an existing one (owner only).</summary>
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public EditModel(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [BindProperty] public long Id { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public MediaType Type { get; set; } = MediaType.Movies;

    public bool IsNew => Id == 0;
    public bool IsDefault { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(long? id)
    {
        if (id is null or 0) return Page();

        var list = await LoadOwnedAsync(id.Value);
        if (list is null) return Redirect("/Account/AccessDenied");

        Id = list.Id;
        Name = list.Name;
        Type = list.Type;
        IsDefault = list.IsDefault;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Bitte einen Namen angeben.";
            return Page();
        }

        if (IsNew)
        {
            var list = new TrackList { Name = Name.Trim(), Type = Type, OwnerUserId = userId.Value };
            _db.Lists.Add(list);
            await _db.SaveChangesAsync();
            return Redirect($"/Lists/{list.Id}");
        }

        var existing = await LoadOwnedAsync(Id);
        if (existing is null) return Redirect("/Account/AccessDenied");

        IsDefault = existing.IsDefault;
        existing.Name = Name.Trim();
        // Type stays fixed once items may exist — keeps the move rule intact.
        await _db.SaveChangesAsync();
        return Redirect($"/Lists/{Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var list = await LoadOwnedAsync(id);
        if (list is null) return Redirect("/Account/AccessDenied");

        if (list.IsDefault)
        {
            Error = "Die Standard-Listen können nicht gelöscht werden.";
            Id = list.Id;
            Name = list.Name;
            Type = list.Type;
            IsDefault = true;
            return Page();
        }

        _db.Lists.Remove(list); // cascades items, shares, assignments
        await _db.SaveChangesAsync();
        return Redirect("/");
    }

    private async Task<TrackList?> LoadOwnedAsync(long id)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return null;
        return await _db.Lists.FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == userId);
    }
}
