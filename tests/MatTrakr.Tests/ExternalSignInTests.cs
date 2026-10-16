using MatPlatform;
using MatTrakr.Data;
using MatTrakr.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace MatTrakr.Tests;

/// <summary>How a person signed in by MatIdentity becomes a local user (<see cref="UserService.SignInExternalAsync"/>), on the app's own database.</summary>
public sealed class ExternalSignInTests(TrakrFactory site) : IClassFixture<TrakrFactory>
{
    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = site.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<ExternalSignInResult> SignInAsync(ExternalIdentity identity)
        => WithScopeAsync(sp => sp.GetRequiredService<UserService>().SignInExternalAsync(identity));

    private Task<User> UserAsync(long id)
        => WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id));

    [Fact]
    public async Task A_new_person_becomes_a_user_with_the_role_of_the_claim_and_the_default_lists()
    {
        var result = await SignInAsync(new ExternalIdentity("sub-1", "Nora@Example.com", "Nora Neu", "nora", "verwalter"));

        Assert.True(result.Succeeded, result.Error);
        var user = await UserAsync(result.User!.Id);
        Assert.Equal("sub-1", user.ExternalSubject);
        Assert.Equal("nora", user.Username);
        Assert.Equal("Nora Neu", user.DisplayName);
        Assert.Equal(UserRole.Verwalter, user.Role);
        Assert.False(user.MustChangePassword);
        Assert.Equal(3, await WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Lists.CountAsync(l => l.OwnerUserId == user.Id)));

        // No local password anybody knows.
        Assert.Null(await WithScopeAsync(sp => sp.GetRequiredService<AuthService>().ValidateCredentialsAsync("nora", "")));

        // The second time the same user, found by sub.
        var again = await SignInAsync(new ExternalIdentity("sub-1", "other@example.com", "Other", "other", "verwalter"));
        Assert.Equal(user.Id, again.User!.Id);
    }

    [Fact]
    public async Task A_taken_user_name_gets_a_number_and_without_a_role_claim_the_user_is_a_user()
    {
        await SignInAsync(new ExternalIdentity("sub-b1", null, null, "bert", null));
        var second = await SignInAsync(new ExternalIdentity("sub-b2", null, null, "bert", null));

        Assert.Equal("bert-2", second.User!.Username);
        Assert.Equal("bert", second.User.DisplayName);
        Assert.Equal(UserRole.User, second.User.Role);
    }

    [Fact]
    public async Task A_local_user_named_like_the_email_address_is_not_linked_by_the_name_alone()
    {
        var local = await WithScopeAsync(sp => sp.GetRequiredService<UserService>()
            .CreateUserAsync("anna@example.com", "Anna", UserRole.User, "secret-password", mustChangePassword: false));
        var count = await WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Users.CountAsync());

        var result = await SignInAsync(new ExternalIdentity("sub-anna", "ANNA@example.com", "Anna Example", "anna", "admin"));

        Assert.False(result.Succeeded);
        Assert.Equal(local.Id, result.PendingLink!.Id);
        Assert.Null((await UserAsync(local.Id)).ExternalSubject);
        Assert.Equal(count, await WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Users.CountAsync()));

        // After its password (the login page) the account is linked and follows the role claim.
        var linked = await WithScopeAsync(sp => sp.GetRequiredService<UserService>()
            .LinkExternalAsync(local.Id, new ExternalIdentity("sub-anna", "ANNA@example.com", "Anna Example", "anna", "verwalter")));
        Assert.Equal(local.Id, linked!.Id);
        var user = await UserAsync(local.Id);
        Assert.Equal("sub-anna", user.ExternalSubject);
        Assert.Equal("Anna", user.DisplayName);
        Assert.Equal(UserRole.Verwalter, user.Role);

        // Once linked it is found by sub; a second link is refused.
        Assert.Equal(local.Id, (await SignInAsync(new ExternalIdentity("sub-anna", "anna@example.com", null, null, null))).User!.Id);
        Assert.Null(await WithScopeAsync(sp => sp.GetRequiredService<UserService>()
            .LinkExternalAsync(local.Id, new ExternalIdentity("sub-other", "anna@example.com", null, null, null))));
    }

    [Fact]
    public async Task A_link_is_refused_when_the_account_was_renamed_or_the_subject_is_taken()
    {
        var users = (Func<IServiceProvider, UserService>)(sp => sp.GetRequiredService<UserService>());
        var renamed = await WithScopeAsync(sp => users(sp).CreateUserAsync("ren@example.com", "Ren", UserRole.User, "secret-password", mustChangePassword: false));
        Assert.Null(await WithScopeAsync(sp => users(sp).LinkExternalAsync(renamed.Id, new ExternalIdentity("sub-ren", "other@example.com", null, null, null))));

        await SignInAsync(new ExternalIdentity("sub-taken", null, "T", "taken", null));
        Assert.Null(await WithScopeAsync(sp => users(sp).LinkExternalAsync(renamed.Id, new ExternalIdentity("sub-taken", "ren@example.com", null, null, null))));
        Assert.Null((await UserAsync(renamed.Id)).ExternalSubject);
    }

    [Fact]
    public async Task The_role_claim_wins_at_every_sign_in_and_without_it_the_local_role_stays()
    {
        var first = await SignInAsync(new ExternalIdentity("sub-r", null, "R", "r", "user"));
        Assert.Equal(UserRole.User, first.User!.Role);

        Assert.Equal(UserRole.Admin, (await SignInAsync(new ExternalIdentity("sub-r", null, "R", "r", "admin"))).User!.Role);
        Assert.Equal(UserRole.Admin, (await SignInAsync(new ExternalIdentity("sub-r", null, "R", "r", null))).User!.Role);
        Assert.Equal(UserRole.Admin, (await SignInAsync(new ExternalIdentity("sub-r", null, "R", "r", "something-else"))).User!.Role);
        Assert.Equal(UserRole.User, (await SignInAsync(new ExternalIdentity("sub-r", null, "R", "r", "user"))).User!.Role);
    }

    [Fact]
    public async Task The_last_administrator_is_not_demoted_by_a_claim()
    {
        var last = (await SignInAsync(new ExternalIdentity("sub-last", null, "Last", "last", "admin"))).User!;
        Assert.Equal(UserRole.Admin, last.Role);

        // Every other administrator becomes a user for a moment, so this one is the last.
        var others = await WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var admins = await db.Users.Where(u => u.Role == UserRole.Admin && u.Id != last.Id).ToListAsync();
            admins.ForEach(a => a.Role = UserRole.User);
            await db.SaveChangesAsync();
            return admins.Select(a => a.Id).ToList();
        });

        try
        {
            var result = await SignInAsync(new ExternalIdentity("sub-last", null, "Last", "last", "user"));
            Assert.Equal(last.Id, result.User!.Id);
            Assert.Equal(UserRole.Admin, result.User.Role);
        }
        finally
        {
            await WithScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<AppDbContext>();
                (await db.Users.Where(u => others.Contains(u.Id)).ToListAsync()).ForEach(a => a.Role = UserRole.Admin);
                return await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task A_deactivated_user_stays_out()
    {
        var created = await SignInAsync(new ExternalIdentity("sub-d", null, "D", "dora", null));
        await WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == created.User!.Id)).IsActive = false;
            return await db.SaveChangesAsync();
        });

        var again = await SignInAsync(new ExternalIdentity("sub-d", null, "D", "dora", "admin"));
        Assert.False(again.Succeeded);
        Assert.False(string.IsNullOrEmpty(again.Error));
    }

    [Fact]
    public async Task No_subject_is_refused()
        => Assert.False((await SignInAsync(new ExternalIdentity(" ", "x@example.com", "X", "x", null))).Succeeded);

    [Fact]
    public async Task The_sign_in_starts_a_session_that_keeps_the_id_token()
    {
        var http = new DefaultHttpContext();
        var person = new MatIdentityPerson("sub-s", "s@example.com", "Sam", "sam", null, "id-token-of-sam", new ClaimsPrincipal());

        var result = await WithScopeAsync(sp =>
        {
            http.RequestServices = sp;
            return sp.GetRequiredService<IMatIdentityUsers>().SignInAsync(person, http, CancellationToken.None);
        });

        Assert.True(result.Succeeded, result.Error);
        var cookie = http.Response.Headers.SetCookie.ToString();
        Assert.StartsWith(AuthConstants.CookieName + "=", cookie);
        var token = cookie[(AuthConstants.CookieName.Length + 1)..].Split(';')[0];

        var session = await WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().UserSessions.AsNoTracking().Include(s => s.User).SingleAsync(s => s.Token == token));
        Assert.Equal("id-token-of-sam", session.IdentityToken);
        Assert.Equal("sub-s", session.User!.ExternalSubject);
    }
}

