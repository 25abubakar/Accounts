using Accounts.Data;
using Accounts.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Database Configuration ────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        })
    .EnableDetailedErrors()          // shows full column/value info in exceptions
    .EnableSensitiveDataLogging());  // shows parameter values in logs

// ── 2. Identity Configuration ────────────────────────────────────────────────
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

// 🔥 FIX 1: Configure Application Cookies for Cross-Origin Cookie Sharing
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite    = SameSiteMode.None;
    // SameAsRequest = works on both HTTP and HTTPS (not Always which requires HTTPS)
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.HttpOnly    = true;
    options.Cookie.Name        = ".AspNetCore.Identity.Application";

    // Return 401 instead of redirecting to login page (API behaviour)
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

// ── 3. HttpClients ───────────────────────────────────────────────────────────
builder.Services.AddHttpClient("CountryApi", client => {
    client.BaseAddress = new Uri("https://restcountries.com/v3.1/");
});
builder.Services.AddHttpClient("CountriesNow", client => {
    client.BaseAddress = new Uri("https://countriesnow.space/api/v0.1/");
});

// 🔥 FIX 2: Explicit CORS policy targeting your React frontend (No Wildcards)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
        policy.WithOrigins(
                  "http://localhost:5173",
                  "https://localhost:5173",
                  "http://localhost:3000",
                  "https://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// ── 4. Dependency Injection Registrations ────────────────────────────────────
builder.Services.AddScoped<VacancyCodeService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IAuthService, Accounts.Services.Services.AuthService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IOrganizationService, Accounts.Services.Services.OrganizationService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IVacancyService, Accounts.Services.Services.VacancyService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IStaffService, Accounts.Services.Services.StaffService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IPersonService, Accounts.Services.Services.PersonService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IMenuService, Accounts.Services.Services.MenuService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IAccessService, Accounts.Services.Services.AccessService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IPermissionFilterService, Accounts.Services.Services.PermissionFilterService>();
builder.Services.AddScoped<Accounts.Services.Services.RbacService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── 5. Seed Logic ────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    string[] roles = { "SuperAdmin", "Manager", "Developer", "AssistantManager" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    const string adminUsername = "admin";
    var existingAdmin = await userManager.FindByNameAsync(adminUsername);
    if (existingAdmin == null)
    {
        var adminUser = new IdentityUser { UserName = adminUsername, Email = "admin@laltechnologies.com", EmailConfirmed = true };
        var result = await userManager.CreateAsync(adminUser, "Admin@123");
        if (result.Succeeded) await userManager.AddToRoleAsync(adminUser, "SuperAdmin");
    }
}

// ── 6. Middleware Pipeline ───────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// CORS policy MUST be executed after UseRouting but before Authorization engines
app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();