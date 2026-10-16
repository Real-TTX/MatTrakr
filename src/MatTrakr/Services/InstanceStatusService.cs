using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>The numbers of the whole instance at one moment (the tiles of the platform API).</summary>
public sealed record InstanceSnapshot(
    int ActiveUsers,
    int Lists,
    int Items,
    int ItemsDone,
    int Ratings,
    int SharedLists,
    long CoverBytes,
    long DatabaseBytes,
    DateTime? LastActivityUtc);

/// <summary>
/// How the instance stands, for the platform API (MatDesign docs/PLATFORM.md): counts over all users, the storage used and
/// the cheap checks of <c>/api/health</c>. Read only; the web layer asks here instead of querying the database itself.
/// </summary>
public class InstanceStatusService
{
    private readonly AppDbContext _db;
    private readonly string _dbPath;

    public InstanceStatusService(AppDbContext db, string dbPath)
    {
        _db = db;
        _dbPath = dbPath;
    }

    public async Task<InstanceSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var activeUsers = await _db.Users.CountAsync(u => u.IsActive, ct);
        var lists = await _db.Lists.CountAsync(ct);
        var items = await _db.ListItems.CountAsync(ct);
        var done = await _db.ListItems.CountAsync(i => i.Status == ItemStatus.Done, ct);
        var ratings = await _db.ItemRatings.CountAsync(ct);
        var sharedLists = await _db.ListShares.Select(s => s.ListId).Distinct().CountAsync(ct);
        var coverBytes = await _db.ItemCovers.SumAsync(c => (long)c.Data.Length, ct);

        var lastItem = await _db.ListItems.MaxAsync(i => (DateTime?)i.UpdateDate, ct);
        var lastRating = await _db.ItemRatings.MaxAsync(r => (DateTime?)r.UpdateDate, ct);
        var lastActivity = lastItem is null ? lastRating
            : lastRating is null ? lastItem
            : lastItem > lastRating ? lastItem : lastRating;

        return new InstanceSnapshot(activeUsers, lists, items, done, ratings, sharedLists, coverBytes, DatabaseBytes(), lastActivity);
    }

    /// <summary>True when the database answers a query.</summary>
    public async Task<bool> CanReadDatabaseAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.Users.AnyAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken ct = default) =>
        _db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.Admin, ct);

    /// <summary>The size of the SQLite file with its write-ahead log.</summary>
    private long DatabaseBytes()
    {
        long total = 0;
        foreach (var file in new[] { _dbPath, _dbPath + "-wal" })
        {
            var info = new FileInfo(file);
            if (info.Exists) total += info.Length;
        }
        return total;
    }
}
