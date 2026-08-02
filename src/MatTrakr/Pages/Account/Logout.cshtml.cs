using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatTrakr.Services;

namespace MatTrakr.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly AuthService _auth;

    public LogoutModel(AuthService auth) => _auth = auth;

    public async Task<IActionResult> OnPostAsync()
    {
        await _auth.SignOutAsync(HttpContext);
        return Redirect("/Account/Login");
    }

    // Convenience: allow GET logout too (e.g. direct link).
    public Task<IActionResult> OnGetAsync() => OnPostAsync();
}
