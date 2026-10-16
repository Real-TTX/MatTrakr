using System.Text.Json;
using MatPlatform;
using MatTrakr.Services;

namespace MatTrakr.Platform;

/// <summary>
/// MatTrakr as an app of the platform contract (MatDesign docs/PLATFORM.md), on the shared package MatPlatform (the folder
/// <c>MatPlatform/</c>, a copy): how Mattiverse shows it (<see cref="Describe"/>: tiles, settings, buttons), what its values are
/// and what the buttons do. The settings are the two API keys of Administration → Einstellungen, kept in the same
/// <c>data/config/settings.json</c>. Texts are German, or English when the caller asks for it with <c>Accept-Language</c>.
/// Only services are called here (no DbContext in the web layer).
/// </summary>
public sealed class TrakrPlatform(
    InstanceStatusService status,
    SettingsService settings,
    MediaSearchService search,
    AuthService auth,
    ILogger<TrakrPlatform> log) : IMatPlatformState, IMatPlatformConfiguration, IMatPlatformActions, IMatPlatformHealth
{
    /// <summary>The id of MatTrakr in the platform (and the default client id in MatIdentity).</summary>
    public const string AppId = "mattrakr";

    /// <summary>The accent of MatTrakr in MatDesign's AppRegistry.</summary>
    public const string Accent = "#6c5ce7";

    /// <summary>The fields of the platform form and the configuration key each one is.</summary>
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["tmdbApiKey"] = "Tmdb:ApiKey",
        ["googleBooksApiKey"] = "GoogleBooks:ApiKey",
    };

    /// <summary>The version of the running build (set by the image), <c>local</c> outside of it.</summary>
    public static string Version => Environment.GetEnvironmentVariable("MATTRAKR_VERSION") is { Length: > 0 } v ? v : "local";

    /// <summary>The MatApiDefinition of MatTrakr (<c>GET /api</c>); <c>auth</c> and <c>endpoints</c> are filled in by MatPlatform.</summary>
    public static void Describe(MatPlatformBuilder o)
    {
        var T = o.T;

        o.App(
            AppId,
            "MatTrakr",
            T("Gesehene Filme, Serien und gelesene Bücher festhalten", "Keep track of what you have watched and read"),
            Version,
            "/favicon.svg",
            Accent);

        o.Page("overview", T("Übersicht", "Overview"), "dashboard",
            MatSection.Tiles("users", "lists", "items", "itemsDone", "ratings", "sharedLists", "covers", "database", "lastActivity", "tmdb"),
            MatSection.Actions("openAdmin", "openSettings", "openBackup", "testTmdb", "cleanupSessions"));
        o.Page("settings", T("Einstellungen", "Settings"), "settings",
            MatSection.Form(T("Cover-Suche", "Cover search"), "tmdbApiKey", "googleBooksApiKey"));

        o.Tile("users", T("Aktive Benutzer", "Active users"), icon: "users");
        o.Tile("lists", T("Listen", "Lists"), icon: "list");
        o.Tile("items", T("Einträge", "Entries"), icon: "layers");
        o.Tile("itemsDone", T("Gesehen oder gelesen", "Watched or read"), icon: "check-circle");
        o.Tile("ratings", T("Bewertungen", "Ratings"), icon: "star");
        o.Tile("sharedLists", T("Geteilte Listen", "Shared lists"), icon: "link");
        o.Tile("covers", T("Hochgeladene Cover", "Uploaded covers"), icon: "image", unit: "bytes");
        o.Tile("database", T("Datenbank", "Database"), icon: "database", unit: "bytes");
        o.Tile("lastActivity", T("Letzte Änderung", "Last change"), "time", icon: "clock");
        o.Tile("tmdb", T("Film- und Seriensuche", "Movie and series search"), "status", icon: "search");

        o.Field(new MatField("tmdbApiKey", T("TMDb-API-Key", "TMDb API key"), "secret")
        {
            Max = 200,
            Hint = T("Nötig für die Suche nach Filmen und Serien (kostenlos auf themoviedb.org). Wirkt sofort.",
                "Needed to search movies and series (free at themoviedb.org). Takes effect at once."),
        });
        o.Field(new MatField("googleBooksApiKey", T("Google-Books-API-Key", "Google Books API key"), "secret")
        {
            Max = 200,
            Hint = T("Optional: die Büchersuche geht auch ohne, ein Key erhöht nur das Tageskontingent.",
                "Optional: book search works without it, a key only raises the daily quota."),
        });

        o.LinkAction("openAdmin", T("Administration öffnen", "Open admin"), "external", "/Admin/Users");
        o.LinkAction("openSettings", T("Alle Einstellungen", "All settings"), "settings", "/Admin/Settings");
        o.LinkAction("openBackup", T("Sicherung", "Backup"), "archive", "/Admin/Backup");
        o.PostAction("testTmdb", T("TMDb-Key prüfen", "Test the TMDb key"), "plug");
        o.PostAction("cleanupSessions", T("Abgelaufene Sitzungen löschen", "Remove expired sessions"), "broom");

        o.Role("admin", T("Administrator", "Administrator"));
        o.Role("verwalter", T("Verwalter", "Manager"));
        o.Role("user", T("Benutzer", "User"));
    }

    public async Task<IReadOnlyList<MatHealthCheck>> CheckHealthAsync(MatPlatformCall call, CancellationToken ct)
    {
        if (!await status.CanReadDatabaseAsync(ct))
        {
            return [new MatHealthCheck("database", "down", call.T("Die Datenbank kann nicht gelesen werden.", "The database cannot be read."))];
        }

        var checks = new List<MatHealthCheck> { new("database", "ok") };
        if (await status.CountActiveAdminsAsync(ct) == 0)
        {
            checks.Add(new MatHealthCheck("administrators", "degraded", call.T("Kein aktiver Administrator.", "No active administrator.")));
        }

        return checks;
    }

    public async Task<IReadOnlyDictionary<string, MatStateValue>> GetStateAsync(MatPlatformCall call, CancellationToken ct)
    {
        var snapshot = await status.GetSnapshotAsync(ct);
        return new Dictionary<string, MatStateValue>
        {
            ["users"] = new(snapshot.ActiveUsers),
            ["lists"] = new(snapshot.Lists),
            ["items"] = new(snapshot.Items),
            ["itemsDone"] = new(snapshot.ItemsDone),
            ["ratings"] = new(snapshot.Ratings),
            ["sharedLists"] = new(snapshot.SharedLists),
            ["covers"] = new(snapshot.CoverBytes),
            ["database"] = new(snapshot.DatabaseBytes),
            ["lastActivity"] = new(snapshot.LastActivityUtc is { } last
                ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc))
                : null),
            ["tmdb"] = search.TmdbConfigured
                ? new("ok", "ok", call.T("API-Key eingetragen", "API key set"))
                : new("warn", "warn", call.T("Kein API-Key: nur Bücher und eigene Einträge", "No API key: books and own entries only")),
        };
    }

    public Task<IReadOnlyDictionary<string, object?>> GetConfigurationAsync(MatPlatformCall call, CancellationToken ct)
    {
        IReadOnlyDictionary<string, object?> values = Fields.ToDictionary(
            f => f.Key,
            f => (object?)MatSecret.Of(settings.GetEffective(f.Value)));
        return Task.FromResult(values);
    }

    public async Task<IReadOnlyDictionary<string, string>> ConfigureAsync(IReadOnlyDictionary<string, JsonElement> values, MatPlatformCall call, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var changes = new Dictionary<string, string?>();

        foreach (var (key, value) in values)
        {
            if (!Fields.TryGetValue(key, out var configKey))
            {
                errors[key] = call.T("Diese Einstellung gibt es nicht.", "There is no such setting.");
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                // "" clears the key, like an empty field on the settings page.
                changes[configKey] = value.GetString()!.Trim();
            }
            else
            {
                errors[key] = call.T("Bitte einen Text angeben.", "Please give a text.");
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        if (changes.Count > 0)
        {
            await settings.SaveAsync(changes);
            // MatTrakr has no activity log of its own: the change goes to the application log, with who made it and never a value.
            log.LogInformation("Settings {Keys} changed through the platform API by {Caller}",
                string.Join(", ", values.Keys), call.Caller);
        }

        return errors;
    }

    public async Task<MatActionResult?> RunActionAsync(string id, MatPlatformCall call, CancellationToken ct)
    {
        switch (id)
        {
            case "cleanupSessions":
                var removed = await auth.CleanupExpiredSessionsAsync(ct);
                return new MatActionResult(true, removed == 1
                    ? call.T("1 abgelaufene Sitzung gelöscht.", "1 expired session removed.")
                    : call.T($"{removed} abgelaufene Sitzungen gelöscht.", $"{removed} expired sessions removed."));
            case "testTmdb":
                var key = settings.GetEffective("Tmdb:ApiKey");
                if (string.IsNullOrWhiteSpace(key))
                {
                    return new MatActionResult(false, call.T("Es ist kein TMDb-API-Key eingetragen.", "No TMDb API key is set."));
                }

                var (ok, _) = await search.TestTmdbKeyAsync(key, ct);
                return ok
                    ? new MatActionResult(true, call.T("Der TMDb-Key ist gültig.", "The TMDb key is valid."))
                    : new MatActionResult(false, call.T("TMDb hat den Key abgelehnt oder ist nicht erreichbar.", "TMDb refused the key or cannot be reached."));
            default:
                return null;
        }
    }
}
