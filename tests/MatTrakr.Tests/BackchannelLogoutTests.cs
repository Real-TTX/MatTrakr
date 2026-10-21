using System.Net;
using System.Security.Claims;
using MatPlatform;
using MatTrakr.Data;
using MatTrakr.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MatTrakr.Tests;

/// <summary>
/// The back-channel logout of MatIdentity (MatPlatform 1.4.0, <c>POST /signout-backchannel</c>): a signed logout token deletes the
/// session rows it names, and the cookie of such a session is no longer signed in at its next request.
/// </summary>
public sealed class BackchannelLogoutTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    private const string Path = "/signout-backchannel";

    [Fact]
    public async Task A_valid_token_ends_the_session_of_its_sid_and_only_that()
    {
        var subject = "sub-" + Guid.NewGuid().ToString("N");
        var first = await SignInAsync(subject, "sid-1-" + subject);
        var second = await SignInAsync(subject, "sid-2-" + subject);
        Assert.Equal(HttpStatusCode.OK, await PageAsync(first));
        Assert.Equal(HttpStatusCode.OK, await PageAsync(second));

        var response = await PostAsync(Token(subject, "sid-1-" + subject));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, await PageAsync(first));
        Assert.Equal(HttpStatusCode.OK, await PageAsync(second));
        Assert.False(await SessionExistsAsync(first));
    }

    [Fact]
    public async Task A_token_with_the_subject_alone_ends_every_session_of_the_person()
    {
        var subject = "sub-" + Guid.NewGuid().ToString("N");
        var first = await SignInAsync(subject, "sid-a-" + subject);
        var second = await SignInAsync(subject, null);
        var other = await SignInAsync("sub-" + Guid.NewGuid().ToString("N"), "sid-other-" + subject);

        var response = await PostAsync(Token(subject, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, await PageAsync(first));
        Assert.Equal(HttpStatusCode.Redirect, await PageAsync(second));
        Assert.Equal(HttpStatusCode.OK, await PageAsync(other));
    }

    [Fact]
    public async Task An_unknown_subject_or_sid_is_no_error()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Token("sub-nobody-" + Guid.NewGuid().ToString("N"), null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Token(null, "sid-nobody-" + Guid.NewGuid().ToString("N")))).StatusCode);
    }

    [Fact]
    public async Task A_token_of_another_key_is_400_and_ends_nothing()
    {
        var subject = "sub-" + Guid.NewGuid().ToString("N");
        var session = await SignInAsync(subject, "sid-" + subject);
        var forger = new SigningCredentials(new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256);

        var response = await PostAsync(Token(subject, "sid-" + subject, forger));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await PageAsync(session));
    }

    /// <summary>What the callback of MatIdentity does for a person (no MatIdentity has to run); returns the session token of the cookie.</summary>
    private async Task<string> SignInAsync(string subject, string? sessionId)
    {
        using var scope = site.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var name = "p" + Guid.NewGuid().ToString("N")[..10];
        var person = new MatIdentityPerson(subject, name + "@example.com", name, name, null, "id-token", new ClaimsPrincipal()) { SessionId = sessionId };

        var result = await scope.ServiceProvider.GetRequiredService<IMatIdentityUsers>().SignInAsync(person, http, CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        var cookie = http.Response.Headers.SetCookie.ToString();
        return cookie[(AuthConstants.CookieName.Length + 1)..].Split(';')[0];
    }

    /// <summary>The answer to a page that needs a signed-in user: 200 signed in, a redirect to the sign-in otherwise.</summary>
    private async Task<HttpStatusCode> PageAsync(string sessionToken)
    {
        var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Profile");
        request.Headers.Add("Cookie", AuthConstants.CookieName + "=" + sessionToken);
        return (await client.SendAsync(request)).StatusCode;
    }

    private async Task<bool> SessionExistsAsync(string sessionToken)
    {
        using var scope = site.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserSessions.AnyAsync(s => s.Token == sessionToken);
    }

    private Task<HttpResponseMessage> PostAsync(string token)
        => site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .PostAsync(Path, new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token }));

    /// <summary>A logout token as MatIdentity writes it.</summary>
    internal static string Token(string? subject, string? sessionId, SigningCredentials? credentials = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = IdentityFactory.Authority,
            ["aud"] = "mattrakr",
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 120,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["events"] = new Dictionary<string, object> { [MatIdentityLogoutToken.EventType] = new Dictionary<string, object>() },
        };
        if (subject is not null)
            claims["sub"] = subject;
        if (sessionId is not null)
            claims["sid"] = sessionId;

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            TokenType = MatIdentityLogoutToken.TokenType,
            SigningCredentials = credentials ?? TrakrFactory.SigningCredentials,
        });
    }
}

/// <summary>Without <c>MATIDENTITY_AUTHORITY</c> there is no back-channel logout.</summary>
public sealed class BackchannelLogoutWithoutIdentityTests(TrakrFactory site) : IClassFixture<TrakrFactory>
{
    [Fact]
    public async Task Without_matidentity_there_is_no_endpoint()
    {
        var response = await site.CreateClient().PostAsync("/signout-backchannel",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = BackchannelLogoutTests.Token("sub", "sid") }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
