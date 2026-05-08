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

        public DbSet<OrganizationTree> OrganizationTree   => Set<OrganizationTree>();
        public DbSet<Vacancy>          Vacancies           => Set<Vacancy>();
        public DbSet<Staff>            Staff               => Set<Staff>();
        public DbSet<Person>           Persons             => Set<Person>();
        public DbSet<PersonAddress>    PersonAddresses     => Set<PersonAddress>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── OrganizationTree ──────────────────────────────────────
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

                // PersonId FK — SetNull so deleting a Person doesn't delete Staff
                e.HasOne(x => x.Person)
                 .WithOne(x => x.Staff)
                 .HasForeignKey<Staff>(x => x.PersonId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ── Person ────────────────────────────────────────────────
            builder.Entity<Person>(e =>
            {
                e.ToTable("Persons");
                e.HasKey(x => x.PersonId);
                e.Property(x => x.PersonId).HasDefaultValueSql("NEWID()");
                e.Property(x => x.CreatedDate).HasDefaultValueSql("GETDATE()");
                e.Property(x => x.LoginId).HasMaxLength(30).IsRequired();
                e.Property(x => x.IdentityUserId).HasMaxLength(450).IsRequired();
                e.Property(x => x.ProfilePhotoUrl).HasMaxLength(500).IsRequired(false);
                e.Property(x => x.BranchId).IsRequired(false);

                // Unique indexes
                e.HasIndex(x => x.LoginId).IsUnique();
                e.HasIndex(x => x.IdentityUserId).IsUnique();
            });

            // ── PersonAddress ─────────────────────────────────────────
            builder.Entity<PersonAddress>(e =>
            {
                e.ToTable("PersonAddresses");
                e.HasKey(x => x.AddressId);
                e.Property(x => x.AddressId).HasDefaultValueSql("NEWID()");
                e.Property(x => x.AddressType).HasMaxLength(20).IsRequired();

                // Unique: one Current + one Permanent per Person
                e.HasIndex(x => new { x.PersonId, x.AddressType }).IsUnique();

                // Cascade delete when Person is deleted
                e.HasOne(x => x.Person)
                 .WithMany(x => x.Addresses)
                 .HasForeignKey(x => x.PersonId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
