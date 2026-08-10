using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

/// <summary>Create a custom item by hand (title, details, optional cover image).</summary>
public class AddManualModel : PageModel
{
    private const long MaxImageBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly ListAccessService _access;
    private readonly ICurrentUser _currentUser;

    public AddManualModel(AppDbContext db, ListAccessService access, ICurrentUser currentUser)
    {
        _db = db;
        _access = access;
        _currentUser = currentUser;
    }

    public TrackList List { get; set; } = null!;

    [BindProperty] public string Title { get; set; } = string.Empty;
    [BindProperty] public int? Year { get; set; }
    [BindProperty] public string? Subtitle { get; set; }
    [BindProperty] public string? Overview { get; set; }
    [BindProperty] public int? TotalSeasons { get; set; }
    [BindProperty] public string? CoverUrl { get; set; }
    [BindProperty] public IFormFile? CoverFile { get; set; }

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var result = await AuthorizeAsync(id);
        return result ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(long id)
    {
        var result = await AuthorizeAsync(id);
        if (result is not null) return result;

        if (string.IsNullOrWhiteSpace(Title))
        {
            Error = "Bitte einen Titel angeben.";
            return Page();
        }

        byte[]? imageBytes = null;
        string? imageType = null;
        if (CoverFile is { Length: > 0 })
        {
            if (CoverFile.Length > MaxImageBytes)
            {
                Error = "Das Bild ist zu groß (max. 5 MB).";
                return Page();
            }
            if (!CoverFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                Error = "Bitte eine Bilddatei hochladen.";
                return Page();
            }
            using var ms = new MemoryStream();
            await CoverFile.CopyToAsync(ms);
            imageBytes = ms.ToArray();
            imageType = CoverFile.ContentType;
        }

        var item = new ListItem
        {
            ListId = id,
            Source = ExternalSource.Manual,
            ExternalId = null,
            Title = Title.Trim(),
            Year = Year,
            Overview = string.IsNullOrWhiteSpace(Overview) ? null : Overview.Trim(),
            Status = ItemStatus.Open,
            TotalSeasons = List.Type == MediaType.Series && TotalSeasons is > 0 ? TotalSeasons : null,
            WatchedSeasons = 0,
            MetadataJson = string.IsNullOrWhiteSpace(Subtitle)
                ? null
                : System.Text.Json.JsonSerializer.Serialize(new { subtitle = Subtitle.Trim() }),
            // Uploaded image wins; otherwise use the pasted URL (if any).
            CoverUrl = imageBytes is null && !string.IsNullOrWhiteSpace(CoverUrl) ? CoverUrl.Trim() : null,
        };

        _db.ListItems.Add(item);
        await _db.SaveChangesAsync(); // assigns item.Id

        if (imageBytes is not null)
        {
            _db.ItemCovers.Add(new ItemCover
            {
                ListItemId = item.Id,
                Data = imageBytes,
                ContentType = imageType!,
            });
            item.CoverUrl = $"/media/cover/{item.Id}";
            await _db.SaveChangesAsync();
        }

        return Redirect($"/Lists/{id}");
    }

    private async Task<IActionResult?> AuthorizeAsync(long id)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Redirect("/Account/Login");

        if (await _access.GetAccessAsync(userId.Value, id) < ListAccess.Edit)
            return Redirect("/Account/AccessDenied");

        List = (await _db.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id))!;
        return null;
    }
}
