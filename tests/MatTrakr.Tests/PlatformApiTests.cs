using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatTrakr.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatTrakr.Tests;

/// <summary>The platform contract of MatDesign docs/PLATFORM.md as MatTrakr answers it: shapes, status codes, access.</summary>
public sealed class PlatformApiTests(TrakrFactory site) : IClassFixture<TrakrFactory>
{
    private static readonly string[] Protected = ["/api/state", "/api/state/users", "/api/configure"];

    [Fact]
    public async Task Health_answers_without_a_key()
    {
        var response = await site.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("mattrakr", doc.RootElement.GetProperty("app").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("version").GetString()));
        Assert.Contains(doc.RootElement.GetProperty("checks").EnumerateArray(), c => c.GetProperty("name").GetString() == "database" && c.GetProperty("status").GetString() == "ok");
    }

    [Fact]
    public async Task The_definition_answers_without_a_key_and_with_an_etag()
    {
        var client = site.CreateClient();
        var response = await client.GetAsync("/api");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var etag = response.Headers.ETag!.Tag;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(request)).StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("contract").GetInt32());
        var app = root.GetProperty("app");
        Assert.Equal("mattrakr", app.GetProperty("id").GetString());
        Assert.Equal("MatTrakr", app.GetProperty("name").GetString());
        Assert.Equal("#6c5ce7", app.GetProperty("accent").GetString());
        Assert.Equal("/", app.GetProperty("home").GetString());
        Assert.True(root.GetProperty("auth").GetProperty("apiKey").GetBoolean());
        Assert.False(root.GetProperty("auth").TryGetProperty("identity", out _));

        Assert.Equal(["overview", "settings"], root.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["admin", "verwalter", "user"], root.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("key").GetString()));

        var openAdmin = root.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "openAdmin");
        Assert.Equal("link", openAdmin.GetProperty("kind").GetString());
        Assert.Equal("/Admin/Users", openAdmin.GetProperty("url").GetString());

        Assert.All(root.GetProperty("configure").EnumerateArray(), f => Assert.Equal("secret", f.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task The_definition_follows_accept_language()
    {
        var client = site.CreateClient();
        using var german = new HttpRequestMessage(HttpMethod.Get, "/api");
        german.Headers.TryAddWithoutValidation("Accept-Language", "de-DE");
        using var english = new HttpRequestMessage(HttpMethod.Get, "/api");
        english.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        using var de = JsonDocument.Parse(await (await client.SendAsync(german)).Content.ReadAsStringAsync());
        using var en = JsonDocument.Parse(await (await client.SendAsync(english)).Content.ReadAsStringAsync());
        Assert.Equal("Übersicht", de.RootElement.GetProperty("pages")[0].GetProperty("label").GetString());
        Assert.Equal("Overview", en.RootElement.GetProperty("pages")[0].GetProperty("label").GetString());
        Assert.Equal("Manager", en.RootElement.GetProperty("roles")[1].GetProperty("label").GetString());
    }

    [Fact]
    public async Task State_configure_and_actions_need_the_key()
    {
        var anonymous = site.CreateClient();
        foreach (var path in Protected)
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/configure", new { values = new { tmdbApiKey = "x" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/actions/cleanupSessions", null)).StatusCode);

        var wrong = site.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/api/state")).StatusCode);

        // A bearer token is no way in while MatIdentity is not configured.
        var bearer = site.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "abc.def.ghi");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/state")).StatusCode);
    }

    [Fact]
    public async Task State_answers_every_tile_with_the_real_numbers_and_one_value_by_key()
    {
        await AddSampleDataAsync();

        var api = site.Api();
        using var definition = JsonDocument.Parse(await api.GetStringAsync("/api"));
        using var state = JsonDocument.Parse(await api.GetStringAsync("/api/state"));
        var values = state.RootElement.GetProperty("values");
        Assert.True(state.RootElement.TryGetProperty("time", out _));

        foreach (var key in definition.RootElement.GetProperty("state").EnumerateArray().Select(s => s.GetProperty("key").GetString()!))
        {
            Assert.True(values.TryGetProperty(key, out var value), $"No value for {key}");
            Assert.True(value.TryGetProperty("value", out _), $"{key} has no value field");
        }

        int Number(string key) => values.GetProperty(key).GetProperty("value").GetInt32();

        // The seed (the administrator with the three default lists) plus the sample data.
        Assert.Equal(1, Number("users"));
        Assert.Equal(3, Number("lists"));
        Assert.Equal(2, Number("items"));
        Assert.Equal(1, Number("itemsDone"));
        Assert.Equal(1, Number("ratings"));
        Assert.Equal(1, Number("sharedLists"));
        Assert.Equal(1234, Number("covers"));
        Assert.True(values.GetProperty("database").GetProperty("value").GetInt64() > 0);
        Assert.Equal(JsonValueKind.String, values.GetProperty("lastActivity").GetProperty("value").ValueKind);
        Assert.Equal("warn", values.GetProperty("tmdb").GetProperty("value").GetString());
        Assert.False(string.IsNullOrEmpty(values.GetProperty("tmdb").GetProperty("text").GetString()));

        var one = await api.GetAsync("/api/state/users");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        using (var doc = JsonDocument.Parse(await one.Content.ReadAsStringAsync()))
        {
            Assert.Equal("users", doc.RootElement.GetProperty("key").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("value").GetInt32());
        }

        var unknown = await api.GetAsync("/api/state/nothing");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using (var doc = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("nothing", out _));
        }
    }

    [Fact]
    public async Task Configure_answers_every_field_and_never_a_secret()
    {
        var api = site.Api();
        const string secret = "tmdb-secret-value-42";

        var saved = await api.PutAsJsonAsync("/api/configure", new { values = new { tmdbApiKey = secret } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedText = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, savedText);

        using var definition = JsonDocument.Parse(await api.GetStringAsync("/api"));
        var text = await api.GetStringAsync("/api/configure");
        using var configuration = JsonDocument.Parse(text);
        var values = configuration.RootElement.GetProperty("values");

        foreach (var field in definition.RootElement.GetProperty("configure").EnumerateArray())
        {
            var key = field.GetProperty("key").GetString()!;
            Assert.True(values.TryGetProperty(key, out var value), $"No value for {key}");
            Assert.Equal(JsonValueKind.Object, value.ValueKind);
            Assert.Equal(["isSet"], value.EnumerateObject().Select(p => p.Name));
        }

        Assert.True(values.GetProperty("tmdbApiKey").GetProperty("isSet").GetBoolean());
        Assert.False(values.GetProperty("googleBooksApiKey").GetProperty("isSet").GetBoolean());
        Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain(TrakrFactory.ApiKey, text);
        Assert.DoesNotContain(TrakrFactory.ApiKey, definition.RootElement.GetRawText());

        // The key is where the settings page reads it, and the tile follows.
        Assert.Contains(secret, await File.ReadAllTextAsync(Path.Combine(site.DataFolder, "config", "settings.json")));
        using (var state = JsonDocument.Parse(await api.GetStringAsync("/api/state/tmdb")))
        {
            Assert.Equal("ok", state.RootElement.GetProperty("value").GetString());
        }

        // A secret that is not sent stays, "" clears it.
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync("/api/configure", new { values = new { googleBooksApiKey = "g" } })).StatusCode);
        using (var after = JsonDocument.Parse(await api.GetStringAsync("/api/configure")))
        {
            Assert.True(after.RootElement.GetProperty("values").GetProperty("tmdbApiKey").GetProperty("isSet").GetBoolean());
            Assert.True(after.RootElement.GetProperty("values").GetProperty("googleBooksApiKey").GetProperty("isSet").GetBoolean());
        }

        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync("/api/configure", new { values = new { tmdbApiKey = "", googleBooksApiKey = "" } })).StatusCode);
        using (var cleared = JsonDocument.Parse(await api.GetStringAsync("/api/configure")))
        {
            Assert.False(cleared.RootElement.GetProperty("values").GetProperty("tmdbApiKey").GetProperty("isSet").GetBoolean());
            Assert.False(cleared.RootElement.GetProperty("values").GetProperty("googleBooksApiKey").GetProperty("isSet").GetBoolean());
        }
    }

    [Fact]
    public async Task Configure_refuses_a_wrong_value_and_saves_nothing_then()
    {
        var api = site.Api();
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync("/api/configure", new { values = new { tmdbApiKey = "" } })).StatusCode);

        var bad = await api.PutAsJsonAsync("/api/configure", new { values = new { tmdbApiKey = "good", nothing = 1 } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using (var errors = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()))
        {
            Assert.True(errors.RootElement.GetProperty("errors").TryGetProperty("nothing", out _));
        }

        foreach (var wrong in new object[] { new { tmdbApiKey = 5 }, new { googleBooksApiKey = true }, new { tmdbApiKey = new string('x', 201) } })
        {
            var response = await api.PutAsJsonAsync("/api/configure", new { values = wrong });
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(wrong)}: {(int)response.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await api.PutAsync("/api/configure", JsonContent.Create(new { nothing = 1 }))).StatusCode);

        using var unchanged = JsonDocument.Parse(await api.GetStringAsync("/api/configure"));
        Assert.False(unchanged.RootElement.GetProperty("values").GetProperty("tmdbApiKey").GetProperty("isSet").GetBoolean());
    }

    [Fact]
    public async Task Actions_run_and_an_unknown_one_is_404()
    {
        var api = site.Api();

        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync(u => u.Username == "admin");
            db.UserSessions.Add(new UserSession { UserId = admin.Id, Token = "expired-" + Guid.NewGuid().ToString("N"), ExpiresDate = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var cleanup = await api.PostAsync("/api/actions/cleanupSessions", null);
        Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
        using (var doc = JsonDocument.Parse(await cleanup.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("1 abgelaufene Sitzung gelöscht.", doc.RootElement.GetProperty("message").GetString());
        }

        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.UserSessions.AnyAsync(s => s.ExpiresDate < DateTime.UtcNow));
        }

        // Without a TMDb key the test cannot run now: 409 with a message, and no call to TMDb.
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync("/api/configure", new { values = new { tmdbApiKey = "" } })).StatusCode);
        var test = await api.PostAsync("/api/actions/testTmdb", null);
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
        using (var doc = JsonDocument.Parse(await test.Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync("/api/actions/nothing", null)).StatusCode);
        // A link action is not run by POST.
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync("/api/actions/openAdmin", null)).StatusCode);
    }

    /// <summary>Two items in the administrator's lists (one done), a rating, a public link and an uploaded cover of 1234 bytes.</summary>
    private async Task AddSampleDataAsync()
    {
        using var scope = site.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.SingleAsync(u => u.Username == "admin");
        var movies = await db.Lists.SingleAsync(l => l.OwnerUserId == admin.Id && l.Type == MediaType.Movies);

        var seen = new ListItem { ListId = movies.Id, Title = "Alien", Source = ExternalSource.Manual, Status = ItemStatus.Done };
        var open = new ListItem { ListId = movies.Id, Title = "Aliens", Source = ExternalSource.Manual };
        db.ListItems.AddRange(seen, open);
        await db.SaveChangesAsync();

        db.ItemRatings.Add(new ItemRating { ListItemId = seen.Id, UserId = admin.Id, Stars = 5 });
        db.ListShares.Add(new ListShare { ListId = movies.Id, Token = Guid.NewGuid().ToString("N") });
        db.ItemCovers.Add(new ItemCover { ListItemId = open.Id, Data = new byte[1234] });
        await db.SaveChangesAsync();
    }
}

/// <summary>Without <c>MAT_API_KEY</c> the key is off: the protected endpoints answer 401 to every key.</summary>
public sealed class PlatformWithoutKeyTests(NoKeyFactory site) : IClassFixture<NoKeyFactory>
{
    [Fact]
    public async Task Every_protected_endpoint_is_401_and_the_definition_says_so()
    {
        var client = site.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TrakrFactory.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/configure")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/actions/cleanupSessions", null)).StatusCode);

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api"));
        Assert.False(doc.RootElement.GetProperty("auth").GetProperty("apiKey").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }
}
