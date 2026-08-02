using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Services;

public static class DbSeeder
{
    /// <summary>
    /// On an empty database, creates the initial admin. The password comes from
    /// MATTRAKR_ADMIN_PASSWORD (falling back to "admin") and must be changed on
    /// first login.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync()) return;

        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var password = Environment.GetEnvironmentVariable("MATTRAKR_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(password)) password = "admin";

        await users.CreateUserAsync("admin", "Administrator", UserRole.Admin, password, mustChangePassword: true);
    }
}
