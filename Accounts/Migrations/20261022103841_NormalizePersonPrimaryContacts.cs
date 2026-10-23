using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePersonPrimaryContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve every nonblank legacy value. Existing normalized primary contacts win;
            // a different legacy value is retained as an additional, non-primary contact.
            migrationBuilder.Sql("""
                INSERT dbo.PersonContacts(PersonId,ContactType,ContactValue,IsPrimary,CreatedDate)
                SELECT p.PersonId,v.ContactType,LTRIM(RTRIM(v.ContactValue)),
                    CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.PersonContacts c WHERE c.PersonId=p.PersonId
                        AND c.ContactType=v.ContactType AND c.IsPrimary=1) THEN 0 ELSE 1 END AS bit),SYSUTCDATETIME()
                FROM dbo.Persons p CROSS APPLY(VALUES(N'Email',p.Email),(N'Phone',p.Phone)) v(ContactType,ContactValue)
                WHERE NULLIF(LTRIM(RTRIM(v.ContactValue)),N'') IS NOT NULL
                    AND NOT EXISTS(SELECT 1 FROM dbo.PersonContacts c WHERE c.PersonId=p.PersonId
                        AND c.ContactType=v.ContactType AND c.ContactValue=LTRIM(RTRIM(v.ContactValue)));
                IF EXISTS(SELECT 1 FROM dbo.Persons p
                    CROSS APPLY(VALUES(N'Email',p.Email),(N'Phone',p.Phone)) v(ContactType,ContactValue)
                    WHERE NULLIF(LTRIM(RTRIM(v.ContactValue)),N'') IS NOT NULL AND NOT EXISTS(
                        SELECT 1 FROM dbo.PersonContacts c WHERE c.PersonId=p.PersonId
                        AND c.ContactType=v.ContactType AND c.ContactValue=LTRIM(RTRIM(v.ContactValue))))
                    THROW 51000,'Legacy person contact values were not preserved; refusing column removal.',1;
                """);
            foreach (var sql in NormalizedSql) migrationBuilder.Sql(sql);
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Persons");

            migrationBuilder.CreateIndex(
                name: "IX_PersonContacts_PersonId_ContactType_IsPrimary_Id",
                table: "PersonContacts",
                columns: new[] { "PersonId", "ContactType", "IsPrimary", "Id" })
                .Annotation("SqlServer:Include", new[] { "ContactValue" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PersonContacts_PersonId_ContactType_IsPrimary_Id",
                table: "PersonContacts");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Persons",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Persons",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
            migrationBuilder.Sql("""
                IF EXISTS(SELECT 1 FROM dbo.vw_PersonPrimaryContacts WHERE LEN(Phone)>50 OR LEN(Email)>150)
                    THROW 51000,'Contacts exceed legacy lengths; export them before rollback.',1;
                UPDATE p SET Phone=c.Phone,Email=c.Email
                FROM dbo.Persons p LEFT JOIN dbo.vw_PersonPrimaryContacts c ON c.PersonId=p.PersonId;
                """);
            foreach (var sql in LegacySql) migrationBuilder.Sql(sql);
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Hr_PersonHrProfile_Get; DROP VIEW IF EXISTS dbo.vw_PersonPrimaryContacts;");
        }
    }
}
