using System.Data;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Syncfusion.Blazor;
using AllWorkHRIS.Core;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host;
using AllWorkHRIS.Host.Components;
using AllWorkHRIS.Host.Hris.Repositories;
using AllWorkHRIS.Host.Hris.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Syncfusion license — must be executed immediately after CreateBuilder so 
//    no component can render before the license is registered. We read from 
//    user secrets via the configuration system, which requires CreateBuilder 
//    first.  Safe to place immediately after CreateBuilder — no components 
//    render during the CreateBuilder event.
// ---------------------------------------------------------------------------
var syncfusionKey = builder.Configuration["Syncfusion:LicenseKey"]
    ?? throw new InvalidOperationException(
        "Syncfusion license key not set. " +
        "Add it via: dotnet user-secrets set \"Syncfusion:LicenseKey\" \"your-key\"");

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);

// ---------------------------------------------------------------------------
// 1b. Dapper — column mapping and DateOnly type handlers
// ---------------------------------------------------------------------------
DefaultTypeMap.MatchNamesWithUnderscores = true;

SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
SqlMapper.AddTypeHandler(new NullableDateOnlyTypeHandler());

// ---------------------------------------------------------------------------
// 2. Validate required environment variables — fail fast before any wiring
// ---------------------------------------------------------------------------
EnvironmentValidator.ValidateRequired(
    "DATABASE_CONNECTION_STRING",
    "DATABASE_PROVIDER",
    "APP_ENVIRONMENT",
    "AUTH_AUTHORITY",
    "AUTH_CLIENT_ID",
    "AUTH_CLIENT_SECRET");

// ---------------------------------------------------------------------------
// 3. Discover modules via MEF
// ---------------------------------------------------------------------------
var modulesPath = Environment.GetEnvironmentVariable("MODULES_PATH") ?? "./modules";
var platformModules = ModuleDiscovery.DiscoverModules(modulesPath);

// ---------------------------------------------------------------------------
// 4. Replace default DI with Autofac
// ---------------------------------------------------------------------------
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(autofacBuilder =>
{
    // Core platform services
    autofacBuilder.RegisterType<ConnectionFactory>()
                  .As<IConnectionFactory>()
                  .SingleInstance();

    // Event bus — singleton; modules register handlers into it
    autofacBuilder.RegisterType<InProcessEventBus>()
                  .As<IEventPublisher>()
                  .SingleInstance();

    // Temporal context
    autofacBuilder.RegisterType<SystemTemporalContext>()
                  .As<ITemporalContext>()
                  .SingleInstance();

    // Lookup cache — must be singleton so it is initialised once and shared
    autofacBuilder.RegisterType<LookupCache>()
                  .As<ILookupCache>()
                  .SingleInstance();

    // -----------------------------------------------------------------------
    // HRIS core — always registered; no module discovery required
    // -----------------------------------------------------------------------

    // Repositories
    autofacBuilder.RegisterType<PersonRepository>()
                  .As<IPersonRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<PersonAddressRepository>()
                  .As<IPersonAddressRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<EmploymentRepository>()
                  .As<IEmploymentRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<AssignmentRepository>()
                  .As<IAssignmentRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<CompensationRepository>()
                  .As<ICompensationRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<OrgUnitRepository>()
                  .As<IOrgUnitRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<JobRepository>()
                  .As<IJobRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<PositionRepository>()
                  .As<IPositionRepository>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<EmployeeEventRepository>()
                  .As<IEmployeeEventRepository>()
                  .InstancePerLifetimeScope();

    // Services
    autofacBuilder.RegisterType<PersonService>()
                  .As<IPersonService>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<EmploymentService>()
                  .As<IEmploymentService>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<LifecycleEventService>()
                  .As<ILifecycleEventService>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<CompensationService>()
                  .As<ICompensationService>()
                  .InstancePerLifetimeScope();

    autofacBuilder.RegisterType<OrgStructureService>()
                  .As<IOrgStructureService>()
                  .InstancePerLifetimeScope();

    // Register each discovered module's services
    foreach (var module in platformModules)
        module.Register(autofacBuilder);
});

