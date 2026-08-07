using Accounts.Data;
using Accounts.Models;
using Accounts.Services;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Middleware;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Accounts.Repositories;
using Accounts.Repositories.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// The Windows Event Log provider can throw AccessDenied for ordinary web-app
// identities. That secondary logging failure was masking the real API response
// (including login/CSRF failures) and surfacing as an unhandled exception.
// Console + Debug logging are safe in IIS, Kestrel, tests, and containers.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Antiforgery and Identity cookies both depend on Data Protection. The default
// Windows profile key folder is not reliable under IIS/service identities and
// can make the CSRF endpoint return 500, which blocks every login. Keep local
// development keys with the application and allow deployments to provide a
// durable shared directory through DataProtection:KeyPath.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Accounts");
if (builder.Environment.IsEnvironment("Testing"))
{
    dataProtection.UseEphemeralDataProtectionProvider();
}
else
{
    var configuredKeyPath = builder.Configuration["DataProtection:KeyPath"];
    if (!string.IsNullOrWhiteSpace(configuredKeyPath) || builder.Environment.IsDevelopment())
    {
        var keyPath = !string.IsNullOrWhiteSpace(configuredKeyPath)
            ? Path.GetFullPath(configuredKeyPath, builder.Environment.ContentRootPath)
            : Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys");
        Directory.CreateDirectory(keyPath);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    }
}

// ── 1. Database Configuration ────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.MaxBatchSize(200);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        });

    // Detailed EF diagnostics are useful locally but add avoidable work and may
    // expose query details in a production process.
    if (builder.Environment.IsDevelopment())
        options.EnableDetailedErrors();
});

// ── 2. Identity Configuration (ApplicationUser, not IdentityUser) ────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredUniqueChars = 6;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ── 3. Cookie Configuration ──────────────────────────────────────────────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = builder.Environment.IsDevelopment()
        ? SameSiteMode.Lax
        : SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.HttpOnly     = true;
    options.Cookie.Name         = ".AspNetCore.Identity.Application";
    options.ExpireTimeSpan      = TimeSpan.FromHours(8);
    options.SlidingExpiration   = true;

    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// ── 4. HttpClients ───────────────────────────────────────────────────────────
builder.Services.AddHttpClient("CountryApi", client =>
    client.BaseAddress = new Uri("https://restcountries.com/v3.1/"));
builder.Services.AddHttpClient("CountriesNow", client =>
    client.BaseAddress = new Uri("https://countriesnow.space/api/v0.1/"));

// ── 5. CORS ──────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
        policy.WithOrigins(
                  "http://localhost:5173",
                  "https://localhost:5173",
                  "http://127.0.0.1:5173",
                  "https://127.0.0.1:5173",
                  "http://localhost:3000",
                  "https://localhost:3000",
                  "http://127.0.0.1:3000",
                  "https://127.0.0.1:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ── 6. Controllers + JSON ────────────────────────────────────────────────────
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsDevelopment()
        ? "Accounts.Antiforgery"
        : "__Host-Accounts.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = builder.Environment.IsDevelopment()
        ? SameSiteMode.Lax
        : SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddControllers(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddRazorPages();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/problem+json"]);
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("scheduler", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown-scheduler",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        return ValueTask.CompletedTask;
    };
});

// ── 7. Dependency Injection ──────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClaimsTransformation,
    Accounts.Authorization.OrganizationCeoClaimsTransformation>();

// ── Multi-Tenant: ITenantService reads TenantId from HttpContext.User claims ─
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IAccountScopeAccessService, AccountScopeAccessService>();
builder.Services.AddScoped<ITenantMenuCeilingService, TenantMenuCeilingService>();
builder.Services.AddScoped<IOrganizationScopeService, OrganizationScopeService>();
builder.Services.AddScoped<IOrganizationDataScopeService, OrganizationDataScopeService>();

// ── Core domain services ──────────────────────────────────────────────────────
builder.Services.AddScoped<VacancyCodeService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IOrganizationEmployeeQueryService, OrganizationEmployeeQueryService>();
builder.Services.AddScoped<IVacancyService, VacancyService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IAccessService, AccessService>();
builder.Services.AddScoped<IPermissionFilterService, PermissionFilterService>();
builder.Services.AddScoped<RbacService>();
builder.Services.AddScoped<OptimizedMenuService>();
builder.Services.AddHostedService<ProcessReportAutoTransferService>();
builder.Services.AddSingleton<AssessmentSchedulerService>();
if (builder.Configuration.GetValue("Assessment:InternalSchedulerEnabled", false))
    builder.Services.AddHostedService(provider => provider.GetRequiredService<AssessmentSchedulerService>());

// ── Communication Center ──────────────────────────────────────────────────────
builder.Services.AddScoped<IAppNoteService, AppNoteService>();
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IPersonAccessService, PersonAccessService>();

// ── Normalized domain services ────────────────────────────────────────────────
builder.Services.AddScoped<StaffMenuAccessService>();
builder.Services.AddScoped<JobTitleService>();
builder.Services.AddScoped<IAttendanceStatusRepository, AttendanceStatusRepository>();
builder.Services.AddScoped<IAttendanceStatusService, AttendanceStatusService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddAutoMapper(_ => { }, typeof(Program).Assembly);

// ── Dynamic Permission-Based Authorization ────────────────────────────────────
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
    Accounts.Authorization.PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Accounts.Authorization.PermissionAuthorizationHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── 8. Seed Roles + Super Admin ──────────────────────────────────────────────
if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", builder.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

// ── 9. Middleware Pipeline ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

// The React development client intentionally talks to http://localhost:5099.
// Redirecting its cross-origin CSRF/login requests to the HTTPS port breaks the
// browser preflight and cookie/token pairing. Production remains HTTPS-only.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
    await next();
});
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowReactApp");
// CORS must run before rate limiting so 429 responses remain readable by the
// browser instead of being misreported as a CORS failure.
app.UseRateLimiter();

// Client disconnects/navigation aborts are expected and must not surface as
// unhandled EF Core errors. Keep this before authentication and controllers.
app.UseMiddleware<RequestCancellationMiddleware>();

app.UseAuthentication();
app.UseMiddleware<SecurityAuditMiddleware>();
app.UseMiddleware<AccountScopeAccessMiddleware>();
app.UseMiddleware<OperationalAccessBoundaryMiddleware>();
app.UseAntiforgery();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();

public partial class Program;
