namespace MatTrakr.ViewComponents;

// ---------------------------------------------------------------------------
// Shared view models for the reusable UI controls (_TableToolbar, _Pagination,
// _FormActionsBar, _TabBar). Each carries an Id prefix so multiple instances
// of the same control can coexist on one page.
// ---------------------------------------------------------------------------

/// <summary>One select-based filter in the toolbar.</summary>
public class ToolbarFilter
{
    public string Name { get; set; } = string.Empty;          // query parameter name
    public string Label { get; set; } = string.Empty;
    public string? Selected { get; set; }
    public List<(string Value, string Text)> Options { get; set; } = new();
}

/// <summary>Sort option offered by the toolbar sort dropdown.</summary>
public class ToolbarSortOption
{
    public string Value { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Toolbar above every table/grid: free-text search, filters, sorting.
/// Submits as GET to the current page, preserving extra route values.
/// </summary>
public class TableToolbarModel
{
    public string Id { get; set; } = "tb";
    public string? SearchValue { get; set; }
    public string SearchParam { get; set; } = "q";
    public string SearchPlaceholder { get; set; } = "Suchen …";
    public List<ToolbarFilter> Filters { get; set; } = new();
    public List<ToolbarSortOption> SortOptions { get; set; } = new();
    public string? SortValue { get; set; }
    public string SortParam { get; set; } = "sort";
    /// <summary>Hidden fields to keep (e.g. route/query values not owned by the toolbar).</summary>
    public Dictionary<string, string> Hidden { get; set; } = new();
}

public class PaginationModel
{
    public string Id { get; set; } = "pg";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public string PageParam { get; set; } = "page";
    /// <summary>Query values to preserve in page links (search, filters, sort).</summary>
    public Dictionary<string, string?> Query { get; set; } = new();

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>
/// Standard action row for forms: positive → negative left-aligned
/// (Save, Back), destructive actions pushed to the right with extra space.
/// </summary>
public class FormActionsModel
{
    public string SaveText { get; set; } = "Speichern";
    public string? SaveIcon { get; set; } = "bi-check-lg";
    public string? BackUrl { get; set; }
    public string BackText { get; set; } = "Zurück";
    /// <summary>Set to show a delete button (posts to this page handler).</summary>
    public string? DeleteHandler { get; set; }
    public string DeleteText { get; set; } = "Löschen";
    public string? DeleteConfirm { get; set; } = "Wirklich löschen?";
    /// <summary>Route values serialized as hidden inputs of the delete form.</summary>
    public Dictionary<string, string> DeleteRouteValues { get; set; } = new();
}

public class TabItem
{
    public string Key { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Url { get; set; }   // link-style tabs (navigation)
    public int? Badge { get; set; }
}

public class TabBarModel
{
    public string Id { get; set; } = "tabs";
    public string ActiveKey { get; set; } = string.Empty;
    public List<TabItem> Tabs { get; set; } = new();
}
