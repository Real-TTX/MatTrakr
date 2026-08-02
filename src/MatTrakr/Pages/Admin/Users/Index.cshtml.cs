using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Pages.Admin.Users;

public class IndexModel : PageModel
{
    private const int PageSize = 20;

    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public List<User> Users { get; set; } = new();
    public int TotalCount { get; set; }

    public string? Q { get; set; }
    public string? RoleFilter { get; set; }
    public string? ActiveFilter { get; set; }
    public string Sort { get; set; } = "username";
    public int PageNumber { get; set; } = 1;
    public int PageSizeValue => PageSize;

    public async Task OnGetAsync(string? q, string? role, string? active, string? sort, int p = 1)
    {
        Q = q;
        RoleFilter = role;
        ActiveFilter = active;
        Sort = string.IsNullOrEmpty(sort) ? "username" : sort;
        PageNumber = Math.Max(1, p);

        var query = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Username.Contains(q) || u.DisplayName.Contains(q));

        if (!string.IsNullOrEmpty(role) && Enum.TryParse<UserRole>(role, out var r))
            query = query.Where(u => u.Role == r);

        if (active == "1") query = query.Where(u => u.IsActive);
        else if (active == "0") query = query.Where(u => !u.IsActive);

        TotalCount = await query.CountAsync();

        query = Sort switch
        {
            "username_desc" => query.OrderByDescending(u => u.Username),
            "role" => query.OrderBy(u => u.Role).ThenBy(u => u.Username),
            "created" => query.OrderByDescending(u => u.CreateDate),
            _ => query.OrderBy(u => u.Username),
        };

        Users = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
