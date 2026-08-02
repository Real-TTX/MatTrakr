using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MatTrakr.Services;

namespace MatTrakr.Data;

/// <summary>
/// Lets `dotnet ef` build the context at design time without the full web host.
/// Uses a throwaway SQLite path and a null current-user (no audit stamping needed).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design_time.db")
            .Options;
        return new AppDbContext(options, new NullCurrentUser());
    }

    private sealed class NullCurrentUser : ICurrentUser
    {
        public long? UserId => null;
    }
}
