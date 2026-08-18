using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

/// <summary>Central overview of the user's own lists: rename, delete, create.</summary>
public class ManageModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ManageModel(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public record Row(TrackList List, int Total, int Done);

    public List<Row> Rows { get; set; } = new();
    public string? Q { get; set; }
    public string? TypeFilter { get; set; }
    public string Sort { get; set; } = "order";
    public string? Error { get; set; }

    /// <summary>Up/down reordering only makes sense in the unfiltered manual-order view.</summary>
    public bool CanReorder => Sort == "order"
        && string.IsNullOrEmpty(Q) && string.IsNullOrEmpty(TypeFilter);

    public async Task<IActionResult> OnGetAsync(string? q, string? type, string? sort)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        await LoadAsync(userId.Value, q, type, sort);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleFavoriteAsync(long id, string? q, string? type, string? sort)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        var list = await _db.Lists.FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == userId);
        if (list is null) return Redirect("/Account/AccessDenied");

        list.IsFavorite = !list.IsFavorite;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { q, type, sort });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, string? q, string? type, string? sort)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        var list = await _db.Lists.FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == userId);
        if (list is null) return Redirect("/Account/AccessDenied");

        _db.Lists.Remove(list); // cascades items, shares, assignments
        await _db.SaveChangesAsync();
        return RedirectToPage(new { q, type, sort });
    }

    /// <summary>Moves a list one step up/down in the manual order (swaps with its neighbour).</summary>
    public async Task<IActionResult> OnPostMoveAsync(long id, string dir, string? q, string? type, string? sort)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        var lists = await _db.Lists
            .Where(l => l.OwnerUserId == userId)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .ToListAsync();

        var idx = lists.FindIndex(l => l.Id == id);
        var swap = dir == "up" ? idx - 1 : idx + 1;
        if (idx >= 0 && swap >= 0 && swap < lists.Count)
        {
            // Normalise to clean indices first, then swap the two positions.
            for (var i = 0; i < lists.Count; i++) lists[i].SortOrder = i;
            (lists[idx].SortOrder, lists[swap].SortOrder) = (lists[swap].SortOrder, lists[idx].SortOrder);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { q, type, sort });
    }

    private async Task LoadAsync(long userId, string? q, string? type, string? sort)
    {
        Q = q;
        TypeFilter = type;
        Sort = string.IsNullOrEmpty(sort) ? "order" : sort;

        var query = _db.Lists.AsNoTracking().Where(l => l.OwnerUserId == userId);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(l => EF.Functions.Like(l.Name, $"%{q}%"));

        if (Enum.TryParse<MediaType>(type, out var mt) && !string.IsNullOrEmpty(type))
            query = query.Where(l => l.Type == mt);

        query = Sort switch
        {
            "name" => query.OrderBy(l => l.Name),
            "name_desc" => query.OrderByDescending(l => l.Name),
            "type" => query.OrderBy(l => l.Type).ThenBy(l => l.Name),
            _ => query.OrderBy(l => l.SortOrder).ThenBy(l => l.Name),
        };

        var lists = await query.ToListAsync();
        var ids = lists.Select(l => l.Id).ToList();

        var counts = await _db.ListItems
            .Where(i => ids.Contains(i.ListId))
            .GroupBy(i => i.ListId)
            .Select(g => new { ListId = g.Key, Total = g.Count(), Done = g.Count(i => i.Status == ItemStatus.Done) })
            .ToDictionaryAsync(x => x.ListId);

        Rows = lists.Select(l =>
        {
            counts.TryGetValue(l.Id, out var c);
            return new Row(l, c?.Total ?? 0, c?.Done ?? 0);
        }).ToList();
    }
}
