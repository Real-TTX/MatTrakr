using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;

namespace MatTrakr.Pages.Admin.Assignments;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;

    public EditModel(AppDbContext db) => _db = db;

    [BindProperty] public long Id { get; set; }
    [BindProperty] public long ListId { get; set; }
    [BindProperty] public long AssignedUserId { get; set; }

    public bool IsNew => Id == 0;
    public string? Error { get; set; }

    public List<TrackList> AllLists { get; set; } = new();
    public List<User> Verwalter { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long? id)
    {
        await LoadOptionsAsync();

        if (id is null or 0) return Page();

        var assignment = await _db.ListAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (assignment is null) return RedirectToPage("Index");

        Id = assignment.Id;
        ListId = assignment.ListId;
        AssignedUserId = assignment.AssignedUserId;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadOptionsAsync();

        var list = await _db.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == ListId);
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == AssignedUserId);

        if (list is null || user is null)
        {
            Error = "Bitte Liste und Verwalter auswählen.";
            return Page();
        }

        if (list.OwnerUserId == user.Id)
        {
            Error = "Eigene Listen müssen nicht zugewiesen werden.";
            return Page();
        }

        var duplicate = await _db.ListAssignments.AnyAsync(a =>
            a.ListId == ListId && a.AssignedUserId == AssignedUserId && a.Id != Id);
        if (duplicate)
        {
            Error = "Diese Zuweisung existiert bereits.";
            return Page();
        }

        if (IsNew)
        {
            _db.ListAssignments.Add(new ListAssignment { ListId = ListId, AssignedUserId = AssignedUserId });
        }
        else
        {
            var assignment = await _db.ListAssignments.FirstOrDefaultAsync(a => a.Id == Id);
            if (assignment is null) return RedirectToPage("Index");
            assignment.ListId = ListId;
            assignment.AssignedUserId = AssignedUserId;
        }

        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var assignment = await _db.ListAssignments.FirstOrDefaultAsync(a => a.Id == id);
        if (assignment is not null)
        {
            _db.ListAssignments.Remove(assignment);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    private async Task LoadOptionsAsync()
    {
        AllLists = await _db.Lists.AsNoTracking()
            .Include(l => l.Owner)
            .OrderBy(l => l.Owner!.Username).ThenBy(l => l.Name)
            .ToListAsync();

        // Verwalter and admins can be assigned; plain users cannot.
        Verwalter = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Verwalter || u.Role == UserRole.Admin))
            .OrderBy(u => u.Username)
            .ToListAsync();
    }
}
