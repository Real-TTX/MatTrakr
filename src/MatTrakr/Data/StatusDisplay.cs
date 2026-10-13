namespace MatTrakr.Data;

/// <summary>
/// Central place for status wording and badges so the item detail, list grid
/// and public share view render them identically (incl. the Partial/season case).
/// </summary>
public static class StatusDisplay
{
    public static string DoneWord(MediaType type) => type == MediaType.Books ? "Gelesen" : "Gesehen";
    public static string OpenWord(MediaType type) => type == MediaType.Books ? "Ungelesen" : "Ungesehen";

    /// <summary>Status key ("open"/"done"/"partial"/"abandoned"), MatUi icon name and label for an item's status.</summary>
    public static (string Key, string Icon, string Text) Badge(ListItem item, MediaType type) => item.Status switch
    {
        ItemStatus.Done => ("done", "check-circle", DoneWord(type)),
        ItemStatus.Partial => ("partial", "clock",
            item.TotalSeasons is > 0 ? $"Teilweise ({item.WatchedSeasons}/{item.TotalSeasons})" : "Teilweise"),
        ItemStatus.Abandoned => ("abandoned", "x-circle", "Abgebrochen"),
        _ => ("open", "eye-off", OpenWord(type)),
    };

    /// <summary>MatUi pill variant class for a status key.</summary>
    public static string PillClass(string key) => key switch
    {
        "done" => "mat-pill--ok",
        "partial" => "mat-pill--warn",
        "abandoned" => "mat-pill--error",
        _ => "mat-pill--plain",
    };
}
