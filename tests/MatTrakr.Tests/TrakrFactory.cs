using MatPlatform;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

// The factories hand their settings to Program through environment variables (Program reads them before the host is built),
// so the test classes run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MatTrakr.Tests;

/// <summary>
/// The whole app in memory on its own empty data folder, in Development. <see cref="ApiKey"/> is <c>MAT_API_KEY</c> unless
/// <c>withKey</c> is false; with an <c>authority</c> the sign-in through MatIdentity is on, with a fixed OpenID configuration so no
/// MatIdentity has to run (the tests look at the redirects only).
/// </summary>
public class TrakrFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-key-0123456789";

    private readonly string _data = Path.Combine(Path.GetTempPath(), "mattrakr-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _authority;
    private readonly bool _reachable;

    public TrakrFactory()
        : this(withKey: true, authority: null)
    {
    }

    protected TrakrFactory(bool withKey, string? authority, bool reachable = true)
    {
        _authority = authority;
        _reachable = reachable;
        Environment.SetEnvironmentVariable("Storage__DataPath", _data);
        Environment.SetEnvironmentVariable("MATTRAKR_ADMIN_PASSWORD", null);
        Environment.SetEnvironmentVariable("Tmdb__ApiKey", null);
        Environment.SetEnvironmentVariable("GoogleBooks__ApiKey", null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ApiKeyVariable, withKey ? ApiKey : null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.AuthorityVariable, authority);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ClientIdVariable, null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ClientSecretVariable, authority is null ? null : "test-secret");
        Environment.SetEnvironmentVariable(MatPlatformSettings.InternalUrlVariable, null);
    }

    public string DataFolder => _data;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_authority is not null && _reachable)
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = _authority,
                AuthorizationEndpoint = _authority + "/connect/authorize",
                TokenEndpoint = _authority + "/connect/token",
                EndSessionEndpoint = _authority + "/connect/logout",
            };

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<OpenIdConnectOptions>(MatIdentitySignIn.Scheme, options =>
                {
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
                services.PostConfigure<JwtBearerOptions>(MatApiAccess.BearerScheme, options =>
                {
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        }
    }

    /// <summary>A client that keeps cookies and does not follow redirects (the tests look at each step).</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public HttpClient Api()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file for a moment; the temp folder is cleaned by the system later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Without <c>MAT_API_KEY</c>: the protected endpoints answer 401 to everyone.</summary>
public sealed class NoKeyFactory() : TrakrFactory(withKey: false, authority: null);

/// <summary>With <c>MATIDENTITY_AUTHORITY</c> (a MatIdentity that does not need to run).</summary>
public sealed class IdentityFactory() : TrakrFactory(withKey: true, authority: IdentityFactory.Authority)
{
    public const string Authority = "http://identity.test";
}

/// <summary>With <c>MATIDENTITY_AUTHORITY</c> pointing at an address where nothing answers (MatIdentity is down).</summary>
public sealed class UnreachableIdentityFactory() : TrakrFactory(withKey: true, authority: "http://127.0.0.1:9", reachable: false);

/// <summary>Where the repository is, for the files the tests read (the sprite, the golden files).</summary>
public static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MatTrakr.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("MatTrakr.slnx not found above " + AppContext.BaseDirectory);
        }
    }

    public static string WebRoot => Path.Combine(Root, "src", "MatTrakr", "wwwroot");

    public static string Sprite => Path.Combine(WebRoot, "matui", "matui-icons.svg");
}
