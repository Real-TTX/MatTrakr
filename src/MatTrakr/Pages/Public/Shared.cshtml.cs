using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Pages.Public;

/// <summary>Anonymous read-only view of a list shared via public token link.</summary>
public class SharedModel : PageModel
{
    private const int PageSize = 24;

    private readonly AppDbContext _db;

    public SharedModel(AppDbContext db) => _db = db;

    public TrackList List { get; set; } = null!;
    public List<ListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public string Token { get; set; } = string.Empty;

    public string? Q { get; set; }
    public string? StatusFilter { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSizeValue => PageSize;

    public string DoneWord => List.Type == MediaType.Books ? "Gelesen" : "Gesehen";
    public string OpenWord => List.Type == MediaType.Books ? "Ungelesen" : "Ungesehen";

    public async Task<IActionResult> OnGetAsync(string token, string? q, string? status, int p = 1)
    {
        var share = await _db.ListShares
            .AsNoTracking()
            .Include(s => s.List)
            .FirstOrDefaultAsync(s => s.Token == token);

        if (share?.List is null) return NotFound();

        List = share.List;
        Token = token;
        Q = q;
        StatusFilter = status;
        PageNumber = Math.Max(1, p);

        var query = _db.ListItems.AsNoTracking().Where(i => i.ListId == List.Id);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(i => EF.Functions.Like(i.Title, $"%{q}%"));

        if (status == "done") query = query.Where(i => i.Status == ItemStatus.Done);
        else if (status == "open") query = query.Where(i => i.Status == ItemStatus.Open);

        TotalCount = await query.CountAsync();

        Items = await query
            .OrderBy(i => i.Title)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }
}
