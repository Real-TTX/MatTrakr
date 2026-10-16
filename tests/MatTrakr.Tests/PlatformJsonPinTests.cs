using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MatTrakr.Tests;

/// <summary>
/// Pins the JSON of the platform contract as Mattiverse and MatIdentity read it: the definition (<c>GET /api</c>, German and English) word for word,
/// and the shape of <c>/api/state</c>, <c>/api/state/{key}</c>, <c>/api/configure</c> and <c>/api/health</c> (names, order and kinds of the values).
/// The expected files are in <c>Golden/</c>; <c>MATTRAKR_WRITE_GOLDEN=1</c> writes them anew (only on purpose, after a change of the definition).
/// Escaping is not part of the comparison, everything else is.
/// </summary>
public sealed class PlatformJsonPinTests(TrakrFactory site) : IClassFixture<TrakrFactory>
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Theory]
    [InlineData("de-DE", "api.de.json")]
    [InlineData("en-US,en;q=0.9", "api.en.json")]
    public async Task The_definition_is_unchanged(string language, string golden)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api");
        request.Headers.TryAddWithoutValidation("Accept-Language", language);
        var node = JsonNode.Parse(await (await site.CreateClient().SendAsync(request)).Content.ReadAsStringAsync())!;
        node["app"]!["version"] = "<version>";

        Compare(golden, node.ToJsonString(Pretty));
    }

    [Theory]
    [InlineData("/api/state", "state.shape.json")]
    [InlineData("/api/state/users", "state-users.shape.json")]
    [InlineData("/api/configure", "configure.shape.json")]
    [InlineData("/api/health", "health.shape.json")]
    public async Task The_shape_of_the_values_is_unchanged(string path, string golden)
    {
        var node = JsonNode.Parse(await site.Api().GetStringAsync(path));

        Compare(golden, Shape(node)!.ToJsonString(Pretty));
    }

    /// <summary>The same tree with every value replaced by its kind (<c>"&lt;number&gt;"</c>, <c>"&lt;string&gt;"</c>, ...).</summary>
    private static JsonNode? Shape(JsonNode? node) => node switch
    {
        null => "<null>",
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Shape(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Shape).ToArray()),
        _ => node.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False => "<bool>",
            var kind => "<" + kind.ToString().ToLowerInvariant() + ">",
        },
    };

    private static void Compare(string golden, string actual)
    {
        var file = Path.Combine(Repo.Root, "tests", "MatTrakr.Tests", "Golden", golden);
        actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

        if (Environment.GetEnvironmentVariable("MATTRAKR_WRITE_GOLDEN") == "1")
        {
            File.WriteAllText(file, actual);
            return;
        }

        Assert.True(File.Exists(file), $"{file} is missing (MATTRAKR_WRITE_GOLDEN=1 writes it).");
        Assert.Equal(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }
}
