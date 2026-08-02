using Microsoft.EntityFrameworkCore;
using MatTrakr.Data;
using MatTrakr.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Persistent data location (SQLite DB + optional mounted config) ----------
// In Docker this is the mounted volume (/app/data); locally it defaults to
// <contentRoot>/data. The folder holds the DB and a config/settings.json that
// can be edited without rebuilding the image.
var dataPath = builder.Configuration["Storage:DataPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataPath);
Directory.CreateDirectory(Path.Combine(dataPath, "config"));

builder.Configuration.AddJsonFile(
    Path.Combine(dataPath, "config", "settings.json"),
    optional: true, reloadOnChange: true);

var dbPath = Path.Combine(dataPath, "mattrakr.db");

// --- Services ----------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddRazorPages();

var app = builder.Build();

// --- Apply migrations on startup --------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// --- Pipeline ----------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
