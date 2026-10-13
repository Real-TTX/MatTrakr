using System.Linq;
using Microsoft.AspNetCore.Html;

namespace MatTrakr;

/// <summary>
/// Small view helpers for the MatUi toolkit. <see cref="Icon"/> renders one icon of the
/// sprite (<c>wwwroot/matui/matui-icons.svg</c>, inline once per page); it accepts either a
/// MatUi id ("tv") or a legacy Bootstrap-icon name ("bi-tv"), so pages can keep familiar names.
/// </summary>
public static class Ui
{
    // Legacy Bootstrap-icon name (without the "bi-" prefix) -> MatUi sprite id (without "mat-i-").
    // Names that already match a sprite id need no entry.
    private static readonly Dictionary<string, string> IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["align-bottom"] = "arrow-down",
        ["align-top"] = "arrow-up",
        ["arrow-left"] = "back",
        ["arrow-right-circle"] = "arrow-right",
        ["box-arrow-in-right"] = "login",
        ["box-arrow-right"] = "logout",
        ["box-arrow-up-right"] = "external",
        ["card-text"] = "file-text",
        ["check-all"] = "check-square",
        ["check-lg"] = "check",
        ["circle-half"] = "monitor",
        ["clipboard"] = "copy",
        ["collection"] = "layers",
        ["collection-play"] = "tv",
        ["exclamation-triangle"] = "alert",
        ["exclamation-triangle-fill"] = "alert",
        ["film"] = "play",
        ["funnel"] = "filter",
        ["gear"] = "settings",
        ["hdd-stack"] = "database",
        ["hourglass"] = "clock",
        ["hourglass-split"] = "clock",
        ["house"] = "home",
        ["info-circle"] = "info",
        ["link-45deg"] = "link",
        ["list"] = "menu",
        ["moon-stars-fill"] = "moon",
        ["pencil"] = "edit",
        ["pencil-square"] = "edit",
        ["people"] = "users",
        ["person"] = "user",
        ["person-check"] = "user-circle",
        ["person-circle"] = "user-circle",
        ["person-gear"] = "user-circle",
        ["person-plus"] = "users",
        ["plus-circle"] = "plus",
        ["plus-lg"] = "plus",
        ["share"] = "link",
        ["shield-lock"] = "shield",
        ["sort-down"] = "sort",
        ["star-fill"] = "star",
        ["sun-fill"] = "sun",
        ["tags"] = "tag",
        ["x-lg"] = "x",
        ["zoom-in"] = "search",
    };

    /// <summary>One icon of the sprite as inline SVG. <paramref name="name"/> may be a MatUi id or a "bi-*" name.</summary>
    public static IHtmlContent Icon(string name, string? cssClass = null)
    {
        var id = Resolve(name);
        var cls = string.IsNullOrEmpty(cssClass) ? "mat-icon" : "mat-icon " + cssClass;
        return new HtmlString($"<svg class=\"{cls}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-{id}\"></use></svg>");
    }

    private static string Resolve(string name)
    {
        var key = name.StartsWith("bi-", StringComparison.OrdinalIgnoreCase) ? name[3..] : name;
        return IconMap.TryGetValue(key, out var mapped) ? mapped : key;
    }

    /// <summary>
    /// The texts the toolkit writes itself (dialog buttons, the tip after Copy), in German. They go on the
    /// <c>&lt;html&gt;</c> element; the markup of a page keeps its own words (ADOPTING §3a).
    /// </summary>
    public static IHtmlContent ToolkitTexts { get; } = new HtmlString(
        "data-dlg-ok=\"OK\" data-dlg-cancel=\"Abbrechen\" data-dlg-close=\"Schließen\" " +
        "data-dlg-confirm=\"Bitte bestätigen\" data-dlg-notice=\"Hinweis\" data-dlg-input=\"Eingabe\" " +
        "data-copied=\"Kopiert\"");

    /// <summary>
    /// The glyph of the MatTrakr logo: a progress ring with a play triangle. It goes inside a <c>.mat-logo</c> tile
    /// (the flat accent square) and follows the tile's text colour, so it is right in light and dark. The same
    /// drawing is <c>wwwroot/favicon.svg</c> (with the tile), which the PWA icons are rendered from.
    /// </summary>
    public static IHtmlContent Logo { get; } = new HtmlString(
        "<svg class=\"mt-logo-glyph\" viewBox=\"11 11 42 42\" aria-hidden=\"true\" focusable=\"false\">" +
        "<circle cx=\"32\" cy=\"32\" r=\"18\" fill=\"none\" stroke=\"currentColor\" stroke-opacity=\".3\" stroke-width=\"6\"/>" +
        "<circle cx=\"32\" cy=\"32\" r=\"18\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-dasharray=\"79 114\" transform=\"rotate(-90 32 32)\"/>" +
        "<polygon points=\"26.5,23 26.5,41 41.5,32\" fill=\"currentColor\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>" +
        "</svg>");

    private static string? _sprite;

    /// <summary>The icon sprite, read once and placed inline at the top of the body so every <see cref="Icon"/> can reference it.</summary>
    public static IHtmlContent Sprite(string webRootPath)
    {
        _sprite ??= File.ReadAllText(Path.Combine(webRootPath, "matui", "matui-icons.svg"));
        return new HtmlString(_sprite);
    }

    /// <summary>Up to two initials for an avatar.</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = parts[0][..1];
        var second = parts.Length > 1 ? parts[^1][..1] : string.Empty;
        return (first + second).ToUpperInvariant();
    }
}
