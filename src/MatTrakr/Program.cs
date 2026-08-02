using Microsoft.AspNetCore.Authentication;
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

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ListAccessService>();
builder.Services.AddScoped<MediaSearchService>();

builder.Services.AddHttpClient("TMDb", c =>
{
    c.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("GoogleBooks", c =>
{
    c.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("OpenLibrary", c =>
{
    c.BaseAddress = new Uri("https://openlibrary.org/");
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("MatTrakr/1.0");
});

builder.Services
    .AddAuthentication(AuthConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(AuthConstants.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(nameof(UserRole.Admin)));
    options.AddPolicy(Policies.ManagerOrAdmin, p =>
        p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Verwalter)));
});

builder.Services.AddRazorPages(options =>
{
    // Everything requires auth by default; carve out the public/anonymous areas.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Account");
    options.Conventions.AllowAnonymousToFolder("/Public");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AuthorizeFolder("/Admin", Policies.AdminOnly);
});

var app = builder.Build();

// --- Migrate + seed on startup ----------------------------------------------
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
await DbSeeder.SeedAsync(app.Services);

// --- Pipeline ----------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Force a password change before anything else once flagged.
app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapRazorPages();

app.Run();
