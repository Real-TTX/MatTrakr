using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages;

public class IndexModel : PageModel
{
    private readonly ListAccessService _access;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public IndexModel(ListAccessService access, AppDbContext db, ICurrentUser currentUser)
    {
        _access = access;
        _db = db;
        _currentUser = currentUser;
    }

    public record ListCard(TrackList List, ListAccess Access, int Total, int Done);

    public List<ListCard> Cards { get; set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _currentUser.UserId;
        if (userId is null) return;

        var visible = await _access.GetVisibleListsAsync(userId.Value);
        var listIds = visible.Select(v => v.List.Id).ToList();

        var counts = await _db.ListItems
            .Where(i => listIds.Contains(i.ListId))
            .GroupBy(i => i.ListId)
            .Select(g => new
            {
                ListId = g.Key,
                Total = g.Count(),
                Done = g.Count(i => i.Status == ItemStatus.Done),
            })
            .ToDictionaryAsync(x => x.ListId);

        Cards = visible.Select(v =>
        {
            counts.TryGetValue(v.List.Id, out var c);
            return new ListCard(v.List, v.Access, c?.Total ?? 0, c?.Done ?? 0);
        }).ToList();
    }
}
