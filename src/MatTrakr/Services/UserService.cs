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
}
