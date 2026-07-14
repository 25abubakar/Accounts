using Accounts.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Data
{
    /// <summary>
    /// Runs once on application startup to ensure the database has all required
    /// roles and at least one Super Admin account.
    ///
    /// Safe to run repeatedly — every step is idempotent (check-before-create).
    /// </summary>
    public static class DbSeeder
    {
        // ── Well-known roles ─────────────────────────────────────────────────
        private static readonly string[] Roles =
        {
            "SuperAdmin",
            "TenantAdmin",
            "Admin",
            "Manager",
            "Developer",
            "AssistantManager",
        };

        // ── Super Admin accounts to seed ─────────────────────────────────────
        // Add entries here if you need additional system-level admins.
        private static readonly SuperAdminSeed[] SuperAdmins = Array.Empty<SuperAdminSeed>();

        /// <summary>
        /// Entry point — call from Program.cs inside a scoped DI scope.
        ///
        /// Usage:
        ///   using var scope = app.Services.CreateScope();
        ///   await DbSeeder.SeedAsync(scope.ServiceProvider);
        /// </summary>
        public static async Task SeedAsync(IServiceProvider services)
        {
            var roleManager    = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager    = services.GetRequiredService<UserManager<ApplicationUser>>();
            var db             = services.GetRequiredService<ApplicationDbContext>();
            var loggerFactory  = services.GetRequiredService<ILoggerFactory>();
            var logger         = loggerFactory.CreateLogger("DbSeeder");

            await SeedRolesAsync(roleManager, logger);
            await SeedSuperAdminsAsync(userManager, logger);
            await BackfillUserTenantIdsAsync(db, logger);
        }

        // ── Roles ─────────────────────────────────────────────────────────────

        private static async Task SeedRolesAsync(
            RoleManager<IdentityRole> roleManager,
            ILogger logger)
        {
            foreach (var role in Roles)
            {
                if (await roleManager.RoleExistsAsync(role)) continue;

                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (result.Succeeded)
                    logger.LogInformation("[DbSeeder] Role created: {Role}", role);
                else
                    logger.LogWarning("[DbSeeder] Failed to create role {Role}: {Errors}",
                        role, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        // ── Super Admin accounts ──────────────────────────────────────────────

        private static async Task SeedSuperAdminsAsync(
            UserManager<ApplicationUser> userManager,
            ILogger logger)
        {
            foreach (var seed in SuperAdmins)
            {
                // Check by username first, then fall back to email
                var user = await userManager.FindByNameAsync(seed.UserName)
                        ?? await userManager.FindByEmailAsync(seed.Email);

                if (user == null)
                {
                    // ── Create brand-new Super Admin ───────────────────────────
                    user = new ApplicationUser
                    {
                        UserName       = seed.UserName,
                        Email          = seed.Email,
                        EmailConfirmed = true,
                        IsSuperAdmin   = true,
                        IsTenantAdmin  = false,
                        TenantId       = null   // Super Admin owns no tenant
                    };

                    var createResult = await userManager.CreateAsync(user, seed.Password);
                    if (!createResult.Succeeded)
                    {
                        logger.LogError(
                            "[DbSeeder] Failed to create Super Admin '{UserName}': {Errors}",
                            seed.UserName,
                            string.Join(", ", createResult.Errors.Select(e => e.Description)));
                        continue;
                    }

                    logger.LogInformation(
                        "[DbSeeder] Super Admin created: {UserName} ({Email})",
                        seed.UserName, seed.Email);
                }
                else
                {
                    // ── Backfill flags on pre-existing account ─────────────────
                    bool dirty = false;
                    if (!user.IsSuperAdmin)  { user.IsSuperAdmin  = true;  dirty = true; }
                    if (user.IsTenantAdmin)  { user.IsTenantAdmin = false; dirty = true; }
                    if (user.TenantId != null) { user.TenantId   = null;  dirty = true; }

                    if (dirty)
                    {
                        await userManager.UpdateAsync(user);
                        logger.LogInformation(
                            "[DbSeeder] Super Admin flags backfilled for existing user: {UserName}",
                            seed.UserName);
                    }
                }

                // ── Ensure SuperAdmin role is assigned ─────────────────────────
                if (!await userManager.IsInRoleAsync(user, "SuperAdmin"))
                {
                    await userManager.AddToRoleAsync(user, "SuperAdmin");
                    logger.LogInformation(
                        "[DbSeeder] SuperAdmin role assigned to: {UserName}", seed.UserName);
                }
            }
        }

        // ── Seed descriptor ───────────────────────────────────────────────────
        private sealed record SuperAdminSeed(string UserName, string Email, string Password);

        // ── Backfill TenantId on staff users ──────────────────────────────────

        /// <summary>
        /// Backfills AspNetUsers.TenantId for regular staff whose Person record
        /// has a TenantId but the Identity user account does not.
        ///
        /// This fixes staff registered before the multi-tenant migration.
        /// Safe to call on every startup — idempotent.
        /// </summary>
        private static async Task BackfillUserTenantIdsAsync(
            ApplicationDbContext db,
            ILogger logger)
        {
            // Find staff users (not SuperAdmin, not TenantAdmin) with null TenantId
            // but whose linked Person record has a TenantId
            var usersToFix = await db.Users
                .Join(db.Persons,
                    u => u.Id,
                    p => p.IdentityUserId,
                    (u, p) => new { User = u, PersonTenantId = p.TenantId })
                .Where(x => x.User.TenantId == null
                         && !x.User.IsSuperAdmin
                         && !x.User.IsTenantAdmin)
                .ToListAsync();

            if (!usersToFix.Any()) return;

            int count = 0;
            foreach (var item in usersToFix)
            {
                item.User.TenantId = item.PersonTenantId;
                count++;
            }

            await db.SaveChangesAsync();
            logger.LogInformation("[DbSeeder] Backfilled TenantId on {Count} staff user(s).", count);
        }
    }
}
