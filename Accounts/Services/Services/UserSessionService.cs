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
        private readonly IPersonAccessService _personAccess;
        private readonly IAppNoteService      _notes;

        public UserSessionService(
            ApplicationDbContext db,
            RbacService rbac,
            IPersonAccessService personAccess,
            IAppNoteService notes)
        {
            _db            = db;
            _rbac          = rbac;
            _personAccess  = personAccess;
            _notes         = notes;
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
                session.Sidebar     = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                session.Permissions = await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey).ToListAsync(cancellationToken);

                var staffForAdmin = await _db.Persons.AsNoTracking()
                    .Include(p => p.Staff)
                    .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, cancellationToken);
                session.StaffId  = staffForAdmin?.Staff?.StaffId;
                session.PersonId = staffForAdmin?.PersonId;

                var adminStaffId = session.StaffId?.ToString() ?? identityUserId;
                session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                    adminStaffId, identityUserId, cancellationToken);
                session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);
                return session;
            }

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

                // Primary: direct PersonMenus + PersonFeatures (admin grants)
                var hasDirectGrants = await _personAccess.HasPersonGrantsAsync(person.PersonId, cancellationToken);

                if (hasDirectGrants)
                {
                    session.Sidebar     = await _personAccess.GetGrantedSidebarAsync(person.PersonId, cancellationToken);
                    session.Permissions = (await _personAccess.GetGrantedFeatureKeysAsync(person.PersonId, cancellationToken)).ToList();
                }
                else if (person.Staff != null)
                {
                    // Fallback: legacy staff-based RBAC (matrix, groups, overrides)
                    session.Sidebar     = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
                    session.Permissions = (await _rbac.GetEffectivePermissionsAsync(person.Staff.StaffId)).ToList();
                }
                else
                {
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
