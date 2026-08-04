using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatTrakr.Services;

namespace MatTrakr.Pages.Admin.Settings;

public class IndexModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly MediaSearchService _search;

    public IndexModel(SettingsService settings, MediaSearchService search)
    {
        _settings = settings;
        _search = search;
    }

    [BindProperty] public string? TmdbApiKey { get; set; }
    [BindProperty] public string? GoogleBooksApiKey { get; set; }

    public string? Success { get; set; }
    public string? Error { get; set; }
    public bool TmdbConfigured => _search.TmdbConfigured;

    public void OnGet() => LoadCurrent();

    public async Task<IActionResult> OnPostAsync()
    {
        await _settings.SaveAsync(new Dictionary<string, string?>
        {
            ["Tmdb:ApiKey"] = TmdbApiKey?.Trim(),
            ["GoogleBooks:ApiKey"] = GoogleBooksApiKey?.Trim(),
        });

        Success = "Einstellungen gespeichert. Sie sind sofort wirksam – kein Neustart nötig.";
        return Page();
    }

    /// <summary>Saves first, then verifies the TMDb key with a live request.</summary>
    public async Task<IActionResult> OnPostTestTmdbAsync(CancellationToken ct)
    {
        await _settings.SaveAsync(new Dictionary<string, string?>
        {
            ["Tmdb:ApiKey"] = TmdbApiKey?.Trim(),
            ["GoogleBooks:ApiKey"] = GoogleBooksApiKey?.Trim(),
        });

        var (ok, message) = await _search.TestTmdbKeyAsync(TmdbApiKey?.Trim() ?? "", ct);
        if (ok) Success = message;
        else Error = message;
        return Page();
    }

    private void LoadCurrent()
    {
        TmdbApiKey = _settings.GetEffective("Tmdb:ApiKey");
        GoogleBooksApiKey = _settings.GetEffective("GoogleBooks:ApiKey");
    }
}
