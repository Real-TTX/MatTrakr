using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>
/// Admin database backup (consistent snapshot download) and restore (replace the
/// live SQLite file, keeping a safety copy). Cover images are not stored locally
/// — only their URLs live in the DB — so the database file is the full backup.
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

    /// <summary>Produces a consistent single-file snapshot of the database.</summary>
    public async Task<byte[]> CreateSnapshotAsync(CancellationToken ct = default)
    {
        var tmp = Path.Combine(_dataPath, $"snapshot-{Guid.NewGuid():N}.db");
        try
        {
            await using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                // VACUUM INTO writes a clean, defragmented, fully-committed copy.
                cmd.CommandText = $"VACUUM INTO '{tmp.Replace("'", "''")}'";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return await File.ReadAllBytesAsync(tmp, ct);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    /// <summary>
    /// Validates and installs an uploaded database, replacing the current one.
    /// The previous database is kept as mattrakr-before-restore-*.db.
    /// </summary>
    public async Task<(bool Ok, string Message)> RestoreAsync(Stream upload, CancellationToken ct = default)
    {
        var incoming = Path.Combine(_dataPath, $"restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var fs = File.Create(incoming))
                await upload.CopyToAsync(fs, ct);

            if (new FileInfo(incoming).Length == 0)
                return (false, "Die hochgeladene Datei ist leer.");

            if (!await IsMatTrakrDatabaseAsync(incoming, ct))
                return (false, "Das ist keine gültige MatTrakr-Datenbank.");

            // Release any pooled handles so the file can be swapped.
            SqliteConnection.ClearAllPools();

            var safety = Path.Combine(_dataPath, $"mattrakr-before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
            if (File.Exists(_dbPath))
                File.Copy(_dbPath, safety, overwrite: true);

            TryDelete(_dbPath);
            TryDelete(_dbPath + "-wal");
            TryDelete(_dbPath + "-shm");
            File.Move(incoming, _dbPath);

            // Bring the restored database up to the current schema if it is older.
            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);

            return (true, $"Wiederhergestellt. Eine Sicherheitskopie der vorherigen Daten liegt unter {Path.GetFileName(safety)}.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Database restore failed");
            TryDelete(incoming);
            return (false, $"Wiederherstellung fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>Checks the file is SQLite and carries the MatTrakr schema.</summary>
    private static async Task<bool> IsMatTrakrDatabaseAsync(string path, CancellationToken ct)
    {
        // SQLite header magic.
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
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return count == 3;
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
