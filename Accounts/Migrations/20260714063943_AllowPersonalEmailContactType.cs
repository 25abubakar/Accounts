using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AllowPersonalEmailContactType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PersonContacts_Type')
    ALTER TABLE dbo.PersonContacts DROP CONSTRAINT CK_PersonContacts_Type;

ALTER TABLE dbo.PersonContacts WITH CHECK ADD CONSTRAINT CK_PersonContacts_Type
    CHECK (ContactType IN ('Email','PersonalEmail','Phone','WhatsApp','Emergency','Other'));
ALTER TABLE dbo.PersonContacts CHECK CONSTRAINT CK_PersonContacts_Type;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PersonContacts_Type')
    ALTER TABLE dbo.PersonContacts DROP CONSTRAINT CK_PersonContacts_Type;

ALTER TABLE dbo.PersonContacts WITH CHECK ADD CONSTRAINT CK_PersonContacts_Type
    CHECK (ContactType IN ('Email','Phone','WhatsApp','Emergency','Other'));
ALTER TABLE dbo.PersonContacts CHECK CONSTRAINT CK_PersonContacts_Type;");
        }
    }
}
