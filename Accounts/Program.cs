using Accounts.Data;
using Accounts.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity with Roles support
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;

    // 🌟 FIXED: These three lines allow auto-generated passwords like "AFG10001@"
    options.Password.RequireLowercase = false;      // Doesn't need a-z
    options.Password.RequireUppercase = true;       // Needs A-Z (for AFG)
    options.Password.RequireNonAlphanumeric = true; // Needs symbol (for @)
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

// HttpClient for country lookup (restcountries.com)
builder.Services.AddHttpClient("CountryApi", client =>
{
    client.BaseAddress = new Uri("https://restcountries.com/v3.1/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// HttpClient for CountriesNow (provinces + cities — no auth required)
builder.Services.AddHttpClient("CountriesNow", client =>
{
    client.BaseAddress = new Uri("https://countriesnow.space/api/v0.1/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// CORS — allow React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// ── Services (Clean Architecture) ────────────────────────────────────────
builder.Services.AddScoped<VacancyCodeService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IAuthService, Accounts.Services.Services.AuthService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IOrganizationService, Accounts.Services.Services.OrganizationService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IVacancyService, Accounts.Services.Services.VacancyService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IStaffService, Accounts.Services.Services.StaffService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IPersonService, Accounts.Services.Services.PersonService>();
builder.Services.AddScoped<Accounts.Services.Interfaces.IMenuService, Accounts.Services.Services.MenuService>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Accounts API", Version = "v1" });
});

var app = builder.Build();

// Seed predefined roles on startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = ["Manager", "Developer", "AssistantManager"];
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Accounts API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();