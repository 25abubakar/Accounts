using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [Migration("20260603160000_NormalizeMenuRoutes")]
    public partial class NormalizeMenuRoutes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE dbo.Menus SET Route = '/access/groups' WHERE Route IN ('/ACCESS/GROUPS', '/Access/Groups', '/access/group');
UPDATE dbo.Menus SET Route = '/organization'   WHERE Route = '/groups/hierarchy';
UPDATE dbo.Menus SET Route = '/hr/vacancies'   WHERE Route IN ('/groups/registration', '/hr/positions');
UPDATE dbo.Menus SET Route = '/hr/staff'       WHERE Route = '/groups/staff';
UPDATE dbo.Menus SET Route = '/hr/staff/register' WHERE Route = '/staff/register';
UPDATE dbo.Menus SET Route = LOWER(Route) WHERE Route IS NOT NULL AND Route <> LOWER(Route);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Non-reversible — routes were corrected to match frontend.
        }
    }
}
