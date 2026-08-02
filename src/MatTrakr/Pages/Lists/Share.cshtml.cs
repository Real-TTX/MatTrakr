using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

public class ShareModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ShareModel(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public TrackList List { get; set; } = null!;
    public List<ListShare> UserShares { get; set; } = new();
    public ListShare? PublicShare { get; set; }
    public List<User> ShareCandidates { get; set; } = new();

    [BindProperty] public long ShareWithUserId { get; set; }
    [BindProperty] public SharePermission Permission { get; set; } = SharePermission.ReadOnly;

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var result = await LoadAsync(id);
        return result ?? Page();
    }

    /// <summary>Adds a share for a registered user.</summary>
    public async Task<IActionResult> OnPostAddUserAsync(long id)
    {
        var result = await LoadAsync(id);
        if (result is not null) return result;

        if (ShareWithUserId == 0 || ShareCandidates.All(u => u.Id != ShareWithUserId))
        {
            Error = "Bitte einen Benutzer auswählen.";
            return Page();
        }

        _db.ListShares.Add(new ListShare
        {
            ListId = id,
            SharedWithUserId = ShareWithUserId,
            Permission = Permission,
        });
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveUserAsync(long id, long shareId)
    {
        var result = await LoadAsync(id);
        if (result is not null) return result;

        var share = await _db.ListShares.FirstOrDefaultAsync(s => s.Id == shareId && s.ListId == id);
        if (share is not null)
        {
            _db.ListShares.Remove(share);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Creates (or keeps) the public read-only link.</summary>
    public async Task<IActionResult> OnPostCreateLinkAsync(long id)
    {
        var result = await LoadAsync(id);
        if (result is not null) return result;

        if (PublicShare is null)
        {
            _db.ListShares.Add(new ListShare
            {
                ListId = id,
                Token = Guid.NewGuid().ToString("N"),
                Permission = SharePermission.ReadOnly,
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRevokeLinkAsync(long id)
    {
        var result = await LoadAsync(id);
        if (result is not null) return result;

        if (PublicShare is not null)
        {
            _db.ListShares.Remove(PublicShare);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Only the owner manages sharing.</summary>
    private async Task<IActionResult?> LoadAsync(long id)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        var list = await _db.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (list is null || list.OwnerUserId != userId)
            return Redirect("/Account/AccessDenied");
        List = list;

        var shares = await _db.ListShares
            .AsNoTracking()
            .Include(s => s.SharedWithUser)
            .Where(s => s.ListId == id)
            .ToListAsync();

        UserShares = shares.Where(s => s.SharedWithUserId != null).ToList();
        PublicShare = shares.FirstOrDefault(s => s.Token != null);

        var alreadyShared = UserShares.Select(s => s.SharedWithUserId!.Value).ToHashSet();
        ShareCandidates = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Id != userId)
            .OrderBy(u => u.Username)
            .ToListAsync();
        ShareCandidates.RemoveAll(u => alreadyShared.Contains(u.Id));

        return null;
    }
}
