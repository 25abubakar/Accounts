using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly ITenantService? _tenantService;

        // Constructor used by the DI container (with tenant service)
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantService? tenantService = null)
            : base(options)
        {
            _tenantService = tenantService;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }

        public DbSet<OrganizationTree>         OrganizationTree         => Set<OrganizationTree>();
        public DbSet<Vacancy>                  Vacancies                => Set<Vacancy>();
        public DbSet<StaffVacancy>            StaffVacancies            => Set<StaffVacancy>();
        public DbSet<Person>                   Persons                  => Set<Person>();
        public DbSet<PersonAddress>            PersonAddresses          => Set<PersonAddress>();
        public DbSet<PersonContact>            PersonContacts           => Set<PersonContact>();
        public DbSet<JobTitle>                 JobTitles                => Set<JobTitle>();
        public DbSet<VacancyCounter>           VacancyCounters          => Set<VacancyCounter>();
        public DbSet<Menu>                     Menus                    => Set<Menu>();
        public DbSet<MenuPermission>           MenuPermissions          => Set<MenuPermission>();
        public DbSet<Feature>                  Features                 => Set<Feature>();
        public DbSet<StaffAccessGroup>         StaffAccessGroups        => Set<StaffAccessGroup>();
        public DbSet<DepartmentAccessMatrix>   DepartmentAccessMatrix   => Set<DepartmentAccessMatrix>();
        // ── Hierarchical RBAC (legacy — kept during migration) ───────────────
        public DbSet<RolePermission>           RolePermissions          => Set<RolePermission>();
        // NOTE: UserPermissionOverrides table was dropped in V2 migration.
        //       All permission writes now go through StaffMenuAccess + AccessFeatures.
        // ── New 2-Tier RBAC ───────────────────────────────────────────────────
        public DbSet<StaffMenuAccess>          StaffMenuAccesses        => Set<StaffMenuAccess>();
        public DbSet<AccessFeature>            AccessFeatures           => Set<AccessFeature>();

        // ── Multi-Tenant SaaS ─────────────────────────────────────────────────
        public DbSet<Tenant>                   Tenants                  => Set<Tenant>();
        public DbSet<TenantMenuPermission>     TenantMenuPermissions    => Set<TenantMenuPermission>();
        public DbSet<TenantRolePermission>     TenantRolePermissions    => Set<TenantRolePermission>();

        // ── Communication Center ──────────────────────────────────────────────
        public DbSet<AppLookupType>     AppLookupTypes     => Set<AppLookupType>();
        public DbSet<AppLookupValue>    AppLookupValues    => Set<AppLookupValue>();
        // NOTE: AppMenuDefinitions table dropped in V2 migration. Use Menus table instead.
        public DbSet<AppNote>           AppNotes            => Set<AppNote>();
        public DbSet<AppNoteTarget>     AppNoteTargets      => Set<AppNoteTarget>();
        public DbSet<AppNoteUserStatus> AppNoteUserStatuses => Set<AppNoteUserStatus>();
        public DbSet<AppNoteUserState>  AppNoteUserStates   => Set<AppNoteUserState>();
        public DbSet<AppNoteAttachment> AppNoteAttachments  => Set<AppNoteAttachment>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── Global Query Filters — Multi-Tenant Data Isolation ────────────
            //
            // IMPORTANT: Must use lazy evaluation (lambda calling _tenantService each time)
            // NOT captured values — OnModelCreating runs once but filters run per query.
            //
            // Rules:
            //   - _tenantService == null  → tooling/migrations context, bypass all filters
            //   - IsSuperAdmin == true    → bypass (Super Admin never queries operational tables)
            //   - TenantId == null        → bypass (unauthenticated or Super Admin)
            //   - Otherwise              → scope to the current request's TenantId

            builder.Entity<Person>()
                .HasQueryFilter(p =>
                    _tenantService == null ||
                    _tenantService.IsSuperAdmin ||
                    _tenantService.TenantId == null ||
                    p.TenantId == _tenantService.TenantId);

            builder.Entity<Vacancy>()
                .HasQueryFilter(v =>
                    _tenantService == null ||
                    _tenantService.IsSuperAdmin ||
                    _tenantService.TenantId == null ||
                    v.TenantId == _tenantService.TenantId);

            builder.Entity<StaffVacancy>()
                .HasQueryFilter(s =>
                    _tenantService == null ||
                    _tenantService.IsSuperAdmin ||
                    _tenantService.TenantId == null ||
                    s.TenantId == _tenantService.TenantId);

            builder.Entity<JobTitle>()
                .HasQueryFilter(j =>
                    _tenantService == null ||
                    _tenantService.IsSuperAdmin ||
                    _tenantService.TenantId == null ||
                    j.TenantId == _tenantService.TenantId);

            builder.Entity<AppNote>()
                .HasQueryFilter(n =>
                    _tenantService == null ||
                    _tenantService.IsSuperAdmin ||
                    _tenantService.TenantId == null ||
                    n.TenantId == null ||
                    n.TenantId == _tenantService.TenantId);

            // ── ApplicationUser (AspNetUsers) — multi-tenant columns ──────────
            builder.Entity<ApplicationUser>(e =>
            {
                e.Property(u => u.TenantId).IsRequired(false);
                e.Property(u => u.IsSuperAdmin).HasDefaultValue(false);
                e.Property(u => u.IsTenantAdmin).HasDefaultValue(false);
                e.HasIndex(u => u.TenantId);
            });

            // ── Tenants table ─────────────────────────────────────────────────
            builder.Entity<Tenant>(e =>
            {
                e.ToTable("Tenants");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.TenantName).HasMaxLength(150).IsRequired();
                e.Property(x => x.TenantCode).HasMaxLength(20).IsRequired();
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired(false);

                // One org node = one tenant
                e.HasIndex(x => x.OrganizationTreeId).IsUnique();
                e.HasIndex(x => x.TenantCode).IsUnique();

                e.HasOne(x => x.OrganizationNode)
                 .WithMany()
                 .HasForeignKey(x => x.OrganizationTreeId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── TenantMenuPermissions ─────────────────────────────────────────
            builder.Entity<TenantMenuPermission>(e =>
            {
                e.ToTable("TenantMenuPermissions");
                e.HasKey(x => new { x.TenantId, x.MenuId });
                e.Property(x => x.IsAllow).HasDefaultValue(true);
                e.Property(x => x.GrantedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.Property(x => x.GrantedByUserId).HasMaxLength(450).IsRequired(false);

                e.HasOne(x => x.Tenant)
                 .WithMany(t => t.MenuPermissions)
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Menu)
                 .WithMany()
                 .HasForeignKey(x => x.MenuId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.TenantId);
                e.HasIndex(x => x.MenuId);
            });

            // ── TenantRolePermissions ─────────────────────────────────────────
            builder.Entity<TenantRolePermission>(e =>
            {
                e.ToTable("TenantRolePermissions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.JobTitle).HasMaxLength(100).IsRequired();
                e.Property(x => x.IsAllowed).HasDefaultValue(false);
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.Property(x => x.SetByUserId).HasMaxLength(450).IsRequired(false);

                // Unique: one row per (TenantId + JobTitle + DeptId + PermissionId)
                e.HasIndex(x => new { x.TenantId, x.JobTitle, x.DeptId, x.PermissionId }).IsUnique();
                e.HasIndex(x => x.TenantId);
                e.HasIndex(x => new { x.TenantId, x.JobTitle });

                e.HasOne(x => x.Tenant)
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Feature)
                 .WithMany()
                 .HasForeignKey(x => x.PermissionId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Department)
                 .WithMany()
                 .HasForeignKey(x => x.DeptId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired(false);
            });

            // ── Person: TenantId FK ───────────────────────────────────────────
            builder.Entity<Person>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── Vacancy: TenantId FK ──────────────────────────────────────────
            builder.Entity<Vacancy>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── StaffVacancy: TenantId FK ─────────────────────────────────────
            builder.Entity<StaffVacancy>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── JobTitle: TenantId FK ─────────────────────────────────────────
            builder.Entity<JobTitle>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
                // UNIQUE constraint now scoped per tenant
                // (old global unique on TitleName is replaced by tenant-scoped unique)
                e.HasIndex(x => new { x.TenantId, x.TitleName }).IsUnique();
            });

            // ── AppNote: optional TenantId FK ────────────────────────────────
            builder.Entity<AppNote>(e =>
            {
                e.Property(x => x.TenantId).IsRequired(false);
                e.HasIndex(x => x.TenantId);
                e.HasOne(x => x.Tenant)
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired(false);
            });

            // ── Menu and MenuPermissions ──────────────────────────────────────
            builder.Entity<Menu>()
                .HasOne(m => m.Parent)
                .WithMany(m => m.Children)
                .HasForeignKey(m => m.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<MenuPermission>(e =>
            {
                e.ToTable("MenuPermissions");
                e.HasKey(x => new { x.MenuId, x.PermissionId });

                e.HasOne(x => x.Menu)
                 .WithMany(m => m.MenuPermissions)
                 .HasForeignKey(x => x.MenuId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Feature)
                 .WithMany()
                 .HasForeignKey(x => x.PermissionId)
                 .OnDelete(DeleteBehavior.Cascade);

                // Optimized indexes
                e.HasIndex(x => x.MenuId);
                e.HasIndex(x => x.PermissionId);
            });

            builder.Entity<OrganizationTree>(e =>
            {
                e.ToTable("OrganizationTree", "dbo");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.FlagUrl).HasMaxLength(500).IsRequired(false);
                e.HasOne(x => x.Parent)
                 .WithMany(x => x.Children)
                 .HasForeignKey(x => x.ParentId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Vacancy>(e =>
            {
                e.ToTable("Vacancies");
                e.HasKey(x => x.VacancyId);
                e.Property(x => x.VacancyId).HasDefaultValueSql("NEWID()").ValueGeneratedNever();
                e.Property(x => x.IsFilled).HasDefaultValue(false);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("GETDATE()");

                e.HasOne(x => x.Organization)
                 .WithMany()
                 .HasForeignKey(x => x.OrganizationId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Staff)
                 .WithOne(x => x.Vacancy)
                 .HasForeignKey<StaffVacancy>(x => x.VacancyId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<StaffVacancy>(e =>
            {
                e.ToTable("StaffVacancy");
                e.HasKey(x => x.StaffId);
                e.Property(x => x.StaffId).HasDefaultValueSql("NEWID()").ValueGeneratedNever();

                e.HasIndex(x => x.VacancyId).IsUnique();
                e.HasIndex(x => x.PersonId).IsUnique();
                e.HasIndex(x => x.LoginId).IsUnique();

                e.HasOne(x => x.Person)
                 .WithOne(x => x.Staff)
                 .HasForeignKey<StaffVacancy>(x => x.PersonId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<Person>(e =>
            {
                e.ToTable("Persons");
                e.HasKey(x => x.PersonId);
                e.Property(x => x.PersonId).HasDefaultValueSql("NEWID()").ValueGeneratedNever();
                e.Property(x => x.CreatedDate).HasDefaultValueSql("GETDATE()");
                e.Property(x => x.IdentityUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.ProfilePhotoUrl).HasMaxLength(500).IsRequired(false);
                e.Property(x => x.PersonalEmail).HasMaxLength(256).IsRequired(false);

                e.HasIndex(x => x.IdentityUserId).IsUnique();
            });

            builder.Entity<PersonAddress>(e =>
            {
                e.ToTable("PersonAddresses");
                e.HasKey(x => x.AddressId);
                e.Property(x => x.AddressId).HasDefaultValueSql("NEWID()").ValueGeneratedNever();
                e.Property(x => x.AddressType).HasMaxLength(20).IsRequired();

                e.HasIndex(x => new { x.PersonId, x.AddressType }).IsUnique();

                e.HasOne(x => x.Person)
                 .WithMany(x => x.Addresses)
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<VacancyCounter>(e =>
            {
                e.ToTable("VacancyCounters");
                e.HasKey(x => x.Prefix);
                e.Property(x => x.Prefix).HasMaxLength(200).IsRequired();
                e.Property(x => x.LastNumber).HasDefaultValue(0).IsRequired();
            });

            // ── Features (Master Permissions) ─────────────────────────────────
            builder.Entity<Feature>(e =>
            {
                e.ToTable("Features");
                e.HasKey(x => x.PermissionId);
                e.Property(x => x.PermissionId).ValueGeneratedOnAdd();
                e.Property(x => x.FeatureKey).HasMaxLength(100).IsRequired();
                e.Property(x => x.FeatureName).HasMaxLength(150).IsRequired();
                e.Property(x => x.Module).HasMaxLength(100).IsRequired();
                e.Property(x => x.CreatedDate).HasDefaultValueSql("GETDATE()");

                // Unique index on FeatureKey for backward compatibility lookups
                e.HasIndex(x => x.FeatureKey).IsUnique();
            });

            builder.Entity<StaffAccessGroup>(e =>
            {
                e.ToTable("StaffAccessGroups");
                e.HasKey(x => new { x.StaffId, x.GroupId });
                e.Property(x => x.AssignedDate)
                 .HasColumnType("datetime")
                 .HasDefaultValueSql("GETDATE()");

                e.HasOne(x => x.Staff)
                 .WithMany()
                 .HasForeignKey(x => x.StaffId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── DepartmentAccessMatrix (Legacy) ───────────────────────────────
            builder.Entity<DepartmentAccessMatrix>(e =>
            {
                e.ToTable("DepartmentAccessMatrix");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.GrantedDate)
                 .HasColumnType("datetime")
                 .HasDefaultValueSql("GETDATE()");
                e.Property(x => x.HasAccess).HasDefaultValue(false);

                // Unique composite index: one entry per StaffId + PermissionId
                e.HasIndex(x => new { x.StaffId, x.PermissionId }).IsUnique();

                // Optimized covering indexes for common query patterns
                e.HasIndex(x => x.StaffId);
                e.HasIndex(x => x.DeptId);
                e.HasIndex(x => x.PermissionId);

                e.HasOne(x => x.Staff)
                 .WithMany()
                 .HasForeignKey(x => x.StaffId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Department)
                 .WithMany()
                 .HasForeignKey(x => x.DeptId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Feature)
                 .WithMany()
                 .HasForeignKey(x => x.PermissionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── RolePermission (Optimized) ────────────────────────────────────
            builder.Entity<RolePermission>(e =>
            {
                e.ToTable("RolePermissions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.JobTitle).HasMaxLength(100).IsRequired();
                e.Property(x => x.IsAllowed).HasDefaultValue(false);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("GETDATE()");

                // Unique composite: one row per JobTitle + DeptId + PermissionId
                e.HasIndex(x => new { x.JobTitle, x.DeptId, x.PermissionId }).IsUnique();

                // Optimized covering indexes for fast role permission lookups
                e.HasIndex(x => x.JobTitle);
                e.HasIndex(x => new { x.JobTitle, x.DeptId });
                e.HasIndex(x => x.PermissionId);

                e.HasOne(x => x.Department)
                 .WithMany()
                 .HasForeignKey(x => x.DeptId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired(false);

                e.HasOne(x => x.Feature)
                 .WithMany(x => x.RolePermissions)
                 .HasForeignKey(x => x.PermissionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // NOTE: UserPermissionOverrides table dropped in V2 migration.
            //       All permission writes now go through StaffMenuAccess + AccessFeatures.

            // ── Communication Center: AppLookupTypes ──────────────────────────
            builder.Entity<AppLookupType>(e =>
            {
                e.ToTable("AppLookupTypes");
                e.HasKey(x => x.LookupTypeId);
                e.Property(x => x.LookupTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.LookupTypeName).HasMaxLength(150).IsRequired();
                e.HasIndex(x => x.LookupTypeCode).IsUnique();
            });

            // ── Communication Center: AppLookupValues ─────────────────────────
            builder.Entity<AppLookupValue>(e =>
            {
                e.ToTable("AppLookupValues");
                e.HasKey(x => x.LookupValueId);
                e.Property(x => x.ValueCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.DisplayText).HasMaxLength(150).IsRequired();
                e.HasIndex(x => new { x.LookupTypeId, x.ValueCode }).IsUnique();
                e.HasOne(x => x.LookupType)
                 .WithMany(x => x.Values)
                 .HasForeignKey(x => x.LookupTypeId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Communication Center: AppNotes ────────────────────────────────
            builder.Entity<AppNote>(e =>
            {
                e.ToTable("AppNotes");
                e.HasKey(x => x.NoteId);
                e.Property(x => x.Title).HasMaxLength(250).IsRequired();
                e.Property(x => x.NoteTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.SourceTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.PriorityCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.VisibilityTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.OwnerIdentityUserId).HasMaxLength(450).IsRequired(false);
                e.HasIndex(x => x.OwnerIdentityUserId);
                e.HasOne<ApplicationUser>()
                 .WithMany()
                 .HasForeignKey(x => x.OwnerIdentityUserId)
                 .OnDelete(DeleteBehavior.SetNull);
                e.HasMany(x => x.Targets).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.UserStatuses).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.UserStates).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Attachments).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            });

            // ── Communication Center: AppNoteTargets ──────────────────────────
            builder.Entity<AppNoteTarget>(e =>
            {
                e.ToTable("AppNoteTargets");
                e.HasKey(x => x.NoteTargetId);
                e.Property(x => x.TargetTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.TargetValue).HasMaxLength(150).IsRequired();
            });

            // ── Communication Center: AppNoteUserStatuses (legacy) ────────────
            builder.Entity<AppNoteUserStatus>(e =>
            {
                e.ToTable("AppNoteUserStatuses");
                e.HasKey(x => x.NoteUserStatusId);
                e.Property(x => x.UserId).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.NoteId, x.UserId }).IsUnique();
            });

            // ── Communication Center: AppNoteUserStates (per-staff) ───────────
            builder.Entity<AppNoteUserState>(e =>
            {
                e.ToTable("AppNoteUserStates");
                e.HasKey(x => x.AppNoteUserStateId);
                e.Property(x => x.StaffId).HasMaxLength(100).IsRequired();
                // One row per (NoteId, StaffId)
                e.HasIndex(x => new { x.NoteId, x.StaffId }).IsUnique();
            });

            // ── Communication Center: AppNoteAttachments ──────────────────────
            builder.Entity<AppNoteAttachment>(e =>
            {
                e.ToTable("AppNoteAttachments");
                e.HasKey(x => x.AttachmentId);
            });

            // ── Keyless query types (stored procedures / views) ───────────────
            builder.Entity<OrganizationVacancyPersonDto>().HasNoKey();
            builder.Entity<EmployeeByOrgAndRoleDto>().HasNoKey();

            // ── JobTitles (normalized lookup — now tenant-scoped) ─────────────
            builder.Entity<JobTitle>(e =>
            {
                e.ToTable("JobTitles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.TitleName).HasMaxLength(100).IsRequired();
                // Unique index is now (TenantId, TitleName) — configured above in tenant section
            });

            // ── PersonContacts (one-to-many contacts per person) ──────────────
            builder.Entity<PersonContact>(e =>
            {
                e.ToTable("PersonContacts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.ContactType).HasMaxLength(20).IsRequired();
                e.Property(x => x.ContactValue).HasMaxLength(256).IsRequired();
                e.Property(x => x.IsPrimary).HasDefaultValue(false);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => x.PersonId);
                e.HasOne(x => x.Person)
                 .WithMany(x => x.Contacts)
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Vacancy: JobTitleId FK ─────────────────────────────────────────
            builder.Entity<Vacancy>(e =>
            {
                e.HasOne(x => x.JobTitleNav)
                 .WithMany(x => x.Vacancies)
                 .HasForeignKey(x => x.JobTitleId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired(false);
                e.HasIndex(x => x.JobTitleId);
            });

            // ── StaffMenuAccess (RBAC Tier-1) ─────────────────────────────────
            builder.Entity<StaffMenuAccess>(e =>
            {
                e.ToTable("StaffMenuAccess");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.IsAllow).HasDefaultValue(true);
                e.Property(x => x.GrantedDate).HasDefaultValueSql("SYSUTCDATETIME()");

                // One row per (staff + menu)
                e.HasIndex(x => new { x.StaffId, x.MenuId }).IsUnique();
                e.HasIndex(x => x.StaffId);
                e.HasIndex(x => x.MenuId);

                e.HasOne(x => x.Staff)
                 .WithMany()
                 .HasForeignKey(x => x.StaffId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Menu)
                 .WithMany()
                 .HasForeignKey(x => x.MenuId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(x => x.AccessFeatures)
                 .WithOne(x => x.StaffMenuAccess)
                 .HasForeignKey(x => x.StaffMenuAccessId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── AccessFeatures (RBAC Tier-2) ──────────────────────────────────
            builder.Entity<AccessFeature>(e =>
            {
                e.ToTable("AccessFeatures");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.IsAllow).HasDefaultValue(true);

                // One row per (menuAccess + permission)
                e.HasIndex(x => new { x.StaffMenuAccessId, x.PermissionId }).IsUnique();
                e.HasIndex(x => x.StaffMenuAccessId);
                e.HasIndex(x => x.PermissionId);

                e.HasOne(x => x.Feature)
                 .WithMany()
                 .HasForeignKey(x => x.PermissionId)
                 .OnDelete(DeleteBehavior.Cascade);
                // StaffMenuAccess → AccessFeatures cascade is configured on the parent side above
            });
        }
    }
}
