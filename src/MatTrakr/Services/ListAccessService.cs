using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>How a user is related to a list — drives what the UI offers.</summary>
public enum ListAccess
{
    None = 0,
    ReadOnly = 1,
    Edit = 2,
    Owner = 3,
}

public record VisibleList(TrackList List, ListAccess Access);

/// <summary>
/// Single source of truth for list visibility & permissions:
/// owner > assignment (Verwalter, Edit) > user share (ReadOnly/Edit).
/// Admins intentionally get no implicit access to foreign lists here —
/// they manage via Administration, not by browsing other people's lists.
/// </summary>
public class ListAccessService
{
    private readonly AppDbContext _db;

    public ListAccessService(AppDbContext db) => _db = db;

    /// <summary>All lists the user can see, for the sidebar and the home page.</summary>
    public async Task<List<VisibleList>> GetVisibleListsAsync(long userId)
    {
        var own = await _db.Lists
            .Where(l => l.OwnerUserId == userId)
            .ToListAsync();

        var assigned = await _db.ListAssignments
            .Where(a => a.AssignedUserId == userId)
            .Include(a => a.List!).ThenInclude(l => l.Owner)
            .Select(a => a.List!)
            .ToListAsync();

        var shared = await _db.ListShares
            .Where(s => s.SharedWithUserId == userId)
            .Include(s => s.List!).ThenInclude(l => l.Owner)
            .ToListAsync();

        var result = new List<VisibleList>();
        var seen = new HashSet<long>();

        foreach (var l in own.OrderBy(l => l.Type).ThenBy(l => l.Name))
            if (seen.Add(l.Id))
                result.Add(new VisibleList(l, ListAccess.Owner));

        foreach (var l in assigned.OrderBy(l => l.Name))
            if (seen.Add(l.Id))
                result.Add(new VisibleList(l, ListAccess.Edit));

        foreach (var s in shared.OrderBy(s => s.List!.Name))
            if (s.List is not null && seen.Add(s.ListId))
                result.Add(new VisibleList(s.List,
                    s.Permission == SharePermission.Edit ? ListAccess.Edit : ListAccess.ReadOnly));

        return result;
    }

    /// <summary>Access level of one user on one list (list may be null → None).</summary>
    public async Task<ListAccess> GetAccessAsync(long userId, long listId)
    {
        var list = await _db.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listId);
        if (list is null) return ListAccess.None;
        if (list.OwnerUserId == userId) return ListAccess.Owner;

        if (await _db.ListAssignments.AnyAsync(a => a.ListId == listId && a.AssignedUserId == userId))
            return ListAccess.Edit;

        var share = await _db.ListShares
            .Where(s => s.ListId == listId && s.SharedWithUserId == userId)
            .OrderByDescending(s => s.Permission)
            .FirstOrDefaultAsync();
        if (share is not null)
            return share.Permission == SharePermission.Edit ? ListAccess.Edit : ListAccess.ReadOnly;

        return ListAccess.None;
    }

    /// <summary>Target lists for the move dialog: same type, editable, not the source.</summary>
    public async Task<List<TrackList>> GetMoveTargetsAsync(long userId, TrackList source)
    {
        var visible = await GetVisibleListsAsync(userId);
        return visible
            .Where(v => v.Access >= ListAccess.Edit
                        && v.List.Type == source.Type
                        && v.List.Id != source.Id)
            .Select(v => v.List)
            .ToList();
    }
}
