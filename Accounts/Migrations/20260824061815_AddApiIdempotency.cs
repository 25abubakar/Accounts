using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddApiIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Infrastructure");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "Infrastructure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    LockToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResponseHeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresUtc",
                schema: "Infrastructure",
                table: "IdempotencyRecords",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ScopeHash_IdempotencyKey",
                schema: "Infrastructure",
                table: "IdempotencyRecords",
                columns: new[] { "ScopeHash", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Status_LeaseExpiresUtc",
                schema: "Infrastructure",
                table: "IdempotencyRecords",
                columns: new[] { "Status", "LeaseExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "Infrastructure");
        }
    }
}
