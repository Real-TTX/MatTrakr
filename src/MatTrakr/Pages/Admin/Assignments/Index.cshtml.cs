using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Pages.Admin.Assignments;

public class IndexModel : PageModel
{
    private const int PageSize = 20;

    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public List<ListAssignment> Assignments { get; set; } = new();
    public int TotalCount { get; set; }

    public string? Q { get; set; }
    public string Sort { get; set; } = "list";
    public int PageNumber { get; set; } = 1;
    public int PageSizeValue => PageSize;

    public async Task OnGetAsync(string? q, string? sort, int page = 1)
    {
        Q = q;
        Sort = string.IsNullOrEmpty(sort) ? "list" : sort;
        PageNumber = Math.Max(1, page);

        var query = _db.ListAssignments
            .AsNoTracking()
            .Include(a => a.List!).ThenInclude(l => l.Owner)
            .Include(a => a.AssignedUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a =>
                a.List!.Name.Contains(q) ||
                a.AssignedUser!.Username.Contains(q) ||
                a.List!.Owner!.Username.Contains(q));

        TotalCount = await query.CountAsync();

        query = Sort switch
        {
            "verwalter" => query.OrderBy(a => a.AssignedUser!.Username),
            "owner" => query.OrderBy(a => a.List!.Owner!.Username),
            "created" => query.OrderByDescending(a => a.CreateDate),
            _ => query.OrderBy(a => a.List!.Name),
        };

        Assignments = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
