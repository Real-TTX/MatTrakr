using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

public class DetailsModel : PageModel
{
    private const int PageSize = 24;

    private readonly AppDbContext _db;
    private readonly ListAccessService _access;
    private readonly ICurrentUser _currentUser;

    public DetailsModel(AppDbContext db, ListAccessService access, ICurrentUser currentUser)
    {
        _db = db;
        _access = access;
        _currentUser = currentUser;
    }

    public TrackList List { get; set; } = null!;
    public ListAccess Access { get; set; }
    public List<ListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public string? Q { get; set; }
    public string? StatusFilter { get; set; }
    public string Sort { get; set; } = "title";
    public int PageNumber { get; set; } = 1;
    public int PageSizeValue => PageSize;

    public bool CanEdit => Access >= ListAccess.Edit;

    /// <summary>"Gesehen"/"Gelesen" depending on list type.</summary>
    public string DoneWord => List.Type == MediaType.Books ? "Gelesen" : "Gesehen";
    public string OpenWord => List.Type == MediaType.Books ? "Ungelesen" : "Ungesehen";

    public async Task<IActionResult> OnGetAsync(long id, string? q, string? status, string? sort, int p = 1)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        Access = await _access.GetAccessAsync(userId.Value, id);
        if (Access == ListAccess.None) return Redirect("/Account/AccessDenied");

        List = (await _db.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id))!;

        Q = q;
        StatusFilter = status;
        Sort = string.IsNullOrEmpty(sort) ? "title" : sort;
        PageNumber = Math.Max(1, p);

        var query = _db.ListItems.AsNoTracking().Where(i => i.ListId == id);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(i => EF.Functions.Like(i.Title, $"%{q}%"));

        if (status == "done") query = query.Where(i => i.Status == ItemStatus.Done);
        else if (status == "open") query = query.Where(i => i.Status == ItemStatus.Open);

        TotalCount = await query.CountAsync();

        query = Sort switch
        {
            "title_desc" => query.OrderByDescending(i => i.Title),
            "year" => query.OrderByDescending(i => i.Year),
            "added" => query.OrderByDescending(i => i.CreateDate),
            _ => query.OrderBy(i => i.Title),
        };

        Items = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    /// <summary>Quick status toggle from the card grid.</summary>
    public async Task<IActionResult> OnPostToggleAsync(long id, long itemId, string? q, string? status, string? sort, int p = 1)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        if (await _access.GetAccessAsync(userId.Value, id) < ListAccess.Edit)
            return Redirect("/Account/AccessDenied");

        var item = await _db.ListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ListId == id);
        if (item is not null)
        {
            item.Status = item.Status == ItemStatus.Done ? ItemStatus.Open : ItemStatus.Done;
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { id, q, status, sort, p });
    }
}
