using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

/// <summary>Create a custom item by hand: details, cover search, upload with crop.</summary>
public class AddManualModel : PageModel
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly Regex DataUrl =
        new(@"^data:(?<t>image/[\w.+-]+);base64,(?<d>.+)$", RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly ListAccessService _access;
    private readonly MediaSearchService _search;
    private readonly ICurrentUser _currentUser;

    public AddManualModel(AppDbContext db, ListAccessService access, MediaSearchService search, ICurrentUser currentUser)
    {
        _db = db;
        _access = access;
        _search = search;
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
    /// <summary>Cropped image from the browser as a base64 data URL (takes precedence).</summary>
    [BindProperty] public string? CroppedImage { get; set; }

    public string? Error { get; set; }

    /// <summary>Cover search is possible for books always, for movies/series with a TMDb key.</summary>
    public bool CoverSearchAvailable => List.Type == MediaType.Books || _search.TmdbConfigured;

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var result = await AuthorizeAsync(id);
        return result ?? Page();
    }

    /// <summary>Returns candidate cover images (JSON) for the cover picker.</summary>
    public async Task<IActionResult> OnGetCoverSearchAsync(long id, string? q, CancellationToken ct)
    {
        var result = await AuthorizeAsync(id);
        if (result is not null) return new JsonResult(new { error = "forbidden" }) { StatusCode = 403 };

        var hits = await _search.SearchAsync(List.Type, q ?? "", ct);
        var covers = hits
            .Where(h => !string.IsNullOrEmpty(h.CoverUrl))
            .Select(h => new { title = h.Title, year = h.Year, coverUrl = h.CoverUrl })
            .Take(24)
            .ToList();

        return new JsonResult(new { available = CoverSearchAvailable, covers });
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

        // Cover priority: cropped image (base64) → uploaded file → pasted/searched URL.
        byte[]? imageBytes = null;
        string? imageType = null;

        if (!string.IsNullOrWhiteSpace(CroppedImage))
        {
            var m = DataUrl.Match(CroppedImage);
            if (m.Success)
            {
                try { imageBytes = Convert.FromBase64String(m.Groups["d"].Value); imageType = m.Groups["t"].Value; }
                catch { imageBytes = null; }
            }
        }
        else if (CoverFile is { Length: > 0 })
        {
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

        if (imageBytes is { Length: > (int)MaxImageBytes })
        {
            Error = "Das Bild ist zu groß (max. 5 MB).";
            return Page();
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
            // Local image wins; otherwise use the searched/pasted URL (if any).
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
                ContentType = imageType ?? "image/jpeg",
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
