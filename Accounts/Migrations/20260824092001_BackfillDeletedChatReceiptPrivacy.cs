using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDeletedChatReceiptPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE ChatMessages
SET DeliveryTrackingClearedOnUtc = DeletedOnUtc
WHERE DeletedOnUtc IS NOT NULL
  AND DeliveryTrackingClearedOnUtc IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Privacy erasure is intentionally irreversible.
        }
    }
}
