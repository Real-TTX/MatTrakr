namespace MatTrakr.Data;

/// <summary>
/// Central place for status wording and badges so the item detail, list grid
/// and public share view render them identically (incl. the Partial/season case).
/// </summary>
public static class StatusDisplay
{
    public static string DoneWord(MediaType type) => type == MediaType.Books ? "Gelesen" : "Gesehen";
    public static string OpenWord(MediaType type) => type == MediaType.Books ? "Ungelesen" : "Ungesehen";

    /// <summary>Bootstrap badge class, icon and label for an item's status.</summary>
    public static (string Css, string Icon, string Text) Badge(ListItem item, MediaType type) => item.Status switch
    {
        ItemStatus.Done => ("text-bg-success", "bi-check-lg", DoneWord(type)),
        ItemStatus.Partial => ("text-bg-warning", "bi-hourglass-split",
            item.TotalSeasons is > 0 ? $"Teilweise ({item.WatchedSeasons}/{item.TotalSeasons})" : "Teilweise"),
        ItemStatus.Abandoned => ("text-bg-danger", "bi-x-circle", "Abgebrochen"),
        _ => ("text-bg-secondary", "bi-hourglass", OpenWord(type)),
    };
}
