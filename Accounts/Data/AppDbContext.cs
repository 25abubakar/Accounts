using Accounts.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<OrganizationTree> OrganizationTree => Set<OrganizationTree>();
        public DbSet<Vacancy>          Vacancies         => Set<Vacancy>();
        public DbSet<Staff>            Staff             => Set<Staff>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── OrganizationTree (existing table) ─────────────────────
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

            // ── Vacancy ───────────────────────────────────────────────
            builder.Entity<Vacancy>(e =>
            {
                e.ToTable("Vacancies");
                e.HasKey(x => x.VacancyId);
                e.Property(x => x.VacancyId).HasDefaultValueSql("NEWID()");
                e.Property(x => x.IsFilled).HasDefaultValue(false);
                e.Property(x => x.CreatedDate).HasDefaultValueSql("GETDATE()");

                e.HasOne(x => x.Organization)
                 .WithMany()
                 .HasForeignKey(x => x.OrganizationId)
                 .OnDelete(DeleteBehavior.Restrict);

                // One vacancy → one staff (1:1)
                e.HasOne(x => x.Staff)
                 .WithOne(x => x.Vacancy)
                 .HasForeignKey<Staff>(x => x.VacancyId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ── Staff ─────────────────────────────────────────────────
            builder.Entity<Staff>(e =>
            {
                e.ToTable("Staff");
                e.HasKey(x => x.StaffId);
                e.Property(x => x.StaffId).HasDefaultValueSql("NEWID()");
                e.Property(x => x.JoiningDate).HasDefaultValueSql("GETDATE()");
                e.Property(x => x.PhotoUrl).HasMaxLength(500).IsRequired(false);
                // UNIQUE constraint: one vacancy = one employee
                e.HasIndex(x => x.VacancyId).IsUnique();
            });
        }
    }
}
