using Accounts.Data;
using Accounts.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Accounts.Tests.Helpers
{
    /// <summary>
    /// Creates a fresh in-memory ApplicationDbContext for each test.
    /// Each test gets its own isolated database — no shared state between tests.
    /// </summary>
    public static class TestDbFactory
    {
        /// <summary>
        /// Creates a new in-memory DbContext with a unique database name.
        /// Call this once per test method to guarantee isolation.
        /// </summary>
        public static ApplicationDbContext Create(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var db = new ApplicationDbContext(options);
            db.Database.EnsureCreated();
            return db;
        }

        // ── Seed Helpers ──────────────────────────────────────────────────────

        /// <summary>Seeds a Feature row. FeatureKey is the PK.</summary>
        public static Feature SeedFeature(
            ApplicationDbContext db,
            string featureKey,
            string featureName = "Test Feature",
            string module      = "TestModule")
        {
            var f = new Feature
            {
                FeatureKey  = featureKey,
                FeatureName = featureName,
                Module      = module
            };
            db.Features.Add(f);
            db.SaveChanges();
            return f;
        }

        /// <summary>Seeds multiple features at once.</summary>
        public static List<Feature> SeedFeatures(
            ApplicationDbContext db,
            params string[] featureKeys)
        {
            var features = featureKeys.Select(k => new Feature
            {
                FeatureKey  = k,
                FeatureName = k.Replace("_", " "),
                Module      = k.Split('_')[0]
            }).ToList();

            db.Features.AddRange(features);
            db.SaveChanges();
            return features;
        }

        /// <summary>Seeds a Staff row with a linked Vacancy and OrgNode.</summary>
        public static (Staff Staff, Vacancy Vacancy, OrganizationTree OrgNode) SeedStaff(
            ApplicationDbContext db,
            string fullName  = "Test Employee",
            string jobTitle  = "Developer",
            int    orgNodeId = 1)
        {
            // Ensure org node exists
            if (!db.OrganizationTree.Any(n => n.Id == orgNodeId))
            {
                db.OrganizationTree.Add(new OrganizationTree
                {
                    Id    = orgNodeId,
                    Name  = "Test Branch",
                    Label = "Branch"
                });
                db.SaveChanges();
            }

            var vacancy = new Vacancy
            {
                VacancyId      = Guid.NewGuid(),
                OrganizationId = orgNodeId,
                VacancyCode    = $"TEST-{Guid.NewGuid().ToString("N").Substring(0, 4)}",
                JobTitle       = jobTitle,
                IsFilled       = true
            };
            db.Vacancies.Add(vacancy);
            db.SaveChanges();

            var staff = new Staff
            {
                StaffId     = Guid.NewGuid(),
                FullName    = fullName,
                VacancyId   = vacancy.VacancyId,
                JoiningDate = DateTime.Now
            };
            db.Staff.Add(staff);
            db.SaveChanges();

            var orgNode = db.OrganizationTree.Find(orgNodeId)!;
            return (staff, vacancy, orgNode);
        }

        /// <summary>Seeds an AccessGroup.</summary>
        public static AccessGroup SeedGroup(
            ApplicationDbContext db,
            string groupName    = "Test Group",
            string? description = null)
        {
            var group = new AccessGroup
            {
                GroupName   = groupName,
                Description = description,
                IsActive    = true,
                CreatedDate = DateTime.Now
            };
            db.AccessGroups.Add(group);
            db.SaveChanges();
            return group;
        }

        /// <summary>Assigns feature keys to a group.</summary>
        public static void SeedGroupFeatures(
            ApplicationDbContext db,
            int groupId,
            params string[] featureKeys)
        {
            foreach (var key in featureKeys)
                db.AccessGroupFeatures.Add(new AccessGroupFeature
                {
                    GroupId    = groupId,
                    FeatureKey = key
                });
            db.SaveChanges();
        }

        /// <summary>Assigns a staff member to a group.</summary>
        public static void SeedStaffGroup(
            ApplicationDbContext db,
            Guid staffId,
            int  groupId)
        {
            db.StaffAccessGroups.Add(new StaffAccessGroup
            {
                StaffId      = staffId,
                GroupId      = groupId,
                AssignedDate = DateTime.Now
            });
            db.SaveChanges();
        }

        /// <summary>Seeds a DepartmentAccessMatrix row for a staff member.</summary>
        public static DepartmentAccessMatrix SeedMatrixRow(
            ApplicationDbContext db,
            Guid   staffId,
            int    deptId,
            string featureKey,
            bool   hasAccess  = true,
            string? grantedBy = null)
        {
            var row = new DepartmentAccessMatrix
            {
                StaffId     = staffId,
                DeptId      = deptId,
                FeatureKey  = featureKey,
                HasAccess   = hasAccess,
                GrantedBy   = grantedBy,
                GrantedDate = DateTime.Now
            };
            db.DepartmentAccessMatrix.Add(row);
            db.SaveChanges();
            return row;
        }
    }
}
