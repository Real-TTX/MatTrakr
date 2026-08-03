using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatTrakr.Services;

namespace MatTrakr.Pages.Admin.Backup;

public class IndexModel : PageModel
{
    private readonly BackupService _backup;

    public IndexModel(BackupService backup) => _backup = backup;

    public string? Error { get; set; }

    public void OnGet() { }

    /// <summary>Streams a fresh database snapshot as a download.</summary>
    public async Task<IActionResult> OnGetDownloadAsync(CancellationToken ct)
    {
        var bytes = await _backup.CreateSnapshotAsync(ct);
        var name = $"mattrakr-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db";
        return File(bytes, "application/x-sqlite3", name);
    }

    public async Task<IActionResult> OnPostRestoreAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            Error = "Bitte eine Backup-Datei (.db) auswählen.";
            return Page();
        }

        await using var stream = file.OpenReadStream();
        var (ok, message) = await _backup.RestoreAsync(stream, ct);
        if (!ok)
        {
            Error = message;
            return Page();
        }

        // The current session lives in the replaced database, so re-login is required.
        return Redirect("/Account/Login");
    }
}
