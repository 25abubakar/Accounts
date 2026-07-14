using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

const string connStr = "Server=(localdb)\\MSSQLLocalDB;Database=Accounts;Trusted_Connection=True;TrustServerCertificate=True;";

// ── Hash the password using ASP.NET Identity V3 hasher ───────────────────
var hasher   = new PasswordHasher<object>();
var bootstrapPassword = Environment.GetEnvironmentVariable("ACCOUNTS_BOOTSTRAP_ADMIN_PASSWORD")
    ?? throw new InvalidOperationException("Set ACCOUNTS_BOOTSTRAP_ADMIN_PASSWORD before running SeedAdmin.");
var hash     = hasher.HashPassword(new object(), bootstrapPassword);

var userId   = Guid.NewGuid().ToString();
var roleId   = Guid.NewGuid().ToString();
var stamp    = Guid.NewGuid().ToString();

Console.WriteLine("Connecting to database...");

await using var conn = new SqlConnection(connStr);
await conn.OpenAsync();

// ── 1. Check if admin already exists ─────────────────────────────────────
var checkCmd = new SqlCommand(
    "SELECT COUNT(*) FROM AspNetUsers WHERE UserName = 'admin'", conn);
var count = (int)await checkCmd.ExecuteScalarAsync()!;

if (count > 0)
{
    Console.WriteLine("ℹ️  Admin user already exists. Nothing to do.");
    return;
}

// ── 2. Create SuperAdmin role if not exists ───────────────────────────────
var roleCheck = new SqlCommand(
    "SELECT COUNT(*) FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN'", conn);
var roleExists = (int)await roleCheck.ExecuteScalarAsync()! > 0;

if (!roleExists)
{
    var createRole = new SqlCommand(@"
        INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
        VALUES (@id, 'SuperAdmin', 'SUPERADMIN', @stamp)", conn);
    createRole.Parameters.AddWithValue("@id",    roleId);
    createRole.Parameters.AddWithValue("@stamp", Guid.NewGuid().ToString());
    await createRole.ExecuteNonQueryAsync();
    Console.WriteLine("✅ SuperAdmin role created.");
}
else
{
    // Get existing role id
    var getRoleId = new SqlCommand(
        "SELECT Id FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN'", conn);
    roleId = (string)await getRoleId.ExecuteScalarAsync()!;
    Console.WriteLine("ℹ️  SuperAdmin role already exists.");
}

// ── 3. Insert admin user ──────────────────────────────────────────────────
var insertUser = new SqlCommand(@"
    INSERT INTO AspNetUsers (
        Id, UserName, NormalizedUserName, Email, NormalizedEmail,
        EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
        PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES (
        @id, 'admin', 'ADMIN', 'admin@laltechnologies.com', 'ADMIN@LALTECHNOLOGIES.COM',
        1, @hash, @stamp, @cstamp,
        0, 0, 1, 0)", conn);

insertUser.Parameters.AddWithValue("@id",     userId);
insertUser.Parameters.AddWithValue("@hash",   hash);
insertUser.Parameters.AddWithValue("@stamp",  stamp);
insertUser.Parameters.AddWithValue("@cstamp", Guid.NewGuid().ToString());
await insertUser.ExecuteNonQueryAsync();

// ── 4. Assign SuperAdmin role to admin user ───────────────────────────────
var assignRole = new SqlCommand(@"
    INSERT INTO AspNetUserRoles (UserId, RoleId)
    VALUES (@userId, @roleId)", conn);
assignRole.Parameters.AddWithValue("@userId", userId);
assignRole.Parameters.AddWithValue("@roleId", roleId);
await assignRole.ExecuteNonQueryAsync();

Console.WriteLine("");
Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   ✅  Admin Created Successfully          ║");
Console.WriteLine("╠══════════════════════════════════════════╣");
Console.WriteLine("║  Username : admin                        ║");
Console.WriteLine("║  Password : value supplied via environment variable ║");
Console.WriteLine("║  Email    : admin@laltechnologies.com    ║");
Console.WriteLine("║  Role     : SuperAdmin                   ║");
Console.WriteLine("╠══════════════════════════════════════════╣");
Console.WriteLine("║  ⚠️  Change password after first login!  ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