// ---------------------------------------------------------------------------
// 5. Collect menu contributions — HRIS first, then module-discovered
// ---------------------------------------------------------------------------
var hrisMenuItems = new List<MenuContribution>
{
    new MenuContribution
    {
        Label        = "Employees",
        Icon         = "HrisIcon",
        SortOrder    = 10,
        AccentColor  = "var(--module-hris)",
        BadgeLabel   = "HRIS",
        RequiredRole = "HrisViewer"
    },
    new MenuContribution
    {
        Label        = "Employees",
        Href         = "/hris/employees",
        Icon         = "HrisIcon",
        SortOrder    = 1,
        ParentLabel  = "Employees",
        RequiredRole = "HrisViewer"
    },
    new MenuContribution
    {
        Label        = "Organisation",
        Href         = "/hris/org",
        Icon         = "HrisIcon",
        SortOrder    = 2,
        ParentLabel  = "Employees",
        RequiredRole = "HrisViewer"
    },
    new MenuContribution
    {
        Label        = "Jobs & Positions",
        Href         = "/hris/jobs",
        Icon         = "HrisIcon",
        SortOrder    = 3,
        ParentLabel  = "Employees",
        RequiredRole = "HrisAdmin"
    }
};

var allMenuItems = hrisMenuItems
    .Concat(platformModules.SelectMany(m => m.GetMenuContributions()))
    .OrderBy(c => c.SortOrder)
    .ToList();

builder.Services.AddSingleton<IReadOnlyList<MenuContribution>>(allMenuItems);

// ---------------------------------------------------------------------------
// 6. Tenant registry — single dev tenant for Phase 1
//    Full multi-tenant switching (ADR-010) wired in Phase 8
// ---------------------------------------------------------------------------
var devConnectionFactory = new ConnectionFactory();
var tenantRegistry = new TenantRegistry(
[
    new TenantConfig
    {
        TenantId          = "00000000-0000-0000-0000-000000000001",
        ConnectionFactory = devConnectionFactory
    }
]);
builder.Services.AddSingleton(tenantRegistry);

// ---------------------------------------------------------------------------
// 7. Syncfusion Blazor services
// ---------------------------------------------------------------------------
builder.Services.AddSyncfusionBlazor();

// ---------------------------------------------------------------------------
// 8. Blazor Server
// ---------------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---------------------------------------------------------------------------
// 9. Authentication — OIDC scaffold per ADR-009
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(options =>
{
    options.Authority    = Environment.GetEnvironmentVariable("AUTH_AUTHORITY");
    options.ClientId     = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID");
    options.ClientSecret = Environment.GetEnvironmentVariable("AUTH_CLIENT_SECRET");
    options.ResponseType = "code";
    options.SaveTokens   = true;

    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("roles");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        RoleClaimType = "roles",
        ClockSkew     = TimeSpan.FromMinutes(2)
    };

    options.MapInboundClaims    = false;
    options.RequireHttpsMetadata = false;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});
builder.Services.AddCascadingAuthenticationState();

// ---------------------------------------------------------------------------
// 10. Build
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// 10b. Initialise lookup cache — must run before any request is served
// ---------------------------------------------------------------------------
await app.Services.GetRequiredService<ILookupCache>().RefreshAsync();

// ---------------------------------------------------------------------------
// 11. Middleware pipeline
// ---------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantConnectionMiddleware>();  // ADR-010 — after auth so claims are available
app.UseAntiforgery();

// ---------------------------------------------------------------------------
// 12. Minimal API endpoints — placeholder; module endpoints registered here
// ---------------------------------------------------------------------------
// HrisEndpoints.Map(app);   // added in Phase 2

app.MapGet("/account/login", async (HttpContext ctx, string? returnUrl) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties
        {
            RedirectUri = returnUrl ?? "/"
        });
});

app.MapGet("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
});

// ---------------------------------------------------------------------------
// 13. Blazor — .NET 9 style
// ---------------------------------------------------------------------------
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Maps DateOnly ↔ PostgreSQL date. Npgsql returns date columns as DateTime when using
// NpgsqlConnection directly (not NpgsqlDataSource), so Parse handles both types.
file sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
        => parameter.Value = value.ToDateTime(TimeOnly.MinValue);

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d  => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _           => DateOnly.FromDateTime(Convert.ToDateTime(value))
    };
}

file sealed class NullableDateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        => parameter.Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    public override DateOnly? Parse(object value)
    {
        if (value is DBNull || value is null) return null;
        return value switch
        {
            DateOnly d  => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _           => DateOnly.FromDateTime(Convert.ToDateTime(value))
        };
    }
}

