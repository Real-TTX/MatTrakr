using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>
/// Admin database backup and restore. A backup is a ZIP containing a consistent
/// database snapshot plus the configuration (API keys). Restore accepts that ZIP
/// as well as a legacy bare .db file, and keeps a safety copy of the old data.
/// </summary>
public class BackupService
{
    private readonly string _dbPath;
    private readonly string _dataPath;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupService> _log;

    public BackupService(string dbPath, string dataPath, IServiceScopeFactory scopeFactory, ILogger<BackupService> log)
    {
        _dbPath = dbPath;
        _dataPath = dataPath;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    private string SettingsPath => Path.Combine(_dataPath, "config", "settings.json");

    /// <summary>Builds a ZIP backup: mattrakr.db (consistent snapshot) + config/settings.json.</summary>
    public async Task<byte[]> CreateBackupAsync(CancellationToken ct = default)
    {
        var tmpDb = Path.Combine(_dataPath, $"snapshot-{Guid.NewGuid():N}.db");
        try
        {
            await using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"VACUUM INTO '{tmpDb.Replace("'", "''")}'";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            using var mem = new MemoryStream();
            using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddFileAsync(zip, tmpDb, "mattrakr.db", ct);
                if (File.Exists(SettingsPath))
                    await AddFileAsync(zip, SettingsPath, "config/settings.json", ct);
            }
            return mem.ToArray();
        }
        finally
        {
            TryDelete(tmpDb);
        }
    }

    /// <summary>
    /// Restores from a ZIP (db + optional config) or a legacy bare .db file.
    /// Replaces the live database (and config, if present) and keeps a safety copy.
    /// </summary>
    public async Task<(bool Ok, string Message)> RestoreAsync(Stream upload, CancellationToken ct = default)
    {
        var incoming = Path.Combine(_dataPath, $"restore-{Guid.NewGuid():N}.bin");
        string? dbToInstall = null;
        string? cfgToInstall = null;
        var cfgRestored = false;
        try
        {
            await using (var fs = File.Create(incoming))
                await upload.CopyToAsync(fs, ct);

            if (new FileInfo(incoming).Length == 0)
                return (false, "Die hochgeladene Datei ist leer.");

            var head = new byte[2];
            await using (var fs = File.OpenRead(incoming))
                _ = await fs.ReadAsync(head.AsMemory(0, 2), ct);
            var isZip = head[0] == 0x50 && head[1] == 0x4B; // "PK"

            if (isZip)
            {
                using var za = ZipFile.OpenRead(incoming);
                var dbEntry = za.GetEntry("mattrakr.db");
                if (dbEntry is null)
                    return (false, "Im ZIP fehlt mattrakr.db.");
                dbToInstall = Path.Combine(_dataPath, $"restore-db-{Guid.NewGuid():N}.db");
                dbEntry.ExtractToFile(dbToInstall, overwrite: true);

                var cfgEntry = za.GetEntry("config/settings.json");
                if (cfgEntry is not null)
                {
                    cfgToInstall = Path.Combine(_dataPath, $"restore-cfg-{Guid.NewGuid():N}.json");
                    cfgEntry.ExtractToFile(cfgToInstall, overwrite: true);
                }
            }
            else
            {
                dbToInstall = incoming; // legacy raw .db
            }

            if (!await IsMatTrakrDatabaseAsync(dbToInstall, ct))
                return (false, "Das Backup enthält keine gültige MatTrakr-Datenbank.");

            SqliteConnection.ClearAllPools();

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var safety = Path.Combine(_dataPath, $"mattrakr-before-restore-{stamp}.db");
            if (File.Exists(_dbPath)) File.Copy(_dbPath, safety, overwrite: true);

            TryDelete(_dbPath);
            TryDelete(_dbPath + "-wal");
            TryDelete(_dbPath + "-shm");
            File.Move(dbToInstall, _dbPath);
            dbToInstall = null; // moved

            if (cfgToInstall is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                if (File.Exists(SettingsPath))
                    File.Copy(SettingsPath, SettingsPath + $".before-restore-{stamp}", overwrite: true);
                File.Move(cfgToInstall, SettingsPath, overwrite: true);
                cfgToInstall = null; // moved
                cfgRestored = true;
            }

            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);

            var cfgNote = cfgRestored ? " inkl. Konfiguration (API-Keys)" : "";
            return (true, $"Wiederhergestellt{cfgNote}. Sicherheitskopie der vorherigen Daten: {Path.GetFileName(safety)}.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Database restore failed");
            return (false, $"Wiederherstellung fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            TryDelete(incoming);
            if (dbToInstall is not null && dbToInstall != incoming) TryDelete(dbToInstall);
            if (cfgToInstall is not null) TryDelete(cfgToInstall);
        }
    }

    private static async Task AddFileAsync(ZipArchive zip, string path, string entryName, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var es = entry.Open();
        await using var fs = File.OpenRead(path);
        await fs.CopyToAsync(es, ct);
    }

    private static async Task<bool> IsMatTrakrDatabaseAsync(string path, CancellationToken ct)
    {
        var header = new byte[16];
        await using (var fs = File.OpenRead(path))
        {
            if (await fs.ReadAsync(header.AsMemory(0, 16), ct) < 16) return false;
        }
        if (!System.Text.Encoding.ASCII.GetString(header, 0, 15).StartsWith("SQLite format 3"))
            return false;

        try
        {
            await using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Users','Lists','ListItems')";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 3;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
