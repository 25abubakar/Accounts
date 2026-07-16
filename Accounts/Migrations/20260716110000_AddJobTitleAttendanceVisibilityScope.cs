using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716110000_AddJobTitleAttendanceVisibilityScope")]
public sealed class AddJobTitleAttendanceVisibilityScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AttendanceVisibilityScope",
            table: "JobTitles",
            type: "int",
            nullable: false,
            defaultValue: 0);

        // Initial classification for existing titles. Tenant Admin can change
        // every value from Job Titles; attendance authorization never reads text.
        migrationBuilder.Sql(
            """
            UPDATE [JobTitles]
            SET [AttendanceVisibilityScope] = CASE
                WHEN LOWER([TitleName]) LIKE N'%ceo%'
                  OR LOWER([TitleName]) LIKE N'%chief%'
                  OR LOWER([TitleName]) LIKE N'%director%'
                  OR LOWER([TitleName]) LIKE N'%head%'
                  OR LOWER([TitleName]) LIKE N'%manager%' THEN 2
                WHEN LOWER([TitleName]) LIKE N'%supervisor%'
                  OR LOWER([TitleName]) LIKE N'%team lead%' THEN 1
                ELSE 0
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AttendanceVisibilityScope", table: "JobTitles");
    }
}
