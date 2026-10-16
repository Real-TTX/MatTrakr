using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using MatPlatform;
using MatTrakr.Data;
using MatTrakr.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MatTrakr.Tests;

/// <summary>
/// A user name proves nothing (anyone can register or rename an account to someone's address), so an unlinked account named like
/// the e-mail address of a MatIdentity person is linked only after its own password (MatPlatform <see cref="MatIdentityPendingLink"/>).
/// </summary>
public sealed class PendingLinkTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    private const string LinkPage = "/Account/Login?local=1&link=1";

    [Fact]
    public async Task A_pre_claimed_address_is_not_entered_and_the_person_lands_on_the_local_sign_in()
    {
        var (attacker, email) = await CreateUserAsync("attacker-password");

        var (target, http) = await MatIdentitySignInAsync(Person(email));

        Assert.Equal(LinkPage, target);
        // No session was started, the account is not linked and no second account was made.
        Assert.DoesNotContain(http.Response.Headers.SetCookie, c => c!.StartsWith(AuthConstants.CookieName + "=", StringComparison.Ordinal));
        Assert.Null((await UserAsync(attacker.Id)).ExternalSubject);
        Assert.False(await WithDbAsync(db => db.Users.AnyAsync(u => u.ExternalSubject == "sub-" + email)));

        var browser = new Browser(site);
        browser.Take(http.Response);
        var page = await browser.GetAsync(LinkPage);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-link\"", html);
        Assert.Contains(email, html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"login-link-cancel\"", html);
    }

    [Fact]
    public async Task The_local_sign_in_of_the_matched_account_links_it_with_the_role_and_the_id_token()
    {
        var (owner, email) = await CreateUserAsync("owner-password");
        var browser = await PendingBrowserAsync(email, role: "verwalter");

        var signedIn = await browser.SignInAsync(email, "owner-password");

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/", LocalForm.PathOf(signedIn.Headers.Location!));
        var user = await UserAsync(owner.Id);
        Assert.Equal("sub-" + email, user.ExternalSubject);
        Assert.Equal(UserRole.Verwalter, user.Role);
        Assert.False(browser.Has(MatIdentityPendingLink.CookieName));

        var session = await WithDbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Token == browser.Get(AuthConstants.CookieName)));
        Assert.Equal(owner.Id, session.UserId);
        Assert.Equal("id-token-" + email, session.IdentityToken);

        // From now on MatIdentity finds the account by sub.
        var (target, _) = await MatIdentitySignInAsync(Person(email));
        Assert.Equal("/", target);
    }

    [Fact]
    public async Task Another_account_signing_in_links_nothing_and_ends_the_pending_link()
    {
        var (matched, email) = await CreateUserAsync("matched-password");
        var (other, otherName) = await CreateUserAsync("other-password");
        var browser = await PendingBrowserAsync(email);

        var signedIn = await browser.SignInAsync(otherName, "other-password");

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Null((await UserAsync(matched.Id)).ExternalSubject);
        Assert.Null((await UserAsync(other.Id)).ExternalSubject);
        Assert.False(browser.Has(MatIdentityPendingLink.CookieName));
        var session = await WithDbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Token == browser.Get(AuthConstants.CookieName)));
        Assert.Equal(other.Id, session.UserId);
        Assert.Null(session.IdentityToken);
    }

    [Fact]
    public async Task A_wrong_password_keeps_the_link_pending()
    {
        var (matched, email) = await CreateUserAsync("matched-password");
        var browser = await PendingBrowserAsync(email);

        var wrong = await browser.SignInAsync(email, "guessed");

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.True(browser.Has(MatIdentityPendingLink.CookieName));
        Assert.Null((await UserAsync(matched.Id)).ExternalSubject);
    }

    [Fact]
    public async Task Cancel_deletes_the_pending_link()
    {
        var (matched, email) = await CreateUserAsync("matched-password");
        var browser = await PendingBrowserAsync(email);

        var html = await (await browser.GetAsync(LinkPage)).Content.ReadAsStringAsync();
        var cancel = await browser.PostAsync("/Account/Login?handler=CancelLink", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Browser.Antiforgery(html),
        });

        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal("/Account/Login?local=1", LocalForm.PathOf(cancel.Headers.Location!));
        Assert.False(browser.Has(MatIdentityPendingLink.CookieName));

        // The password afterwards is a plain local sign-in.
        await browser.SignInAsync(email, "matched-password");
        Assert.Null((await UserAsync(matched.Id)).ExternalSubject);
    }

    [Fact]
    public async Task An_expired_or_forged_pending_cookie_is_ignored()
    {
        var (matched, email) = await CreateUserAsync("matched-password");
        var json = JsonSerializer.Serialize(
            new MatIdentityPendingLink("sub-" + email, email, "X", null, "admin", "t", matched.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var expired = site.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(MatIdentityPendingLink.Purpose)
            .ToTimeLimitedDataProtector().Protect(json, DateTimeOffset.UtcNow.AddSeconds(-1));

        foreach (var value in new[] { expired, Uri.EscapeDataString(json), "CfDJ8forged" })
        {
            var browser = new Browser(site);
            browser.Set(MatIdentityPendingLink.CookieName, value);

            var html = await (await browser.GetAsync(LinkPage)).Content.ReadAsStringAsync();
            Assert.Contains("id=\"login-link-expired\"", html);
            Assert.DoesNotContain("id=\"login-link\"", html);

            await browser.SignInAsync(email, "matched-password");
            var user = await UserAsync(matched.Id);
            Assert.Null(user.ExternalSubject);
            Assert.Equal(UserRole.User, user.Role);
        }
    }

    [Fact]
    public async Task A_deactivated_match_is_refused_and_one_without_a_password_gets_a_new_account_beside_it()
    {
        var (inactive, inactiveName) = await CreateUserAsync("x-password");
        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == inactive.Id)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        Assert.Equal("/Account/AccessDenied", (await MatIdentitySignInAsync(Person(inactiveName))).Target);

        var (noPassword, noPasswordName) = await CreateUserAsync("y-password");
        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == noPassword.Id)).PasswordHash = string.Empty;
            return await db.SaveChangesAsync();
        });
        Assert.Equal("/", (await MatIdentitySignInAsync(Person(noPasswordName))).Target);
        Assert.Null((await UserAsync(noPassword.Id)).ExternalSubject);
        Assert.True(await WithDbAsync(db => db.Users.AnyAsync(u => u.ExternalSubject == "sub-" + noPasswordName && u.Id != noPassword.Id)));
    }

    private async Task<Browser> PendingBrowserAsync(string email, string? role = null)
    {
        var (target, http) = await MatIdentitySignInAsync(Person(email, role));
        Assert.Equal(LinkPage, target);
        var browser = new Browser(site);
        browser.Take(http.Response);
        Assert.True(browser.Has(MatIdentityPendingLink.CookieName));
        return browser;
    }

    /// <summary>What the callback of MatIdentity does for <paramref name="person"/> (no MatIdentity has to run).</summary>
    private async Task<(string Target, DefaultHttpContext Http)> MatIdentitySignInAsync(MatIdentityPerson person)
    {
        using var scope = site.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost");
        return (await MatIdentitySignIn.CompleteSignInAsync(http, person), http);
    }

    private static MatIdentityPerson Person(string email, string? role = null)
        => new("sub-" + email, email.ToUpperInvariant(), "Someone", "someone", role, "id-token-" + email, new ClaimsPrincipal());

    private async Task<(User User, string Name)> CreateUserAsync(string password)
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10] + "@example.com";
        var user = await WithScopeAsync(sp => sp.GetRequiredService<UserService>().CreateUserAsync(name, name, UserRole.User, password, mustChangePassword: false));
        return (user, name);
    }

    private Task<User> UserAsync(long id) => WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));

    private Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work) => WithScopeAsync(sp => work(sp.GetRequiredService<AppDbContext>()));

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = site.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>A browser with its own cookie jar, so a cookie made outside the app (the pending link) can be put in.</summary>
    private sealed class Browser(IdentityFactory site)
    {
        private readonly HttpClient _client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        private readonly Dictionary<string, string> _cookies = [];

        public bool Has(string name) => _cookies.ContainsKey(name);

        public string Get(string name) => _cookies[name];

        public void Set(string name, string value) => _cookies[name] = value;

        public void Take(HttpResponse response)
            => Take(response.Headers.SetCookie.Select(c => c ?? string.Empty));

        public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(new HttpRequestMessage(HttpMethod.Get, path));

        public Task<HttpResponseMessage> PostAsync(string path, Dictionary<string, string> form)
            => SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) });

        public async Task<HttpResponseMessage> SignInAsync(string username, string password)
        {
            var html = await (await GetAsync(LinkPage)).Content.ReadAsStringAsync();
            return await PostAsync(LinkPage, new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Antiforgery(html),
                ["Username"] = username,
                ["Password"] = password,
                ["RememberMe"] = "true",
            });
        }

        public static string Antiforgery(string html)
        {
            var token = Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(token), "No antiforgery token");
            return token;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));

            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                Take(values);
            return response;
        }

        private void Take(IEnumerable<string> setCookies)
        {
            foreach (var cookie in SetCookieHeaderValue.ParseList(setCookies.ToList()))
            {
                var name = cookie.Name.Value!;
                if (string.IsNullOrEmpty(cookie.Value.Value) || cookie.Expires < DateTimeOffset.UtcNow)
                    _cookies.Remove(name);
                else
                    _cookies[name] = cookie.Value.Value!;
            }
        }
    }
}
