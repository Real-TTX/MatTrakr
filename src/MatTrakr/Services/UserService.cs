using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

/// <summary>
/// User lifecycle plus the rule that every user starts with the three empty
/// default lists (Movies, Series, Books). Reused by admin CRUD and the seeder.
/// </summary>
public class UserService
{
    // The three default lists every user gets on creation.
    private static readonly (MediaType Type, string Name)[] DefaultLists =
    {
        (MediaType.Movies, "Movies"),
        (MediaType.Series, "Series"),
        (MediaType.Books, "Books"),
    };

    private readonly AppDbContext _db;
    private readonly AuthService _auth;

    public UserService(AppDbContext db, AuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    public Task<bool> UsernameExistsAsync(string username, long? exceptId = null) =>
        _db.Users.AnyAsync(u => u.Username == username && (exceptId == null || u.Id != exceptId));

    public async Task<User> CreateUserAsync(string username, string displayName, UserRole role,
        string password, bool mustChangePassword)
    {
        var user = new User
        {
            Username = username.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
            Role = role,
            IsActive = true,
            MustChangePassword = mustChangePassword,
        };
        user.PasswordHash = _auth.HashPassword(user, password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(); // assigns user.Id

        var order = 0;
        foreach (var (type, name) in DefaultLists)
            _db.Lists.Add(new TrackList
            {
                Name = name, Type = type, OwnerUserId = user.Id,
                IsDefault = true, IsFavorite = true, SortOrder = order++,
            });

        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Maps a person MatIdentity has signed in to the local user: found by <see cref="ExternalIdentity.Subject"/> (the claim
    /// <c>sub</c>), else the one local user whose user name is the person's e-mail address (MatTrakr keeps no separate address)
    /// is linked, else a new user is created with the three default lists and an unusable password. The role claim, when it
    /// names one of MatTrakr's roles, wins at every sign-in, except that the last active administrator is never demoted by it.
    /// A deactivated user is refused.
    /// </summary>
    public async Task<ExternalSignInResult> SignInExternalAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(identity.Subject))
            return ExternalSignInResult.Refused("MatIdentity hat keine Kennung (sub) geliefert.");

        const string disabled = "Dein Konto ist in MatTrakr deaktiviert. Bitte wende dich an einen Administrator.";
        var subject = identity.Subject.Trim();
        var email = string.IsNullOrWhiteSpace(identity.Email) ? null : identity.Email.Trim().ToLowerInvariant();
        var wanted = RoleOf(identity.Role);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.ExternalSubject == subject, ct);

        if (user is null && email is not null)
        {
            var sameName = await _db.Users
                .Where(u => u.ExternalSubject == null && u.Username.ToLower() == email)
                .ToListAsync(ct);
            if (sameName.Count == 1)
            {
                user = sameName[0];
                user.ExternalSubject = subject;
            }
        }

        if (user is not null && !user.IsActive)
            return ExternalSignInResult.Refused(disabled);

        if (user is null)
        {
            var username = await FreeUsernameAsync(FirstNonEmpty(identity.PreferredUserName, email, identity.Name) ?? "matidentity");
            var displayName = FirstNonEmpty(identity.Name, identity.PreferredUserName, email) ?? username;
            if (displayName.Length > 150) displayName = displayName[..150];

            // No local password: a random one nobody knows (the local form stays closed to this user).
            var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            user = await CreateUserAsync(username, displayName, wanted ?? UserRole.User, password, mustChangePassword: false);
            user.ExternalSubject = subject;
            await _db.SaveChangesAsync(ct);
            return ExternalSignInResult.Success(user);
        }

        if (wanted is { } role && user.Role != role)
        {
            var lastAdmin = user.Role == UserRole.Admin
                && !await _db.Users.AnyAsync(u => u.Id != user.Id && u.IsActive && u.Role == UserRole.Admin, ct);
            if (!lastAdmin) user.Role = role;
        }

        await _db.SaveChangesAsync(ct);
        return ExternalSignInResult.Success(user);
    }

    /// <summary>The role of a role claim (the keys of the platform definition), or null for none or an unknown one.</summary>
    public static UserRole? RoleOf(string? claim) => claim?.Trim().ToLowerInvariant() switch
    {
        "admin" => UserRole.Admin,
        "verwalter" => UserRole.Verwalter,
        "user" => UserRole.User,
        _ => null,
    };

    /// <summary>The wanted user name if it is free, else with a number appended (max. 100 characters).</summary>
    private async Task<string> FreeUsernameAsync(string wanted)
    {
        var name = wanted.Trim();
        if (name.Length > 90) name = name[..90];
        var candidate = name;
        for (var n = 2; await UsernameExistsAsync(candidate); n++)
            candidate = $"{name}-{n}";
        return candidate;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

/// <summary>A person as MatIdentity names them (the claims <c>sub</c>, <c>email</c>, <c>name</c>, <c>preferred_username</c>, <c>role</c>).</summary>
public sealed record ExternalIdentity(string Subject, string? Email, string? Name, string? PreferredUserName, string? Role);

/// <summary>The local user of a sign-in through MatIdentity, or why it was refused.</summary>
public sealed record ExternalSignInResult(User? User, string? Error)
{
    public bool Succeeded => User is not null;

    public static ExternalSignInResult Success(User user) => new(user, null);

    public static ExternalSignInResult Refused(string error) => new(null, error);
}
