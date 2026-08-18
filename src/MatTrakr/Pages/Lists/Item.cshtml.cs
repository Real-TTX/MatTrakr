using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

public class ItemModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ListAccessService _access;
    private readonly ICurrentUser _currentUser;

    public ItemModel(AppDbContext db, ListAccessService access, ICurrentUser currentUser)
    {
        _db = db;
        _access = access;
        _currentUser = currentUser;
    }

    public TrackList List { get; set; } = null!;
    public ListItem Item { get; set; } = null!;
    public ListAccess Access { get; set; }
    public List<TrackList> MoveTargets { get; set; } = new();

    [BindProperty] public ItemStatus Status { get; set; }
    [BindProperty] public List<int> Seasons { get; set; } = new();
    [BindProperty] public long MoveToListId { get; set; }

    [BindProperty] public int MyStars { get; set; }
    [BindProperty] public string? MyComment { get; set; }
    public List<RatingView> Ratings { get; set; } = new();
    public record RatingView(string Name, int Stars, string? Comment, bool IsMine);

    public bool CanEdit => Access >= ListAccess.Edit;
    public string DoneWord => List.Type == MediaType.Books ? "Gelesen" : "Gesehen";
    public string OpenWord => List.Type == MediaType.Books ? "Ungelesen" : "Ungesehen";
    public string PartialWord => "Teilweise gesehen";

    /// <summary>Series with a known season count → track watched seasons (auto status).</summary>
    public bool UsesSeasons => List.Type == MediaType.Series && Item.TotalSeasons is > 0;

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(long listId, long itemId)
    {
        var result = await LoadAsync(listId, itemId, ListAccess.ReadOnly);
        if (result is not null) return result;

        Status = Item.Status;
        Seasons = Item.WatchedSeasonNumbers.OrderBy(n => n).ToList();
        await LoadRatingsAsync(_currentUser.UserId!.Value, itemId);
        return Page();
    }

    /// <summary>Set/update/clear the current user's personal rating (ReadOnly access is enough).</summary>
    public async Task<IActionResult> OnPostRateAsync(long listId, long itemId)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        var result = await LoadAsync(listId, itemId, ListAccess.ReadOnly);
        if (result is not null) return result;

        var stars = Math.Clamp(MyStars, 0, 5);
        var comment = string.IsNullOrWhiteSpace(MyComment) ? null : MyComment.Trim();
        if (comment is { Length: > 1000 }) comment = comment[..1000];

        var existing = await _db.ItemRatings
            .FirstOrDefaultAsync(r => r.ListItemId == itemId && r.UserId == userId);

        if (stars == 0)
        {
            if (existing is not null) _db.ItemRatings.Remove(existing);
        }
        else if (existing is null)
        {
            _db.ItemRatings.Add(new ItemRating
            {
                ListItemId = itemId, UserId = userId.Value, Stars = stars, Comment = comment,
            });
        }
        else
        {
            existing.Stars = stars;
            existing.Comment = comment;
        }

        await _db.SaveChangesAsync();
        return Redirect($"/Lists/{listId}/Items/{itemId}");
    }

    private async Task LoadRatingsAsync(long userId, long itemId)
    {
        var ratings = await _db.ItemRatings.AsNoTracking()
            .Where(r => r.ListItemId == itemId)
            .Include(r => r.User)
            .OrderByDescending(r => r.Stars)
            .ToListAsync();

        Ratings = ratings.Select(r => new RatingView(
            string.IsNullOrWhiteSpace(r.User?.DisplayName) ? (r.User?.Username ?? "?") : r.User!.DisplayName,
            r.Stars, r.Comment, r.UserId == userId)).ToList();

        var mine = ratings.FirstOrDefault(r => r.UserId == userId);
        MyStars = mine?.Stars ?? 0;
        MyComment = mine?.Comment;
    }

    public async Task<IActionResult> OnPostAsync(long listId, long itemId)
    {
        var result = await LoadAsync(listId, itemId, ListAccess.Edit);
        if (result is not null) return result;

        // Status comes from the shortcuts (incl. Abgebrochen). For series we also
        // persist which seasons are watched; the two are kept in sync client-side.
        if (UsesSeasons)
        {
            var total = Item.TotalSeasons!.Value;
            var set = Seasons.Where(s => s >= 1 && s <= total).Distinct().OrderBy(s => s).ToList();
            Item.WatchedSeasonsData = set.Count == 0 ? null : string.Join(",", set);
            Item.WatchedSeasons = set.Count;
        }
        Item.Status = Status;

        // Optional move — target must be editable and of the same media type.
        if (MoveToListId != 0 && MoveToListId != listId)
        {
            var target = MoveTargets.FirstOrDefault(t => t.Id == MoveToListId);
            if (target is null)
            {
                Error = "Ungültige Zielliste (Typ muss übereinstimmen und du brauchst Bearbeitungsrechte).";
                Status = Item.Status;
                return Page();
            }

            var duplicate = await _db.ListItems.AnyAsync(i =>
                i.ListId == target.Id && i.ExternalId == Item.ExternalId && i.ExternalId != null);
            if (duplicate)
            {
                Error = $"„{Item.Title}“ ist in „{target.Name}“ bereits vorhanden.";
                Status = Item.Status;
                return Page();
            }

            Item.ListId = target.Id;
            await _db.SaveChangesAsync();
            return Redirect($"/Lists/{target.Id}");
        }

        await _db.SaveChangesAsync();
        return Redirect($"/Lists/{listId}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(long listId, long itemId)
    {
        var result = await LoadAsync(listId, itemId, ListAccess.Edit);
        if (result is not null) return result;

        _db.ListItems.Remove(Item);
        await _db.SaveChangesAsync();
        return Redirect($"/Lists/{listId}");
    }

    private async Task<IActionResult?> LoadAsync(long listId, long itemId, ListAccess required)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        Access = await _access.GetAccessAsync(userId.Value, listId);
        if (Access < required) return Redirect("/Account/AccessDenied");

        List = (await _db.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listId))!;

        var item = await _db.ListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ListId == listId);
        if (item is null) return Redirect($"/Lists/{listId}");
        Item = item;

        if (Access >= ListAccess.Edit)
            MoveTargets = await _access.GetMoveTargetsAsync(userId.Value, List);

        return null;
    }
}
