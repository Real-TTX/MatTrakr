using Microsoft.AspNetCore.Mvc;
using MatTrakr.Data;
using MatTrakr.Services;

namespace MatTrakr.ViewComponents;

public class SidebarViewModel
{
    public List<VisibleList> Lists { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Left navigation: all lists visible to the user + admin section.</summary>
public class SidebarViewComponent : ViewComponent
{
    private readonly ListAccessService _access;
    private readonly ICurrentUser _currentUser;

    public SidebarViewComponent(ListAccessService access, ICurrentUser currentUser)
    {
        _access = access;
        _currentUser = currentUser;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new SidebarViewModel();
        var userId = _currentUser.UserId;
        if (userId is not null)
        {
            var visible = await _access.GetVisibleListsAsync(userId.Value);
            // Sidebar shows pinned own lists plus every shared/assigned list.
            model.Lists = visible
                .Where(v => v.Access != ListAccess.Owner || v.List.IsFavorite)
                .ToList();
            model.IsAdmin = HttpContext.User.IsInRole(nameof(UserRole.Admin));
            model.DisplayName = HttpContext.User.FindFirst(AuthConstants.DisplayNameClaim)?.Value ?? "";
        }
        return View(model);
    }
}
