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
        public DbSet<PersonHrProfile>          PersonHrProfiles         => Set<PersonHrProfile>();
        public DbSet<PersonEducation>          PersonEducations         => Set<PersonEducation>();
        public DbSet<PersonExperience>         PersonExperiences        => Set<PersonExperience>();
        public DbSet<PersonHrProfileReadRow>   PersonHrProfileReadRows  => Set<PersonHrProfileReadRow>();
        public DbSet<Designation>              Designations             => Set<Designation>();
        public DbSet<Designation>              JobTitles                => Set<Designation>();
        public DbSet<SalaryScale>              SalaryScales             => Set<SalaryScale>();
        public DbSet<PlatformTypeCategory>     PlatformTypeCategories   => Set<PlatformTypeCategory>();
        public DbSet<PlatformTypeValue>        PlatformTypeValues       => Set<PlatformTypeValue>();
        public DbSet<ContractType>             ContractTypes            => Set<ContractType>();
        public DbSet<FrequencyType>            FrequencyTypes           => Set<FrequencyType>();
        public DbSet<RateType>                 RateTypes                => Set<RateType>();
        public DbSet<AllowanceType>            AllowanceTypes           => Set<AllowanceType>();
        public DbSet<TadaType>                 TadaTypes                => Set<TadaType>();
        public DbSet<LeaveType>                LeaveTypes               => Set<LeaveType>();
        public DbSet<AnnouncementType>         AnnouncementTypes        => Set<AnnouncementType>();
        public DbSet<AssessmentType>           AssessmentTypes          => Set<AssessmentType>();
        public DbSet<AttendanceType>           AttendanceTypes          => Set<AttendanceType>();
        public DbSet<BenefitType>              BenefitTypes             => Set<BenefitType>();
        public DbSet<PlatformSettingAction>    PlatformSettingActions   => Set<PlatformSettingAction>();
        public DbSet<PlatformSettingStatus>    PlatformSettingStatuses  => Set<PlatformSettingStatus>();
        public DbSet<PlatformSettingColor>     PlatformSettingColors    => Set<PlatformSettingColor>();
        public DbSet<PlatformSettingActionStatus> PlatformSettingActionStatuses => Set<PlatformSettingActionStatus>();
        public DbSet<PlatformSettingStatusCrDbValue> PlatformSettingStatusCrDbValues => Set<PlatformSettingStatusCrDbValue>();
        public DbSet<ProcessMaster>            Processes                => Set<ProcessMaster>();
        public DbSet<StatusDefinition>         Statuses                 => Set<StatusDefinition>();
        public DbSet<ColorStyle>               ColorStyles              => Set<ColorStyle>();
        public DbSet<ProcessStatusStyle>       ProcessStatusStyles      => Set<ProcessStatusStyle>();
        public DbSet<StatusConfigurationManagementRow> StatusConfigurationManagementRows => Set<StatusConfigurationManagementRow>();
        public IQueryable<ProcessStatusStyle> AttendanceStatuses =>
            ProcessStatusStyles.Where(x => x.Process.ProcessName == "Attendance");
        public DbSet<AttendanceRecord>         AttendanceRecords        => Set<AttendanceRecord>();
        public DbSet<EmployeeTimingSchedule>   EmployeeTimingSchedules  => Set<EmployeeTimingSchedule>();
        public DbSet<StaffDirectoryRow>        StaffDirectoryRows       => Set<StaffDirectoryRow>();
        public DbSet<AttendanceMapRule>        AttendanceMapRules       => Set<AttendanceMapRule>();
        public DbSet<AttendanceMapRuleReadRow> AttendanceMapRuleReadRows => Set<AttendanceMapRuleReadRow>();
        public DbSet<AttendanceRuleSetting>    AttendanceRuleSettings   => Set<AttendanceRuleSetting>();
        public DbSet<WorkflowApprovalRequest> WorkflowApprovalRequests => Set<WorkflowApprovalRequest>();
        public DbSet<AttendanceRuleSettingReadRow> AttendanceRuleSettingReadRows => Set<AttendanceRuleSettingReadRow>();
        public DbSet<AttendanceDeductionRequest> AttendanceDeductionRequests => Set<AttendanceDeductionRequest>();
        public DbSet<AttendanceHolidayColorMap> AttendanceHolidayColorMaps => Set<AttendanceHolidayColorMap>();
        public DbSet<AttendanceHolidayColorMapReadRow> AttendanceHolidayColorMapReadRows => Set<AttendanceHolidayColorMapReadRow>();
        public DbSet<AttendanceWorkMode>       AttendanceWorkModes      => Set<AttendanceWorkMode>();
        public DbSet<AttendanceDailyReportRow> AttendanceDailyReportRows => Set<AttendanceDailyReportRow>();
        public DbSet<AttendanceDeductionReportRow> AttendanceDeductionReportRows => Set<AttendanceDeductionReportRow>();
        public DbSet<ApplicationLoginSession>  ApplicationLoginSessions => Set<ApplicationLoginSession>();
        public DbSet<AttendancePolicy> AttendancePolicies => Set<AttendancePolicy>();
        public DbSet<StaffAssessment> StaffAssessments => Set<StaffAssessment>();
        public DbSet<AssessmentBonusRule> AssessmentBonusRules => Set<AssessmentBonusRule>();
        public DbSet<AssessmentSchedule> AssessmentSchedules => Set<AssessmentSchedule>();
        public DbSet<VacancyCounter>           VacancyCounters          => Set<VacancyCounter>();
        public DbSet<Menu>                     Menus                    => Set<Menu>();
        public DbSet<MenuPermission>           MenuPermissions          => Set<MenuPermission>();
        public DbSet<Feature>                  Features                 => Set<Feature>();
        public DbSet<StaffAccessGroup>         StaffAccessGroups        => Set<StaffAccessGroup>();
        public DbSet<DepartmentAccessMatrix>   DepartmentAccessMatrix   => Set<DepartmentAccessMatrix>();
        // â”€â”€ Hierarchical RBAC (legacy â€” kept during migration) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public DbSet<RolePermission>           RolePermissions          => Set<RolePermission>();
        // NOTE: UserPermissionOverrides table was dropped in V2 migration.
        //       All permission writes now go through StaffMenuAccess + AccessFeatures.
        // â”€â”€ New 2-Tier RBAC â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public DbSet<StaffMenuAccess>          StaffMenuAccesses        => Set<StaffMenuAccess>();
        public DbSet<AccessFeature>            AccessFeatures           => Set<AccessFeature>();
        public DbSet<PersonMenu>               PersonMenus              => Set<PersonMenu>();
        public DbSet<PersonFeature>            PersonFeatures           => Set<PersonFeature>();

        // â”€â”€ Multi-Tenant SaaS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public DbSet<Tenant>                   Tenants                  => Set<Tenant>();
        public DbSet<TenantMenuPermission>     TenantMenuPermissions    => Set<TenantMenuPermission>();
        public DbSet<TenantRolePermission>     TenantRolePermissions    => Set<TenantRolePermission>();

        // â”€â”€ Communication Center â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public DbSet<AppLookupType>     AppLookupTypes     => Set<AppLookupType>();
        public DbSet<AppLookupValue>    AppLookupValues    => Set<AppLookupValue>();
        // NOTE: AppMenuDefinitions table dropped in V2 migration. Use Menus table instead.
        public DbSet<AppNote>           AppNotes            => Set<AppNote>();
        public DbSet<AppNoteTarget>     AppNoteTargets      => Set<AppNoteTarget>();
        public DbSet<AppNoteUserStatus> AppNoteUserStatuses => Set<AppNoteUserStatus>();
        public DbSet<AppNoteUserState>  AppNoteUserStates   => Set<AppNoteUserState>();
        public DbSet<AppNoteAttachment> AppNoteAttachments  => Set<AppNoteAttachment>();
        public DbSet<ChatWorkspace> ChatWorkspaces => Set<ChatWorkspace>();
        public DbSet<ChatContactRequest> ChatContactRequests => Set<ChatContactRequest>();
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
        public DbSet<ChatConversationMember> ChatConversationMembers => Set<ChatConversationMember>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<ChatMessageReaction> ChatMessageReactions => Set<ChatMessageReaction>();
        public DbSet<ChatMessageDeletion> ChatMessageDeletions => Set<ChatMessageDeletion>();
        public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
        public DbSet<ChatBlock> ChatBlocks => Set<ChatBlock>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Global filters are fail-closed. A request without a verified tenant
            // sees no operational rows, and SuperAdmin sees no tenant operational
            // rows. Explicit IgnoreQueryFilters is reserved for reviewed platform
            // provisioning services.
            builder.Entity<ChatWorkspace>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatContactRequest>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatConversation>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatConversationMember>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatMessage>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatMessageReaction>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatMessageDeletion>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatAttachment>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ChatBlock>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<Person>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PersonAddress>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Person != null &&
                row.Person.TenantId == _tenantService.TenantId);
            builder.Entity<PersonContact>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Person != null &&
                row.Person.TenantId == _tenantService.TenantId);
            builder.Entity<PersonHrProfile>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PersonEducation>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PersonExperience>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PersonHrProfileReadRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<Vacancy>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<StaffVacancy>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<StaffDirectoryRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<StaffAccessGroup>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Staff != null &&
                row.Staff.TenantId == _tenantService.TenantId);
            builder.Entity<DepartmentAccessMatrix>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Staff != null &&
                row.Staff.TenantId == _tenantService.TenantId);
            builder.Entity<StaffMenuAccess>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Staff != null &&
                row.Staff.TenantId == _tenantService.TenantId);
            builder.Entity<UserPermissionOverride>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Staff != null &&
                row.Staff.TenantId == _tenantService.TenantId);
            builder.Entity<PersonMenu>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Person != null &&
                row.Person.TenantId == _tenantService.TenantId);
            builder.Entity<PersonFeature>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Person != null &&
                row.Person.TenantId == _tenantService.TenantId);
            builder.Entity<Designation>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<SalaryScale>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PlatformTypeValue>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PlatformTypeTableRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PlatformSettingNamedRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PlatformSettingColor>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PlatformSettingActionStatus>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<PlatformSettingStatusCrDbValue>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceRecord>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceDeductionRequest>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<ApplicationLoginSession>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<EmployeeTimingSchedule>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceMapRule>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceMapRuleReadRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceRuleSetting>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<WorkflowApprovalRequest>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceRuleSettingReadRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceHolidayColorMap>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AttendanceHolidayColorMapReadRow>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AppNote>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null &&
                (row.TenantId == null || row.TenantId == _tenantService.TenantId));
            builder.Entity<AppNoteTarget>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Note != null &&
                (row.Note.TenantId == null || row.Note.TenantId == _tenantService.TenantId));
            builder.Entity<AppNoteUserStatus>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Note != null &&
                (row.Note.TenantId == null || row.Note.TenantId == _tenantService.TenantId));
            builder.Entity<AppNoteUserState>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Note != null &&
                (row.Note.TenantId == null || row.Note.TenantId == _tenantService.TenantId));
            builder.Entity<AppNoteAttachment>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.Note != null &&
                (row.Note.TenantId == null || row.Note.TenantId == _tenantService.TenantId));
            builder.Entity<StaffAssessment>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AssessmentBonusRule>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);
            builder.Entity<AssessmentSchedule>().HasQueryFilter(row =>
                _tenantService != null && !_tenantService.IsSuperAdmin &&
                _tenantService.TenantId != null && row.TenantId == _tenantService.TenantId);

            // ——— ApplicationUser (AspNetUsers) — multi-tenant columns ————————————
            builder.Entity<ApplicationUser>(e =>
            {
                e.Property(u => u.TenantId).IsRequired(false);
                e.Property(u => u.IsSuperAdmin).HasDefaultValue(false);
                e.Property(u => u.IsTenantAdmin).HasDefaultValue(false);
                e.HasIndex(u => u.TenantId);
            });

            // ——— Tenants table ——————————————————————————————————————————————————
            builder.Entity<ApplicationLoginSession>(e =>
            {
                e.ToTable("ApplicationLoginSessions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.IdentityUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.IpAddress).HasMaxLength(45).IsRequired(false);
                e.Property(x => x.UserAgent).HasMaxLength(300).IsRequired(false);
                e.Property(x => x.Source).HasMaxLength(50).HasDefaultValue("Software");
                e.Property(x => x.Remarks).HasMaxLength(500).IsRequired(false);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.Property(x => x.WorkingMinutes).HasDefaultValue(0);
                e.HasIndex(x => new { x.TenantId, x.SessionDate });
                e.HasIndex(x => new { x.IdentityUserId, x.LogoutUtc });
                e.HasIndex(x => new { x.StaffId, x.SessionDate });
                e.HasOne(x => x.Staff)
                 .WithMany()
                 .HasForeignKey(x => x.StaffId)
                 .OnDelete(DeleteBehavior.SetNull)
                 .IsRequired(false);
                e.HasOne(x => x.Person)
                 .WithMany()
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.SetNull)
                 .IsRequired(false);
                e.HasOne(x => x.IdentityUser)
                 .WithMany()
                 .HasForeignKey(x => x.IdentityUserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

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
                e.Property(x => x.BrandingFileName).HasMaxLength(255).IsRequired(false);
                e.Property(x => x.BrandingContentType).HasMaxLength(100).IsRequired(false);
                e.Property(x => x.BrandingAssetType).HasMaxLength(20).IsRequired(false);
                e.Property(x => x.BrandingContent).HasColumnType("varbinary(max)").IsRequired(false);
                e.Property(x => x.BrandingUpdatedOnUtc).IsRequired(false);

                // One org node = one tenant
                e.HasIndex(x => x.OrganizationTreeId).IsUnique();
                e.HasIndex(x => x.TenantCode).IsUnique();

                e.HasOne(x => x.OrganizationNode)
                 .WithMany()
                 .HasForeignKey(x => x.OrganizationTreeId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ——— TenantMenuPermissions ——————————————————————————————————————————
            builder.Entity<TenantMenuPermission>(e =>
            {
                e.ToTable("TenantMenuPermissions");
                e.HasKey(x => new { x.TenantId, x.MenuId });
                e.Property(x => x.IsAllow).HasDefaultValue(true);
                // Do NOT configure store defaults for CRUD flags. EF Core skips
                // INSERT columns when the value equals HasDefaultValue(...), and
                // a mismatched SQL default previously turned full CRUD into View-only.
                e.Property(x => x.CanView).IsRequired();
                e.Property(x => x.CanAdd).IsRequired();
                e.Property(x => x.CanEdit).IsRequired();
                e.Property(x => x.CanDelete).IsRequired();
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

            // ——— TenantRolePermissions ——————————————————————————————————————————
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

            // ——— Person: TenantId FK ————————————————————————————————————————————
            builder.Entity<Person>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ——— Vacancy: TenantId FK ———————————————————————————————————————————
            builder.Entity<Vacancy>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ——— StaffVacancy: TenantId FK ——————————————————————————————————————
            builder.Entity<StaffVacancy>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SalaryScale>(e =>
            {
                e.ToTable("SalaryScales");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.TenantId).IsRequired();
                e.Property(x => x.ScaleName).HasMaxLength(100).IsRequired();
                e.Property(x => x.DisplayOrder).HasDefaultValue(0);
                e.Property(x => x.ScaleType).HasMaxLength(50).HasDefaultValue("Regular");
                e.Property(x => x.PayMode).HasMaxLength(20).HasDefaultValue("PM");
                e.Property(x => x.BasicSalary).HasColumnType("decimal(18,2)");
                e.Property(x => x.MaximumSalary).HasColumnType("decimal(18,2)");
                e.Property(x => x.YearlyIncrement).HasColumnType("decimal(18,2)");
                e.Property(x => x.GrossSalary).HasColumnType("decimal(18,2)");
                e.Property(x => x.MedicalAllowance).HasColumnType("decimal(18,2)");
                e.Property(x => x.TravellingAllowance).HasColumnType("decimal(18,2)");
                e.Property(x => x.Other).HasColumnType("decimal(18,2)");
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => x.TenantId);
                e.HasIndex(x => new { x.TenantId, x.ScaleName }).IsUnique();
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ——— AppNote: optional TenantId FK ——————————————————————————————————
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

            // ——— Menu and MenuPermissions ———————————————————————————————————————
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
                e.Property(x => x.IsActive).HasDefaultValue(true);
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
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.ShiftStartTime).HasDefaultValue("09:00");
                e.Property(x => x.ShiftEndTime).HasDefaultValue("18:00");
                e.Property(x => x.TimeZoneId).HasDefaultValue("Asia/Karachi");

                e.HasIndex(x => x.IdentityUserId).IsUnique();
                e.HasIndex(x => x.ReportsToPersonId);
                e.HasOne(x => x.ReportsToPerson)
                 .WithMany(x => x.DirectReports)
                 .HasForeignKey(x => x.ReportsToPersonId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => x.AlternativeReportsToPersonId);
                e.HasOne(x => x.AlternativeReportsToPerson)
                 .WithMany(x => x.AlternativeDirectReports)
                 .HasForeignKey(x => x.AlternativeReportsToPersonId)
                 .OnDelete(DeleteBehavior.Restrict);
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

            builder.Entity<ProcessMaster>(e =>
            {
                e.ToTable("Processes");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.ProcessName).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.ProcessName).IsUnique();
            });

            builder.Entity<StatusDefinition>(e =>
            {
                e.ToTable("Statuses");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.StatusName).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.StatusName).IsUnique();
            });

            builder.Entity<ColorStyle>(e =>
            {
                e.ToTable("ColorStyles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.ColorName).HasMaxLength(100).IsRequired();
                e.Property(x => x.ColorCode).HasMaxLength(20);
                e.Property(x => x.FontColor).HasMaxLength(20);
                e.Property(x => x.FontSize).HasMaxLength(20);
                e.HasIndex(x => new { x.ColorName, x.ColorCode, x.FontColor, x.FontSize }).IsUnique();
            });

            builder.Entity<ProcessStatusStyle>(e =>
            {
                e.ToTable("ProcessStatusStyles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.Code).HasMaxLength(10).IsRequired();
                e.Property(x => x.Description).HasMaxLength(500);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.ProcessId, x.Code }).IsUnique().HasFilter("[TenantId] IS NULL");
                e.HasIndex(x => new { x.TenantId, x.ProcessId, x.Code }).IsUnique().HasFilter("[TenantId] IS NOT NULL");
                e.HasIndex(x => new { x.ProcessId, x.StatusId, x.ColorStyleId }).HasFilter("[TenantId] IS NULL");
                e.HasOne(x => x.Process).WithMany(x => x.StatusStyles).HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Status).WithMany(x => x.ProcessStyles).HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ColorStyle).WithMany(x => x.ProcessStatuses).HasForeignKey(x => x.ColorStyleId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<StatusConfigurationManagementRow>(e =>
            {
                e.HasNoKey();
                e.ToView("vw_StatusConfigurationsForManagement", "dbo");
                e.Property(x => x.ProcessName).HasMaxLength(100);
                e.Property(x => x.StatusName).HasMaxLength(100);
                e.Property(x => x.Code).HasMaxLength(10);
                e.Property(x => x.Description).HasMaxLength(500);
                e.Property(x => x.ColorName).HasMaxLength(100);
                e.Property(x => x.ColorCode).HasMaxLength(20);
                e.Property(x => x.FontColor).HasMaxLength(20);
                e.Property(x => x.FontSize).HasMaxLength(20);
            });

            builder.Entity<PlatformTypeCategory>(e =>
            {
                e.ToTable("PlatformTypeCategories");
                e.HasKey(x => x.Id);
                e.Property(x => x.Code).HasMaxLength(50).IsRequired();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Icon).HasMaxLength(50).IsRequired();
                e.HasIndex(x => x.Code).IsUnique();
            });

            builder.Entity<PlatformTypeValue>(e =>
            {
                e.ToTable("PlatformTypeValues");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(150).IsRequired();
                e.Property(x => x.Code).HasMaxLength(100).IsRequired();
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.CategoryId, x.Code }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.CategoryId, x.DisplayOrder });
                e.HasOne(x => x.Category).WithMany(x => x.Values).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PlatformTypeTableRow>(e =>
            {
                e.UseTpcMappingStrategy();
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(150).IsRequired();
                e.Property(x => x.Code).HasMaxLength(100).IsRequired();
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            ConfigurePlatformTypeTable<ContractType>(builder, "ContractTypes");
            ConfigurePlatformTypeTable<FrequencyType>(builder, "FrequencyTypes");
            ConfigurePlatformTypeTable<RateType>(builder, "RateTypes");
            ConfigurePlatformTypeTable<AllowanceType>(builder, "AllowanceTypes");
            ConfigurePlatformTypeTable<TadaType>(builder, "TadaTypes");
            ConfigurePlatformTypeTable<LeaveType>(builder, "LeaveTypes");
            ConfigurePlatformTypeTable<AnnouncementType>(builder, "AnnouncementTypes");
            ConfigurePlatformTypeTable<AssessmentType>(builder, "AssessmentTypes");
            ConfigurePlatformTypeTable<AttendanceType>(builder, "AttendanceTypes");
            ConfigurePlatformTypeTable<BenefitType>(builder, "BenefitTypes");

            builder.Entity<PlatformSettingNamedRow>(e =>
            {
                e.UseTpcMappingStrategy();
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(150).IsRequired();
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });
            ConfigurePlatformSettingNameTable<PlatformSettingAction>(builder, "Actions");
            ConfigurePlatformSettingNameTable<PlatformSettingStatus>(builder, "Statuses");

            builder.Entity<PlatformSettingColor>(e =>
            {
                e.ToTable("Colors", "PlatformSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.ColorCode).HasMaxLength(9).IsRequired();
                e.Property(x => x.FontColor).HasMaxLength(9);
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.ColorCode }).IsUnique();
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PlatformSettingActionStatus>(e =>
            {
                e.ToTable("ActionStatuses", "PlatformSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.ActionId, x.StatusId }).IsUnique();
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Action).WithMany().HasForeignKey(x => x.ActionId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Color).WithMany().HasForeignKey(x => x.ColorId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PlatformSettingStatusCrDbValue>(e =>
            {
                e.ToTable("StatusCrDbValues", "PlatformSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.CrValue).HasMaxLength(150).IsRequired();
                e.Property(x => x.DbValue).HasMaxLength(150).IsRequired();
                e.Property(x => x.CreatedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.StatusId }).IsUnique();
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AttendanceRecord>(e =>
            {
                e.ToTable("AttendanceRecords");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.PersonId, x.AttendanceDate }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.AttendanceDate });
                e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.AttendanceStatus).WithMany().HasForeignKey(x => x.AttendanceStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformActionStatus).WithMany().HasForeignKey(x => x.PlatformActionStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.AttendanceEntryType).WithMany().HasForeignKey(x => x.AttendanceEntryTypeId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.AttendanceWorkMode).WithMany(x => x.Records).HasForeignKey(x => x.AttendanceWorkModeId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.VerificationStatus).WithMany().HasForeignKey(x => x.VerificationStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformVerificationStatus).WithMany().HasForeignKey(x => x.PlatformVerificationStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<StaffDirectoryRow>(e =>
            {
                e.HasNoKey();
                e.ToView("vw_StaffDirectory", "dbo");
                e.Property(x => x.EmployeeId).HasMaxLength(50);
                e.Property(x => x.FullName).HasMaxLength(200);
                e.Property(x => x.Department).HasMaxLength(200);
                e.Property(x => x.Designation).HasMaxLength(150);
                e.Property(x => x.PhotoUrl).HasMaxLength(1000);
                e.Property(x => x.ShiftStartTime).HasMaxLength(5);
                e.Property(x => x.ShiftEndTime).HasMaxLength(5);
                e.Property(x => x.TimeZoneId).HasMaxLength(100);
            });

            builder.Entity<EmployeeTimingSchedule>(e =>
            {
                e.ToTable("EmployeeTimingSchedules", table => table.HasCheckConstraint(
                    "CK_EmployeeTimingSchedules_RequiredWeekend",
                    "(((DATEDIFF(day,'19000101',[ScheduleDate]) % 7 + 7) % 7) NOT IN (5,6)) OR " +
                    "(((DATEDIFF(day,'19000101',[ScheduleDate]) % 7 + 7) % 7) IN (5,6) AND [IsOn] = 0 AND [TimeFrom] IS NULL AND [TimeTo] IS NULL AND [WorkingMinutes] = 0)"));
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.TimeFrom).HasMaxLength(5);
                e.Property(x => x.TimeTo).HasMaxLength(5);
                e.Property(x => x.IsOn).HasDefaultValue(true);
                e.Property(x => x.WorkingMinutes).HasDefaultValue(0);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.StaffId, x.ScheduleDate }).IsUnique();
                e.HasIndex(x => new { x.StaffId, x.ScheduleYear, x.ScheduleMonth });
                e.HasIndex(x => new { x.TenantId, x.ScheduleDate });
                e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.HolidayType).WithMany().HasForeignKey(x => x.HolidayTypeId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AttendanceMapRule>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.ShiftCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.TimeFrom).HasMaxLength(5).IsRequired();
                e.Property(x => x.TimeTo).HasMaxLength(5).IsRequired();
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.StaffId }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.AttendanceEntryTypeId });
                e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.AttendanceEntryType).WithMany().HasForeignKey(x => x.AttendanceEntryTypeId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AttendanceMapRuleReadRow>(e =>
            {
                e.HasNoKey();
                e.ToView("vw_AttendanceMapRules", "dbo");
                e.Property(x => x.AttendanceTypeCode).HasMaxLength(50);
                e.Property(x => x.AttendanceTypeName).HasMaxLength(100);
                e.Property(x => x.ShiftCode).HasMaxLength(100);
                e.Property(x => x.ShiftName).HasMaxLength(200);
                e.Property(x => x.TimeFrom).HasMaxLength(5);
                e.Property(x => x.TimeTo).HasMaxLength(5);
            });

            builder.Entity<AttendanceRuleSetting>(e =>
            {
                e.ToTable("AttendanceRuleSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Reference).HasMaxLength(50).IsRequired();
                e.Property(x => x.RuleName).HasMaxLength(150).IsRequired();
                e.Property(x => x.Remarks).HasMaxLength(500).IsRequired(false);
                e.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired(false);
                e.Property(x => x.ModifiedByUserId).HasMaxLength(450).IsRequired(false);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.Property(x => x.WorkingMinutes).HasDefaultValue(540);
                e.Property(x => x.BeforeCheckInMinutes).HasDefaultValue(5);
                e.Property(x => x.AfterCheckOutMinutes).HasDefaultValue(0);
                e.Property(x => x.CheckInAdjustMinutes).HasDefaultValue(5);
                e.Property(x => x.CheckOutAdjustMinutes).HasDefaultValue(5);
                e.Property(x => x.AbsentAfterShiftStartMinutes).HasDefaultValue(120);
                e.Property(x => x.EarlyCheckoutAbsentAfterMinutes).HasDefaultValue(120);
                e.Property(x => x.MissingCheckoutAfterShiftEndMinutes).HasDefaultValue(120);
                e.Property(x => x.CameraVerificationToleranceMinutes).HasDefaultValue(10);
                e.Property(x => x.WeekendChargeValue).HasColumnType("decimal(6,2)").HasDefaultValue(0m);
                e.Property(x => x.AccountLockAbsentDays).HasDefaultValue(0);
                e.Property(x => x.AdjustAbsentDays).HasDefaultValue(0);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.HasIndex(x => new { x.TenantId, x.AttendanceEntryTypeId }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.IsActive, x.IsApproved });
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.AttendanceEntryType).WithMany().HasForeignKey(x => x.AttendanceEntryTypeId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<WorkflowApprovalRequest>(e =>
            {
                e.ToTable("WorkflowApprovalRequests");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.ProcessCode).HasMaxLength(80).IsRequired();
                e.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
                e.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
                e.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.StatusCode).HasMaxLength(20).IsRequired();
                e.Property(x => x.DecisionCode).HasMaxLength(40);
                e.Property(x => x.DecisionByUserId).HasMaxLength(450);
                e.Property(x => x.Comments).HasMaxLength(1000);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.ProcessCode, x.EntityType, x.EntityId, x.StatusCode });
                e.HasIndex(x => new { x.TenantId, x.StatusCode, x.CreatedDate });
            });

            builder.Entity<AttendanceRuleSettingReadRow>(e =>
            {
                e.HasNoKey();
                e.ToView("vw_AttendanceRuleSettings", "dbo");
                e.Property(x => x.AttendanceTypeCode).HasMaxLength(30);
                e.Property(x => x.AttendanceTypeName).HasMaxLength(100);
                e.Property(x => x.Reference).HasMaxLength(50);
                e.Property(x => x.RuleName).HasMaxLength(150);
                e.Property(x => x.WeekendChargeValue).HasColumnType("decimal(6,2)");
                e.Property(x => x.Remarks).HasMaxLength(500);
            });

            builder.Entity<AttendanceDeductionRequest>(e =>
            {
                e.ToTable("AttendanceDeductionRequests");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.UserId).HasMaxLength(100).IsRequired();
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.DeductionYear, x.DeductionMonth });
                e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AttendanceHolidayColorMap>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.HolidayTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.ColorCode).HasMaxLength(7).IsRequired();
                e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.TenantId, x.HolidayTypeCode }).IsUnique();
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AttendanceHolidayColorMapReadRow>(e =>
            {
                e.HasNoKey();
                e.ToView("vw_AttendanceHolidayColorMaps", "dbo");
                e.Property(x => x.HolidayTypeCode).HasMaxLength(100);
                e.Property(x => x.HolidayTypeName).HasMaxLength(200);
                e.Property(x => x.ColorCode).HasMaxLength(7);
            });

            builder.Entity<AttendanceWorkMode>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Code).HasMaxLength(30).IsRequired();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.Code).IsUnique();
            });

            builder.Entity<AttendanceDailyReportRow>(e =>
            {
                e.HasNoKey();
                e.ToView(null);
            });

            builder.Entity<AttendanceDeductionReportRow>(e =>
            {
                e.HasNoKey();
                e.ToView(null);
                e.Property(x => x.StaffNumber).HasMaxLength(50);
                e.Property(x => x.EmployeeName).HasMaxLength(200);
                e.Property(x => x.JobTitle).HasMaxLength(150);
                e.Property(x => x.Department).HasMaxLength(200);
                e.Property(x => x.DeductionDays).HasColumnType("decimal(18,2)");
                e.Property(x => x.GrossDeduction).HasColumnType("decimal(18,2)");
                e.Property(x => x.AdjustAmount).HasColumnType("decimal(18,2)");
                e.Property(x => x.NetDeduction).HasColumnType("decimal(18,2)");
                e.Property(x => x.PerHour).HasColumnType("decimal(18,2)");
                e.Property(x => x.PerDay).HasColumnType("decimal(18,2)");
            });

            builder.Entity<AttendancePolicy>(e =>
            {
                e.ToTable("AttendancePolicies"); e.HasKey(x => x.Id);
                e.Property(x => x.PolicyName).HasMaxLength(100).IsRequired();
                e.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.TenantId).IsUnique().HasFilter("[IsActive] = 1");
                e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PresentStatus).WithMany().HasForeignKey(x => x.PresentStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformPresentStatus).WithMany().HasForeignKey(x => x.PlatformPresentStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.LateStatus).WithMany().HasForeignKey(x => x.LateStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformLateStatus).WithMany().HasForeignKey(x => x.PlatformLateStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.CompletedLateStatus).WithMany().HasForeignKey(x => x.CompletedLateStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformCompletedLateStatus).WithMany().HasForeignKey(x => x.PlatformCompletedLateStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ShortLeaveStatus).WithMany().HasForeignKey(x => x.ShortLeaveStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformShortLeaveStatus).WithMany().HasForeignKey(x => x.PlatformShortLeaveStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.EarlyDepartureStatus).WithMany().HasForeignKey(x => x.EarlyDepartureStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformEarlyDepartureStatus).WithMany().HasForeignKey(x => x.PlatformEarlyDepartureStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.AbsentStatus).WithMany().HasForeignKey(x => x.AbsentStatusId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.PlatformAbsentStatus).WithMany().HasForeignKey(x => x.PlatformAbsentStatusId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<VacancyCounter>(e =>
            {
                e.ToTable("VacancyCounters");
                e.HasKey(x => x.Prefix);
                e.Property(x => x.Prefix).HasMaxLength(200).IsRequired();
                e.Property(x => x.LastNumber).HasDefaultValue(0).IsRequired();
            });

            // ——— Features (Master Permissions) ——————————————————————————————————
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

            // ——— DepartmentAccessMatrix (Legacy) ————————————————————————————————
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

            // ——— RolePermission (Optimized) —————————————————————————————————————
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

            // ——— Communication Center: AppLookupTypes ———————————————————————————
            builder.Entity<AppLookupType>(e =>
            {
                e.ToTable("AppLookupTypes");
                e.HasKey(x => x.LookupTypeId);
                e.Property(x => x.LookupTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.LookupTypeName).HasMaxLength(150).IsRequired();
                e.HasIndex(x => x.LookupTypeCode).IsUnique();
            });

            // ——— Communication Center: AppLookupValues ——————————————————————————
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

            // ——— Communication Center: AppNotes —————————————————————————————————
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
                e.HasIndex(x => new { x.TenantId, x.IsPublished, x.IsActive, x.IsDeleted })
                 .HasDatabaseName("IX_AppNotes_TenantId_PublishedActiveDeleted");
                e.HasIndex(x => new { x.IsPublished, x.IsActive, x.IsDeleted, x.StartDateUtc, x.EndDateUtc })
                 .HasDatabaseName("IX_AppNotes_PublishedActiveDeleted_Dates");
                e.HasOne<ApplicationUser>()
                 .WithMany()
                 .HasForeignKey(x => x.OwnerIdentityUserId)
                 .OnDelete(DeleteBehavior.SetNull);
                e.HasMany(x => x.Targets).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.UserStatuses).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.UserStates).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Attachments).WithOne(x => x.Note).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            });

            // ——— Communication Center: AppNoteTargets ———————————————————————————
            builder.Entity<AppNoteTarget>(e =>
            {
                e.ToTable("AppNoteTargets");
                e.HasKey(x => x.NoteTargetId);
                e.Property(x => x.TargetTypeCode).HasMaxLength(100).IsRequired();
                e.Property(x => x.TargetValue).HasMaxLength(150).IsRequired();
                e.HasIndex(x => new { x.NoteId, x.TargetTypeCode, x.TargetValue })
                 .HasDatabaseName("IX_AppNoteTargets_NoteId_TypeValue");
                e.HasIndex(x => new { x.TargetTypeCode, x.TargetValue, x.NoteId })
                 .HasDatabaseName("IX_AppNoteTargets_TypeValueNoteId");
            });

            // ——— Communication Center: AppNoteUserStatuses (legacy) —————————————
            builder.Entity<AppNoteUserStatus>(e =>
            {
                e.ToTable("AppNoteUserStatuses");
                e.HasKey(x => x.NoteUserStatusId);
                e.Property(x => x.UserId).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.NoteId, x.UserId }).IsUnique();
            });

            // ——— Communication Center: AppNoteUserStates (per-staff) ————————————
            builder.Entity<AppNoteUserState>(e =>
            {
                e.ToTable("AppNoteUserStates");
                e.HasKey(x => x.AppNoteUserStateId);
                e.Property(x => x.StaffId).HasMaxLength(100).IsRequired();
                // One row per (NoteId, StaffId)
                e.HasIndex(x => new { x.NoteId, x.StaffId }).IsUnique();
                e.HasIndex(x => new { x.StaffId, x.NoteId })
                 .HasDatabaseName("IX_AppNoteUserStates_StaffId_NoteId");
            });

            // ——— Communication Center: AppNoteAttachments ——————————————————————
            builder.Entity<AppNoteAttachment>(e =>
            {
                e.ToTable("AppNoteAttachments");
                e.HasKey(x => x.AttachmentId);
            });

            // ——— Keyless query types (stored procedures / views) ————————————————
            builder.Entity<OrganizationVacancyPersonDto>().HasNoKey();
            builder.Entity<EmployeeByOrgAndRoleDto>().HasNoKey();

            // ——— Designations (JobTitles table — tenant-scoped) ———————————————
            builder.Entity<Designation>(e =>
            {
                e.ToTable("JobTitles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.Name).HasColumnName("TitleName").HasMaxLength(100).IsRequired();
            });

            // ——— Designation: TenantId FK ——————————————————————————————————————
            builder.Entity<Designation>(e =>
            {
                e.Property(x => x.TenantId).IsRequired();
                e.HasIndex(x => x.TenantId);
                e.HasOne<Tenant>()
                 .WithMany()
                 .HasForeignKey(x => x.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            });

            // ——— PersonContacts (one-to-many contacts per person) ———————————————
            builder.Entity<PersonContact>(e =>
            {
                e.ToTable("PersonContacts", table => table.HasCheckConstraint(
                    "CK_PersonContacts_Type",
                    "[ContactType] IN ('Email','PersonalEmail','Phone','WhatsApp','Emergency','Other')"));
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

            builder.Entity<PersonHrProfile>(e =>
            {
                e.ToTable("PersonHrProfiles");
                e.HasKey(x => x.PersonId);
                e.HasIndex(x => x.TenantId);
                e.HasOne(x => x.Person)
                 .WithOne()
                 .HasForeignKey<PersonHrProfile>(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<PersonEducation>(e =>
            {
                e.ToTable("PersonEducations");
                e.HasKey(x => x.EducationId);
                e.HasIndex(x => new { x.TenantId, x.PersonId, x.SortOrder });
                e.HasOne(x => x.Person)
                 .WithMany()
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<PersonExperience>(e =>
            {
                e.ToTable("PersonExperiences");
                e.HasKey(x => x.ExperienceId);
                e.HasIndex(x => new { x.TenantId, x.PersonId, x.SortOrder });
                e.HasOne(x => x.Person)
                 .WithMany()
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<PersonHrProfileReadRow>(e =>
            {
                e.HasNoKey();
                e.ToView("vw_PersonHrProfiles");
            });
            // ——— Vacancy: DesignationId FK ——————————————————————————————————————
            builder.Entity<Vacancy>(e =>
            {
                e.Property(x => x.DesignationId).HasColumnName("JobTitleId");
                e.Property(x => x.JobTitle).HasMaxLength(100);
                e.HasOne(x => x.DesignationNav)
                 .WithMany(x => x.Vacancies)
                 .HasForeignKey(x => x.DesignationId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired(false);
                e.HasIndex(x => x.DesignationId);
            });

            // ——— StaffMenuAccess (RBAC Tier-1) ——————————————————————————————————
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

            // ——— AccessFeatures (RBAC Tier-2) ———————————————————————————————————
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
                // StaffMenuAccess â†’ AccessFeatures cascade is configured on the parent side above
            });

            builder.Entity<PersonMenu>(e =>
            {
                e.ToTable("PersonMenus");
                e.HasKey(x => new { x.PersonId, x.MenuId });
                e.Property(x => x.GrantedBy).HasMaxLength(450);
                e.Property(x => x.GrantedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => x.MenuId);
                e.HasOne(x => x.Person)
                 .WithMany()
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Menu)
                 .WithMany()
                 .HasForeignKey(x => x.MenuId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<PersonFeature>(e =>
            {
                e.ToTable("PersonFeatures");
                e.HasKey(x => new { x.PersonId, x.PermissionId });
                e.Property(x => x.GrantedBy).HasMaxLength(450);
                e.Property(x => x.GrantedOnUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => x.PermissionId);
                e.HasOne(x => x.Person)
                 .WithMany()
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Feature)
                 .WithMany()
                 .HasForeignKey(x => x.PermissionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<StaffAssessment>().HasIndex(row => new
            {
                row.TenantId, row.AssessorPersonId, row.SubjectPersonId,
                row.AssessmentYear, row.AssessmentMonth
            }).IsUnique();
            builder.Entity<AssessmentBonusRule>().HasIndex(row => row.TenantId).IsUnique();
            builder.Entity<AssessmentSchedule>().HasIndex(row => new { row.TenantId, row.AssessmentYear, row.AssessmentMonth }).IsUnique();

            builder.Entity<ChatWorkspace>(e =>
            {
                e.HasIndex(x => new { x.TenantId, x.OrganizationTreeId }).IsUnique();
                e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<OrganizationTree>().WithMany().HasForeignKey(x => x.OrganizationTreeId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ChatContactRequest>(e =>
            {
                e.HasIndex(x => new { x.WorkspaceId, x.PairKey })
                    .IsUnique()
                    .HasFilter("[Status] = 'Pending'");
                e.HasIndex(x => new { x.TenantId, x.ReceiverPersonId, x.Status, x.CreatedOnUtc }).IsDescending(false, false, false, true);
                e.HasIndex(x => new { x.TenantId, x.SenderPersonId, x.Status, x.CreatedOnUtc }).IsDescending(false, false, false, true);
                e.HasIndex(x => new { x.TenantId, x.PairKey, x.Status });
                e.HasOne<ChatWorkspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.SenderPersonId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.ReceiverPersonId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ChatConversation>(e =>
            {
                e.HasIndex(x => new { x.WorkspaceId, x.DirectPairKey })
                    .IsUnique()
                    .HasFilter("[DirectPairKey] IS NOT NULL");
                e.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedOnUtc }).IsDescending(false, false, true);
                e.HasIndex(x => new { x.TenantId, x.IsActive, x.CreatedOnUtc });
                e.HasOne<ChatWorkspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.CreatedByPersonId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ChatConversationMember>(e =>
            {
                e.HasIndex(x => new { x.ConversationId, x.PersonId }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.PersonId, x.LeftOnUtc, x.ConversationId });
                e.HasIndex(x => new { x.TenantId, x.ConversationId, x.LeftOnUtc, x.PersonId });
                e.HasOne<ChatConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ChatMessage>(e =>
            {
                e.HasIndex(x => new { x.ConversationId, x.Id });
                e.HasIndex(x => new { x.TenantId, x.ConversationId, x.Id }).IsDescending(false, false, true);
                e.HasIndex(x => new { x.TenantId, x.ConversationId, x.CreatedOnUtc }).IsDescending(false, false, true);
                e.HasIndex(x => new { x.TenantId, x.SenderPersonId, x.ClientMessageId }).IsUnique();
                e.HasOne<ChatConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.SenderPersonId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.ReplyToMessageId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ChatMessageReaction>(e =>
            {
                e.HasIndex(x => new { x.MessageId, x.PersonId, x.Emoji }).IsUnique();
                e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ChatAttachment>(e =>
            {
                e.Property(x => x.Content).HasColumnType("varbinary(max)");
                e.HasIndex(x => new { x.MessageId, x.Id });
                e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ChatBlock>(e =>
            {
                e.HasIndex(x => new { x.TenantId, x.BlockerPersonId, x.BlockedPersonId }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.BlockedPersonId, x.BlockerPersonId });
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.BlockerPersonId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<Person>().WithMany().HasForeignKey(x => x.BlockedPersonId).OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigurePlatformTypeTable<TEntity>(ModelBuilder builder, string tableName)
            where TEntity : PlatformTypeTableRow
        {
            var entity = builder.Entity<TEntity>();
            // Keep all independent tenant-owned masters together in SQL Server.
            // The common schema makes the database easy to browse without
            // changing the existing tenant boundary or authorization model.
            entity.ToTable(tableName, "PlatformTypes");
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.DisplayOrder });
        }

        private static void ConfigurePlatformSettingNameTable<TEntity>(ModelBuilder builder, string tableName)
            where TEntity : PlatformSettingNamedRow
        {
            var entity = builder.Entity<TEntity>();
            entity.ToTable(tableName, "PlatformSettings");
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        }
    }
}