/// <summary>The migration adds the columns of MatIdentity to a database of the version before.</summary>
public sealed class MigrationTests
{
    [Fact]
    public async Task An_old_database_gets_the_external_subject_and_the_identity_token()
    {
        var file = Path.Combine(Path.GetTempPath(), "mattrakr-migration-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + file).Options;
            await using (var db = new AppDbContext(options, new NoUser()))
            {
                await db.GetService<IMigrator>().MigrateAsync("20260818142447_AddListSortOrder");
                Assert.Contains("20260818142447_AddListSortOrder", await db.Database.GetAppliedMigrationsAsync());
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }

            await using (var db = new AppDbContext(options, new NoUser()))
            {
                var user = new User { Username = "x", PasswordHash = "-", ExternalSubject = "sub" };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                db.UserSessions.Add(new UserSession { UserId = user.Id, Token = "t", IdentityToken = "id" });
                await db.SaveChangesAsync();
            }

            await using (var db = new AppDbContext(options, new NoUser()))
            {
                Assert.Equal("sub", (await db.Users.SingleAsync()).ExternalSubject);
                Assert.Equal("id", (await db.UserSessions.SingleAsync()).IdentityToken);

                // One local user per MatIdentity person.
                db.Users.Add(new User { Username = "y", PasswordHash = "-", ExternalSubject = "sub" });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }

    private sealed class NoUser : ICurrentUser
    {
        public long? UserId => null;
    }
}
