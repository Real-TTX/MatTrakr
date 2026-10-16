using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MatTrakr.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatTrakr.Tests;

/// <summary>Signs in with the local form (the seeded administrator, who has to change the password first).</summary>
internal static class LocalForm
{
    public static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string path)
    {
        var html = await (await browser.GetAsync(path)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "No antiforgery token on " + path);

        return await browser.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = "admin",
            ["Password"] = "admin",
            ["RememberMe"] = "true",
        }));
    }

    public static string PathOf(Uri location) => location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
}

/// <summary>Without <c>MATIDENTITY_AUTHORITY</c> nothing changes: the local sign-in form, as before.</summary>
public sealed class LocalSignInTests(TrakrFactory site) : IClassFixture<TrakrFactory>
{
    [Fact]
    public async Task The_sign_in_page_is_the_local_form()
    {
        var response = await site.Browser().GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.DoesNotContain("MatIdentity", html);
    }

    [Fact]
    public async Task A_page_that_needs_a_sign_in_sends_to_the_local_form_and_there_is_no_oidc()
    {
        var response = await site.Browser().GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", LocalForm.PathOf(response.Headers.Location!));
        Assert.Equal(HttpStatusCode.NotFound, (await site.Browser().GetAsync("/signin-oidc")).StatusCode);
    }

    [Fact]
    public async Task The_local_sign_in_and_sign_out_work_as_before()
    {
        var browser = site.Browser();
        var signedIn = await LocalForm.SignInAsync(browser, "/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Account/ChangePassword", LocalForm.PathOf(signedIn.Headers.Location!));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Account/ChangePassword")).StatusCode);

        var signedOut = await browser.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/Account/Login", LocalForm.PathOf(signedOut.Headers.Location!));
        // Signed out: a page that needs a sign-in sends to the form again.
        var after = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("/Account/Login", LocalForm.PathOf(after.Headers.Location!));
    }
}

/// <summary>With <c>MATIDENTITY_AUTHORITY</c>: the automatic sign-in at MatIdentity, the local way in with <c>?local=1</c>, the sign-out there.</summary>
public sealed class IdentitySignInTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    [Fact]
    public async Task The_sign_in_page_goes_straight_to_matidentity_with_code_and_pkce()
    {
        var response = await site.Browser().GetAsync("/Account/Login?returnUrl=%2FAdmin%2FUsers");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.StartsWith(IdentityFactory.Authority + "/connect/authorize", location.OriginalString);

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("mattrakr", query["client_id"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrEmpty(query["code_challenge"].ToString()));
        Assert.EndsWith("/signin-oidc", query["redirect_uri"].ToString());
        Assert.Equal(["openid", "profile", "email", "roles", "offline_access"], query["scope"].ToString().Split(' '));
    }

    [Fact]
    public async Task A_page_that_needs_a_sign_in_ends_at_matidentity()
    {
        var browser = site.Browser();
        var first = await browser.GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var login = LocalForm.PathOf(first.Headers.Location!);
        Assert.StartsWith("/Account/Login", login);

        var second = await browser.GetAsync(login);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.StartsWith(IdentityFactory.Authority, second.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_local_form_stays_reachable_and_works()
    {
        var browser = site.Browser();
        var page = await browser.GetAsync("/Account/Login?local=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("Mit MatIdentity anmelden", html);

        var signedIn = await LocalForm.SignInAsync(browser, "/Account/Login?local=1");
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Account/ChangePassword", LocalForm.PathOf(signedIn.Headers.Location!));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Account/ChangePassword")).StatusCode);

        // A local session has no ID token: the sign-out stays here and does not sign in again at once.
        var signedOut = await browser.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/Account/Login?signedOut=1", LocalForm.PathOf(signedOut.Headers.Location!));
    }

    [Theory]
    [InlineData("/Account/Login?signedOut=1")]
    [InlineData("/Account/Login?ssoError=1")]
    public async Task After_the_sign_out_or_a_failure_there_is_no_automatic_sign_in(string path)
    {
        var response = await site.Browser().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Mit MatIdentity anmelden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_sign_out_of_a_session_from_matidentity_ends_it_there_too()
    {
        var token = Guid.NewGuid().ToString("N");
        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync(u => u.Username == "admin");
            db.UserSessions.Add(new UserSession { UserId = admin.Id, Token = token, ExpiresDate = DateTime.UtcNow.AddDays(1), IdentityToken = "the-id-token" });
            await db.SaveChangesAsync();
        }

        var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Logout");
        request.Headers.Add("Cookie", "MatTrakr.Session=" + token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(IdentityFactory.Authority + "/connect/logout", location.OriginalString);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("the-id-token", query["id_token_hint"].ToString());
        Assert.EndsWith("/signout-callback-oidc", query["post_logout_redirect_uri"].ToString());

        using var check = site.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AppDbContext>().UserSessions.AnyAsync(s => s.Token == token));
    }

    [Fact]
    public async Task The_definition_names_matidentity_and_a_bad_token_is_401()
    {
        using var doc = JsonDocument.Parse(await site.CreateClient().GetStringAsync("/api"));
        Assert.Equal(IdentityFactory.Authority, doc.RootElement.GetProperty("auth").GetProperty("identity").GetString());

        // Not a token of MatIdentity: the bearer check refuses it.
        var bearer = site.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await site.Api().GetAsync("/api/state")).StatusCode);
    }

    [Fact]
    public async Task Public_links_stay_anonymous()
    {
        var token = Guid.NewGuid().ToString("N");
        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var list = await db.Lists.FirstAsync(l => l.Type == MediaType.Books);
            db.ListShares.Add(new ListShare { ListId = list.Id, Token = token });
            await db.SaveChangesAsync();
        }

        var response = await site.Browser().GetAsync("/Shared/" + token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>MatIdentity is configured but down: the sign-in page falls back to the local form instead of an error.</summary>
public sealed class UnreachableIdentityTests(UnreachableIdentityFactory site) : IClassFixture<UnreachableIdentityFactory>
{
    [Fact]
    public async Task The_sign_in_page_shows_the_local_form_with_a_message()
    {
        var response = await site.Browser().GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("id=\"login-sso-error\"", html);
    }
}
