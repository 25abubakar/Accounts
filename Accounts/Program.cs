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
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Accounts.Hubs;
using Accounts.Idempotency;

var builder = WebApplication.CreateBuilder(args);

// The default Windows Event Log provider requires machine-level permissions on
// some developer/deployment accounts. A logging warning must never prevent the
// API from starting (which otherwise appears to the frontend as "Network Error").
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
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
var allowedFrontendOrigins =
    builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>()
    ??
    [
        "http://localhost:5173",
        "https://localhost:5173",
        "http://localhost:3000",
        "https://localhost:3000",
        "http://localhost:8080",
        "http://127.0.0.1:8080",
    ];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
        policy.WithOrigins(allowedFrontendOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ── 6. Controllers + JSON ────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = ".Accounts.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddRazorPages();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 64 * 1024;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
});
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/problem+json"]);
});

// ── 7. Dependency Injection ──────────────────────────────────────────────────
builder.Services.AddMemoryCache(options => { options.SizeLimit = 10_000; });
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClaimsTransformation,
    Accounts.Authorization.OrganizationCeoClaimsTransformation>();

// ── Multi-Tenant: ITenantService reads TenantId from HttpContext.User claims ─
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IAccountScopeAccessService, AccountScopeAccessService>();

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
builder.Services.AddScoped<TenantPermissionService>();
builder.Services.AddScoped<OptimizedMenuService>();
builder.Services.AddHostedService<ProcessReportAutoTransferService>();
builder.Services.AddSingleton<AssessmentSchedulerService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<AssessmentSchedulerService>());
builder.Services.AddScoped<IOrganizationDataScopeService, OrganizationDataScopeService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IChatRuleService, ChatRuleService>();
builder.Services.AddHostedService<ChatViewOnceCleanupService>();
builder.Services.AddSingleton<ChatPresenceTracker>();
builder.Services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
builder.Services.AddOptions<IdempotencyOptions>()
    .Bind(builder.Configuration.GetSection(IdempotencyOptions.SectionName))
    .Validate(options => options.DefaultTtlHours > 0, "DefaultTtlHours must be positive.")
    .Validate(options => options.ProcessingLeaseSeconds > 0, "ProcessingLeaseSeconds must be positive.")
    .Validate(options => options.MaxRequestBodyBytes > 0, "MaxRequestBodyBytes must be positive.")
    .Validate(options => options.MaxResponseBodyBytes > 0, "MaxResponseBodyBytes must be positive.")
    .Validate(options => options.CleanupIntervalMinutes > 0, "CleanupIntervalMinutes must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
builder.Services.AddScoped<IdempotencyMiddleware>();
builder.Services.AddHostedService<IdempotencyCleanupService>();

// ── Communication Center ──────────────────────────────────────────────────────
builder.Services.AddScoped<IAppNoteService, AppNoteService>();
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IPersonAccessService, PersonAccessService>();

// ── Normalized domain services ────────────────────────────────────────────────
builder.Services.AddScoped<StaffMenuAccessService>();
builder.Services.AddScoped<DesignationService>();
builder.Services.AddScoped<PlatformSettingsProvisioningService>();
builder.Services.AddScoped<IAttendanceStatusRepository, AttendanceStatusRepository>();
builder.Services.AddScoped<IAttendanceStatusService, AttendanceStatusService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<AttendanceFinalizationService>();
builder.Services.AddScoped<PayrollCalculationService>();
builder.Services.AddScoped<StaffMonthlyEobiService>();
builder.Services.AddHostedService<AttendanceFinalizationScheduler>();
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
using (var scope = app.Services.CreateScope())
{
    // Apply pending schema changes before seeding or serving requests. This keeps
    // deployed/running databases aligned with the model (for example the
    // OrganizationTree.IsActive hierarchy status column).
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

// Vite uses the HTTP development endpoint (localhost:5099). Redirecting its
// CORS preflight OPTIONS request to HTTPS makes browsers reject login before
// the POST is sent. Enforce HTTPS outside local development only.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();

app.UseCors("AllowReactApp");

// Client disconnects/navigation aborts are expected and must not surface as
// unhandled EF Core errors. Keep this before authentication and controllers.
app.UseMiddleware<RequestCancellationMiddleware>();

app.UseAuthentication();
app.UseMiddleware<AccountScopeAccessMiddleware>();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapGet("/api/security/csrf-token", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
}).AllowAnonymous();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<ApplicationRealtimeHub>("/hubs/application");
app.MapRazorPages();

app.Run();
