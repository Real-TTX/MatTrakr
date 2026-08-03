using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.Pages.Lists;

public class AddModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ListAccessService _access;
    private readonly MediaSearchService _search;
    private readonly ICurrentUser _currentUser;

    public AddModel(AppDbContext db, ListAccessService access, MediaSearchService search, ICurrentUser currentUser)
    {
        _db = db;
        _access = access;
        _search = search;
        _currentUser = currentUser;
    }

    public TrackList List { get; set; } = null!;
    public bool SearchAvailable { get; set; }
    public string? Warning { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var result = await AuthorizeAsync(id);
        if (result is not null) return result;

        SearchAvailable = List.Type == MediaType.Books || _search.TmdbConfigured;
        if (!SearchAvailable)
            Warning = "Für die Movie/Series-Suche muss ein TMDb API-Key hinterlegt werden. " +
                      "Ein Admin kann ihn unter Administration → Einstellungen eintragen.";

        return Page();
    }

    /// <summary>Realtime search endpoint (JSON) for the debounced frontend.</summary>
    public async Task<IActionResult> OnGetSearchAsync(long id, string? q, CancellationToken ct)
    {
        var result = await AuthorizeAsync(id);
        if (result is not null) return new JsonResult(new { error = "forbidden" }) { StatusCode = 403 };

        var hits = await _search.SearchAsync(List.Type, q ?? "", ct);

        // Mark items already on the list so the UI can show it.
        var externalIds = hits.Select(h => h.ExternalId).ToList();
        var existing = await _db.ListItems
            .Where(i => i.ListId == id && i.ExternalId != null && externalIds.Contains(i.ExternalId))
            .Select(i => i.ExternalId!)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        return new JsonResult(hits.Select(h => new
        {
            externalId = h.ExternalId,
            title = h.Title,
            year = h.Year,
            coverUrl = h.CoverUrl,
            overview = h.Overview is { Length: > 220 } ? h.Overview[..220] + "…" : h.Overview,
            subtitle = h.Subtitle,
            alreadyAdded = existingSet.Contains(h.ExternalId),
        }));
    }

    /// <summary>Adds one picked search result to the list (data comes from the search hit).</summary>
    public async Task<IActionResult> OnPostAddAsync(long id, string externalId, string title,
        int? year, string? coverUrl, string? overview, string? subtitle, CancellationToken ct)
    {
        var result = await AuthorizeAsync(id);
        if (result is not null) return new JsonResult(new { error = "forbidden" }) { StatusCode = 403 };

        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(title))
            return new JsonResult(new { error = "invalid" }) { StatusCode = 400 };

        var duplicate = await _db.ListItems.AnyAsync(i => i.ListId == id && i.ExternalId == externalId, ct);
        if (!duplicate)
        {
            // Series: fetch the season count so we can track partial progress.
            int? totalSeasons = List.Type == MediaType.Series
                ? await _search.GetTvSeasonCountAsync(externalId, ct)
                : null;

            _db.ListItems.Add(new ListItem
            {
                ListId = id,
                Source = List.Type == MediaType.Books ? ExternalSource.GoogleBooks : ExternalSource.Tmdb,
                ExternalId = externalId,
                Title = title.Trim(),
                Year = year,
                CoverUrl = coverUrl,
                Overview = overview,
                Status = ItemStatus.Open,
                TotalSeasons = totalSeasons,
                WatchedSeasons = 0,
                MetadataJson = subtitle is null ? null : System.Text.Json.JsonSerializer.Serialize(new { subtitle }),
            });
            await _db.SaveChangesAsync();
        }

        return new JsonResult(new { ok = true, duplicate });
    }

    /// <summary>Loads the list and verifies edit access; returns a redirect when denied.</summary>
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
