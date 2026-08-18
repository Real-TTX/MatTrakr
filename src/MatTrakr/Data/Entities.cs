using System.ComponentModel.DataAnnotations.Schema;

namespace MatTrakr.Data;

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

public enum UserRole
{
    User = 0,
    Verwalter = 1,
    Admin = 2,
}

public enum MediaType
{
    Movies = 1,
    Series = 2,
    Books = 3,
}

/// <summary>
/// Binary tracking state. Rendered as Gesehen/Ungesehen (Movies, Series) or
/// Gelesen/Ungelesen (Books) depending on the owning list's <see cref="MediaType"/>.
/// </summary>
public enum ItemStatus
{
    Open = 0,
    Done = 1,
    /// <summary>Partially watched — used by series when some (but not all) seasons are seen.</summary>
    Partial = 2,
    /// <summary>Started but abandoned / dropped.</summary>
    Abandoned = 3,
}

public enum ExternalSource
{
    Manual = 0,
    Tmdb = 1,
    GoogleBooks = 2,
}

public enum SharePermission
{
    ReadOnly = 0,
    Edit = 1,
}

/// <summary>Where the status tag sits on a grid card (personal preference).</summary>
public enum CardStatusPosition
{
    Above = 0,
    Below = 1,
}

// ---------------------------------------------------------------------------
// Audit base — every table carries Create/Update stamps (see AppDbContext).
// PK is always "Id" as BIGINT (long).
// ---------------------------------------------------------------------------

public abstract class AuditableEntity
{
    public long Id { get; set; }

    public DateTime CreateDate { get; set; }
    public long? CreateUserId { get; set; }
    public DateTime UpdateDate { get; set; }
    public long? UpdateUserId { get; set; }
}

// ---------------------------------------------------------------------------
// Entities
// ---------------------------------------------------------------------------

public class User : AuditableEntity
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    /// <summary>Personal preference: status tag above (small) or below (large) the cover.</summary>
    public CardStatusPosition CardStatusPosition { get; set; } = CardStatusPosition.Above;

    public ICollection<TrackList> OwnedLists { get; set; } = new List<TrackList>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}

/// <summary>
/// DB-backed session so logins survive a container restart. The cookie only ever
/// carries <see cref="Token"/>; the row is the source of truth and can be revoked.
/// </summary>
public class UserSession : AuditableEntity
{
    public long UserId { get; set; }
    public string Token { get; set; } = string.Empty; // uuid — security key
    public DateTime? ExpiresDate { get; set; }

    public User? User { get; set; }
}

/// <summary>A user's tracking list. Table name "Lists".</summary>
public class TrackList : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public MediaType Type { get; set; }
    public long OwnerUserId { get; set; }
    public bool IsDefault { get; set; }   // one of the three auto-created lists
    public bool IsFavorite { get; set; }  // pinned to the sidebar
    public int SortOrder { get; set; }    // manual order within the owner's lists (sidebar/menu)

    public User? Owner { get; set; }
    public ICollection<ListItem> Items { get; set; } = new List<ListItem>();
    public ICollection<ListShare> Shares { get; set; } = new List<ListShare>();
    public ICollection<ListAssignment> Assignments { get; set; } = new List<ListAssignment>();
}

public class ListItem : AuditableEntity
{
    public long ListId { get; set; }
    public ExternalSource Source { get; set; }
    public string? ExternalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public string? Overview { get; set; }
    public int? Year { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Open;

    /// <summary>Series only: total number of seasons (from TMDb), null for movies/books.</summary>
    public int? TotalSeasons { get; set; }
    /// <summary>Series only: how many seasons have been watched (kept in sync with the set below).</summary>
    public int WatchedSeasons { get; set; }
    /// <summary>Series only: which seasons are watched, as a sorted CSV of season numbers (e.g. "1,3,4").</summary>
    public string? WatchedSeasonsData { get; set; }

    public string? MetadataJson { get; set; } // raw extras (author, director, genres, ...)

    public TrackList? List { get; set; }

    /// <summary>True when this item tracks progress per season (a series with known season count).</summary>
    [NotMapped]
    public bool UsesSeasons => TotalSeasons is > 0;

    /// <summary>The watched season numbers (falls back to the first N seasons for legacy count-only data).</summary>
    [NotMapped]
    public HashSet<int> WatchedSeasonNumbers
    {
        get
        {
            var set = new HashSet<int>();
            if (!string.IsNullOrWhiteSpace(WatchedSeasonsData))
            {
                foreach (var part in WatchedSeasonsData.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part, out var n)) set.Add(n);
            }
            else if (WatchedSeasons > 0)
            {
                for (var n = 1; n <= WatchedSeasons; n++) set.Add(n); // legacy count-only fallback
            }
            return set;
        }
    }

    /// <summary>Sets the watched seasons and keeps the count + status in sync.</summary>
    public void SetWatchedSeasons(IEnumerable<int> seasons, int total)
    {
        var set = seasons.Where(s => s >= 1 && s <= total).Distinct().OrderBy(s => s).ToList();
        WatchedSeasonsData = set.Count == 0 ? null : string.Join(",", set);
        WatchedSeasons = set.Count;
        Status = DeriveSeasonStatus(set.Count, total);
    }

    /// <summary>Derives Open/Partial/Done from watched vs. total seasons.</summary>
    public static ItemStatus DeriveSeasonStatus(int watched, int total) =>
        watched <= 0 ? ItemStatus.Open : watched >= total ? ItemStatus.Done : ItemStatus.Partial;
}

/// <summary>
/// Sharing a list. Either with a registered user (SharedWithUserId + Permission)
/// or via a public read-only link (Token). Exactly one of the two is set per row.
/// </summary>
public class ListShare : AuditableEntity
{
    public long ListId { get; set; }
    public long? SharedWithUserId { get; set; }
    public string? Token { get; set; } // uuid — public link key
    public SharePermission Permission { get; set; } = SharePermission.ReadOnly;

    public TrackList? List { get; set; }
    public User? SharedWithUser { get; set; }
}

/// <summary>
/// Locally stored cover image for a (usually manually added) item. Kept in its own
/// table so listing items never pulls the image bytes; served via /media/cover/{id}.
/// </summary>
public class ItemCover : AuditableEntity
{
    public long ListItemId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "image/jpeg";

    public ListItem? ListItem { get; set; }
}

/// <summary>
/// One user's personal rating of an item (1–5 stars + optional comment).
/// Everyone with access to the item's list can see all ratings.
/// </summary>
public class ItemRating : AuditableEntity
{
    public long ListItemId { get; set; }
    public long UserId { get; set; }
    public int Stars { get; set; }        // 1..5
    public string? Comment { get; set; }

    public ListItem? ListItem { get; set; }
    public User? User { get; set; }
}

/// <summary>
/// Admin delegates edit rights on a foreign list to a Verwalter.
/// The assigning admin is captured by <see cref="AuditableEntity.CreateUserId"/>.
/// </summary>
public class ListAssignment : AuditableEntity
{
    public long ListId { get; set; }
    public long AssignedUserId { get; set; } // the Verwalter

    public TrackList? List { get; set; }
    public User? AssignedUser { get; set; }
}
