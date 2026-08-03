using Microsoft.EntityFrameworkCore;
using MatTrakr.Services;

namespace MatTrakr.Data;

public class AppDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<TrackList> Lists => Set<TrackList>();
    public DbSet<ListItem> ListItems => Set<ListItem>();
    public DbSet<ListShare> ListShares => Set<ListShare>();
    public DbSet<ListAssignment> ListAssignments => Set<ListAssignment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(150);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CardStatusPosition).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<UserSession>(e =>
        {
            e.ToTable("UserSessions");
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.Sessions)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TrackList>(e =>
        {
            e.ToTable("Lists");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Owner).WithMany(u => u.OwnedLists)
                .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListItem>(e =>
        {
            e.ToTable("ListItems");
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.ListId);
            e.HasOne(x => x.List).WithMany(l => l.Items)
                .HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListShare>(e =>
        {
            e.ToTable("ListShares");
            e.Property(x => x.Permission).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Token).HasMaxLength(64);
            // Unique only for the public-link tokens that are actually set.
            e.HasIndex(x => x.Token).IsUnique().HasFilter("[Token] IS NOT NULL");
            e.HasOne(x => x.List).WithMany(l => l.Shares)
                .HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SharedWithUser).WithMany()
                .HasForeignKey(x => x.SharedWithUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ListAssignment>(e =>
        {
            e.ToTable("ListAssignments");
            e.HasIndex(x => new { x.ListId, x.AssignedUserId }).IsUnique();
            e.HasOne(x => x.List).WithMany(l => l.Assignments)
                .HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedUser).WithMany()
                .HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>Stamps Create/Update audit columns on every added/modified entity.</summary>
    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        var uid = _currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreateDate = now;
                entry.Entity.CreateUserId ??= uid;
                entry.Entity.UpdateDate = now;
                entry.Entity.UpdateUserId ??= uid;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateDate = now;
                entry.Entity.UpdateUserId = uid;
            }
        }
    }
}
