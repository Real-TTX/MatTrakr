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
    public bool IsDefault { get; set; } // one of the three auto-created lists

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
    public string? MetadataJson { get; set; } // raw extras (author, director, genres, ...)

    public TrackList? List { get; set; }
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
