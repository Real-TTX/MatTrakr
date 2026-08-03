using System.Text.Json;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>Unified search hit from TMDb or Google Books.</summary>
public record MediaSearchResult(
    ExternalSource Source,
    string ExternalId,
    string Title,
    int? Year,
    string? CoverUrl,
    string? Overview,
    string? Subtitle,      // director-less shorthand: authors for books, original title for movies
    string? MetadataJson);

/// <summary>
/// Realtime search against TMDb (movies/series) and Google Books (books).
/// TMDb needs an API key (config "Tmdb:ApiKey"); Google Books works without one
/// (optional "GoogleBooks:ApiKey" raises the quota).
/// </summary>
public class MediaSearchService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaSearchService> _log;

    public MediaSearchService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<MediaSearchService> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
    }

    public bool TmdbConfigured => !string.IsNullOrWhiteSpace(_config["Tmdb:ApiKey"]);

    /// <summary>Validates a TMDb API key with a minimal live request.</summary>
    public async Task<(bool Ok, string Message)> TestTmdbKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "Kein API-Key angegeben.");

        try
        {
            var client = _httpFactory.CreateClient("TMDb");
            using var response = await client.GetAsync(
                $"configuration?api_key={Uri.EscapeDataString(apiKey)}", ct);

            return response.IsSuccessStatusCode
                ? (true, "Verbindung erfolgreich – der TMDb-Key ist gültig.")
                : (false, $"TMDb hat abgelehnt ({(int)response.StatusCode}). Bitte Key prüfen.");
        }
        catch (Exception ex)
        {
            return (false, $"Verbindung fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches a TV show's season count from the TMDb details endpoint (the search
    /// endpoint doesn't include it). Returns null if unavailable.
    /// </summary>
    public async Task<int?> GetTvSeasonCountAsync(string tmdbId, CancellationToken ct = default)
    {
        var apiKey = _config["Tmdb:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(tmdbId)) return null;

        try
        {
            var client = _httpFactory.CreateClient("TMDb");
            using var response = await client.GetAsync(
                $"tv/{Uri.EscapeDataString(tmdbId)}?api_key={Uri.EscapeDataString(apiKey)}&language=de-DE", ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("number_of_seasons", out var ns) &&
                ns.ValueKind == JsonValueKind.Number)
            {
                var count = ns.GetInt32();
                return count > 0 ? count : null;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TMDb season lookup failed for tv/{Id}", tmdbId);
        }
        return null;
    }

    public async Task<List<MediaSearchResult>> SearchAsync(MediaType type, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();

        try
        {
            return type switch
            {
                MediaType.Movies => await SearchTmdbAsync("movie", query, ct),
                MediaType.Series => await SearchTmdbAsync("tv", query, ct),
                MediaType.Books => await SearchGoogleBooksAsync(query, ct),
                _ => new(),
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Media search failed for {Type} '{Query}'", type, query);
            return new();
        }
    }

    // --- TMDb -----------------------------------------------------------------

    private async Task<List<MediaSearchResult>> SearchTmdbAsync(string kind, string query, CancellationToken ct)
    {
        var apiKey = _config["Tmdb:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return new();

        var client = _httpFactory.CreateClient("TMDb");
        var url = $"search/{kind}?api_key={Uri.EscapeDataString(apiKey)}" +
                  $"&query={Uri.EscapeDataString(query)}&language=de-DE&include_adult=false";

        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("TMDb returned {Status} for '{Query}'", response.StatusCode, query);
            return new();
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<MediaSearchResult>();

        foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray().Take(20))
        {
            // movie: title/release_date — tv: name/first_air_date
            var title = item.TryGetProperty("title", out var t) ? t.GetString()
                : item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;

            var dateStr = item.TryGetProperty("release_date", out var rd) ? rd.GetString()
                : item.TryGetProperty("first_air_date", out var fa) ? fa.GetString() : null;
            int? year = !string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4 &&
                        int.TryParse(dateStr[..4], out var y) ? y : null;

            var poster = item.TryGetProperty("poster_path", out var p) ? p.GetString() : null;
            var cover = string.IsNullOrEmpty(poster) ? null : $"https://image.tmdb.org/t/p/w342{poster}";

            var overview = item.TryGetProperty("overview", out var o) ? o.GetString() : null;
            var original = item.TryGetProperty("original_title", out var ot) ? ot.GetString()
                : item.TryGetProperty("original_name", out var on) ? on.GetString() : null;

            results.Add(new MediaSearchResult(
                ExternalSource.Tmdb,
                item.GetProperty("id").GetInt64().ToString(),
                title,
                year,
                cover,
                overview,
                original != title ? original : null,
                item.GetRawText()));
        }
        return results;
    }

    // --- Google Books (with Open Library fallback) -------------------------------

    private async Task<List<MediaSearchResult>> SearchGoogleBooksAsync(string query, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("GoogleBooks");
        var url = $"volumes?q={Uri.EscapeDataString(query)}&maxResults=20&printType=books";

        var apiKey = _config["GoogleBooks:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            url += $"&key={Uri.EscapeDataString(apiKey)}";

        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("Google Books returned {Status} for '{Query}' — falling back to Open Library",
                response.StatusCode, query);
            return await SearchOpenLibraryAsync(query, ct);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<MediaSearchResult>();

        if (!doc.RootElement.TryGetProperty("items", out var items)) return results;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("volumeInfo", out var info)) continue;
            var title = info.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;

            var dateStr = info.TryGetProperty("publishedDate", out var pd) ? pd.GetString() : null;
            int? year = !string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4 &&
                        int.TryParse(dateStr[..4], out var y) ? y : null;

            string? cover = null;
            if (info.TryGetProperty("imageLinks", out var img) &&
                img.TryGetProperty("thumbnail", out var thumb))
            {
                // Google returns http:// links; browsers block them on https pages.
                cover = thumb.GetString()?.Replace("http://", "https://");
            }

            string? authors = null;
            if (info.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array)
                authors = string.Join(", ", a.EnumerateArray().Select(x => x.GetString()));

            var overview = info.TryGetProperty("description", out var d) ? d.GetString() : null;

            results.Add(new MediaSearchResult(
                ExternalSource.GoogleBooks,
                item.GetProperty("id").GetString() ?? "",
                title,
                year,
                cover,
                overview,
                authors,
                item.GetRawText()));
        }
        return results;
    }

    // --- Open Library (keyless fallback when Google Books is rate-limited) -------

    private async Task<List<MediaSearchResult>> SearchOpenLibraryAsync(string query, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("OpenLibrary");
        var url = $"search.json?q={Uri.EscapeDataString(query)}&limit=20" +
                  "&fields=key,title,first_publish_year,cover_i,author_name";

        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("Open Library returned {Status} for '{Query}'", response.StatusCode, query);
            return new();
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<MediaSearchResult>();

        if (!doc.RootElement.TryGetProperty("docs", out var docs)) return results;

        foreach (var item in docs.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;

            int? year = item.TryGetProperty("first_publish_year", out var fy) &&
                        fy.ValueKind == JsonValueKind.Number ? fy.GetInt32() : null;

            string? cover = null;
            if (item.TryGetProperty("cover_i", out var ci) && ci.ValueKind == JsonValueKind.Number)
                cover = $"https://covers.openlibrary.org/b/id/{ci.GetInt64()}-M.jpg";

            string? authors = null;
            if (item.TryGetProperty("author_name", out var a) && a.ValueKind == JsonValueKind.Array)
                authors = string.Join(", ", a.EnumerateArray().Take(3).Select(x => x.GetString()));

            var key = item.TryGetProperty("key", out var k) ? k.GetString() : null; // e.g. /works/OL27448W
            if (string.IsNullOrEmpty(key)) continue;

            results.Add(new MediaSearchResult(
                ExternalSource.GoogleBooks, // stored source stays "books provider"
                key,
                title,
                year,
                cover,
                null,
                authors,
                item.GetRawText()));
        }
        return results;
    }
}
