using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class UserSessionService : IUserSessionService
    {
        private readonly ApplicationDbContext _db;
        private readonly RbacService          _rbac;
        private readonly IAppNoteService      _notes;

        public UserSessionService(
            ApplicationDbContext db,
            RbacService rbac,
            IAppNoteService notes)
        {
            _db    = db;
            _rbac  = rbac;
            _notes = notes;
        }

        public async Task<UserSessionDto> GetSessionAsync(
            string identityUserId,
            bool isFullAccess,
            CancellationToken cancellationToken = default)
        {
            var session = new UserSessionDto
            {
                IsFullAccess   = isFullAccess,
                IdentityUserId = identityUserId
            };

            if (isFullAccess)
            {
                // SuperAdmin / Admin sees every menu and every feature key
                session.Sidebar     = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                session.Permissions = await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey)
                    .ToListAsync(cancellationToken);

                var adminPerson = await _db.Persons.AsNoTracking()
                    .Include(p => p.Staff)
                    .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, cancellationToken);

                session.StaffId  = adminPerson?.Staff?.StaffId;
                session.PersonId = adminPerson?.PersonId;

                var adminStaffId = session.StaffId?.ToString() ?? identityUserId;
                session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                    adminStaffId, identityUserId, cancellationToken);
                session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);
                return session;
            }

            // Regular user — resolve via 3-layer RBAC (RolePermissions → UserOverrides → deny)
            var person = await _db.Persons.AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, cancellationToken);

            if (person == null)
            {
                session.Sidebar     = new List<object>();
                session.Permissions = new List<string>();
            }
            else
            {
                session.PersonId = person.PersonId;
                session.StaffId  = person.Staff?.StaffId;

                if (person.Staff != null)
                {
                    session.Sidebar     = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
                    session.Permissions = (await _rbac.GetEffectivePermissionsAsync(person.Staff.StaffId)).ToList();
                }
                else
                {
                    // Person exists but has no staff record — no permissions yet
                    session.Sidebar     = new List<object>();
                    session.Permissions = new List<string>();
                }
            }

            var noteStaffId = session.StaffId?.ToString() ?? identityUserId;
            session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                noteStaffId, identityUserId, cancellationToken);
            session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);

            return session;
        }
    }
}
