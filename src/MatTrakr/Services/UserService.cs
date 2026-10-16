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
    private readonly ILogger<UserService> _log;

    public UserService(AppDbContext db, AuthService auth, ILogger<UserService> log)
    {
        _db = db;
        _auth = auth;
        _log = log;
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
    /// <c>sub</c>); else, when exactly one local user that is not linked yet has the person's e-mail address as its user name
    /// (MatTrakr keeps no separate address), that user is <b>not</b> linked: anyone can create or rename an account to any name,
    /// so the result is <see cref="ExternalSignInResult.LinkRequired"/> and the account is linked only after its password
    /// (<see cref="LinkExternalAsync"/>); else a new user is created with the three default lists and an unusable password (also
    /// beside a match that has no password at all, which can never be proven). The role claim, when it names one of MatTrakr's
    /// roles, wins at every sign-in, except that the last active administrator is never demoted by it. A deactivated user is refused.
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
                var match = sameName[0];
                if (!match.IsActive)
                    return ExternalSignInResult.Refused(disabled);

                // The name proves nothing: the person proves the account with its password first.
                if (!string.IsNullOrEmpty(match.PasswordHash))
                    return ExternalSignInResult.LinkRequired(match);
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

        await ApplyRoleAsync(user, wanted, ct);
        await _db.SaveChangesAsync(ct);
        return ExternalSignInResult.Success(user);
    }

    /// <summary>
    /// Links the local user <paramref name="userId"/> to the person MatIdentity signed in, after that user has signed in with its
    /// own password while the link was pending (the proof that <see cref="SignInExternalAsync"/> asked for): sets the external
    /// subject and applies the role claim like a sign-in through MatIdentity. Null (nothing changed) when the user is no longer
    /// active, is linked already, no longer has the e-mail address as its user name, or the subject belongs to another user.
    /// </summary>
    public async Task<User?> LinkExternalAsync(long userId, ExternalIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var subject = identity.Subject?.Trim();
        var email = string.IsNullOrWhiteSpace(identity.Email) ? null : identity.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || string.IsNullOrEmpty(subject) || email is null || !user.IsActive || user.ExternalSubject is not null
            || !string.Equals(user.Username.ToLowerInvariant(), email, StringComparison.Ordinal)
            || await _db.Users.AnyAsync(u => u.ExternalSubject == subject, ct))
        {
            _log.LogWarning("User {UserId} signed in locally but could not be linked with MatIdentity ({Subject})", userId, subject);
            return null;
        }

        user.ExternalSubject = subject;
        await ApplyRoleAsync(user, RoleOf(identity.Role), ct);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("User {UserId} ({Username}) linked with MatIdentity ({Subject}) after signing in with the password", user.Id, user.Username, subject);
        return user;
    }

    /// <summary>Follows the role claim, except that the last active administrator is never demoted by it.</summary>
    private async Task ApplyRoleAsync(User user, UserRole? wanted, CancellationToken ct)
    {
        if (wanted is { } role && user.Role != role)
        {
            var lastAdmin = user.Role == UserRole.Admin
                && !await _db.Users.AnyAsync(u => u.Id != user.Id && u.IsActive && u.Role == UserRole.Admin, ct);
            if (!lastAdmin) user.Role = role;
        }
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

/// <summary>The local user of a sign-in through MatIdentity, why it was refused, or the account that has to be proven first.</summary>
public sealed record ExternalSignInResult(User? User, string? Error)
{
    public bool Succeeded => User is not null;

    /// <summary>The unlinked account whose user name is the person's e-mail address: linked only after its password.</summary>
    public User? PendingLink { get; private init; }

    public static ExternalSignInResult Success(User user) => new(user, null);

    public static ExternalSignInResult Refused(string error) => new(null, error);

    public static ExternalSignInResult LinkRequired(User user) => new(null, null) { PendingLink = user };
}
