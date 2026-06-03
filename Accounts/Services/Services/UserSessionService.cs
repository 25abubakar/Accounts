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
                IsFullAccess    = isFullAccess,
                IdentityUserId  = identityUserId
            };

            if (isFullAccess)
            {
                session.Sidebar = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                var allKeys = await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey).ToListAsync(cancellationToken);
                session.Permissions = allKeys;
            }
            else
            {
                var person = await _db.Persons
                    .AsNoTracking()
                    .Include(p => p.Staff)
                    .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, cancellationToken);

                if (person?.Staff != null)
                {
                    session.StaffId     = person.Staff.StaffId;
                    session.Sidebar     = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
                    session.Permissions = (await _rbac.GetEffectivePermissionsAsync(person.Staff.StaffId)).ToList();
                }
            }

            var staffId = session.StaffId?.ToString() ?? identityUserId;
            session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                staffId, identityUserId, cancellationToken);
            session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);

            return session;
        }
    }
}
