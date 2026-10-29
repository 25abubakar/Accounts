using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundFaxModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaxRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    FaxNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NormalizedFaxNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaxRecipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FaxTransmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PatientExternalId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PatientDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppointmentExternalId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RecipientOrganization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientFaxNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderFaxId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResubmissionOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttachmentCount = table.Column<int>(type: "int", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaxTransmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaxTransmissions_FaxRecipients_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "FaxRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FaxAttachmentMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    TransmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaxAttachmentMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaxAttachmentMetadata_FaxTransmissions_TransmissionId",
                        column: x => x.TransmissionId,
                        principalTable: "FaxTransmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaxStatusEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    TransmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderEventReference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaxStatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaxStatusEvents_FaxTransmissions_TransmissionId",
                        column: x => x.TransmissionId,
                        principalTable: "FaxTransmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaxAttachmentMetadata_TransmissionId",
                table: "FaxAttachmentMetadata",
                column: "TransmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FaxRecipients_TenantId_NormalizedFaxNumber",
                table: "FaxRecipients",
                columns: new[] { "TenantId", "NormalizedFaxNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaxStatusEvents_TenantId_ProviderEventReference",
                table: "FaxStatusEvents",
                columns: new[] { "TenantId", "ProviderEventReference" },
                unique: true,
                filter: "[ProviderEventReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FaxStatusEvents_TransmissionId",
                table: "FaxStatusEvents",
                column: "TransmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FaxTransmissions_Provider_ProviderFaxId",
                table: "FaxTransmissions",
                columns: new[] { "Provider", "ProviderFaxId" },
                unique: true,
                filter: "[ProviderFaxId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FaxTransmissions_RecipientId",
                table: "FaxTransmissions",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_FaxTransmissions_TenantId_IdempotencyKey",
                table: "FaxTransmissions",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(
                """
                DECLARE @fax int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route = N'/email/fax' ORDER BY Id);
                IF @fax IS NOT NULL
                BEGIN
                    DECLARE @add nvarchar(100) = CONCAT(N'MENU_', @fax, N'_ADD');
                    DECLARE @edit nvarchar(100) = CONCAT(N'MENU_', @fax, N'_EDIT');
                    DECLARE @process nvarchar(100) = CONCAT(N'MENU_', @fax, N'_PROCESS');

                    INSERT dbo.Features(FeatureKey, FeatureName, Module, Description, CreatedDate)
                    SELECT source.FeatureKey, source.FeatureName, N'Menu', source.Description, SYSUTCDATETIME()
                    FROM (VALUES
                        (@add, N'Fax - Send', N'Preview and submit an outbound fax.'),
                        (@edit, N'Fax - Manage Recipients', N'Create and maintain the fax recipient directory.'),
                        (@process, N'Fax - Reconcile', N'Reconcile an outbound fax with its provider.'))
                        source(FeatureKey, FeatureName, Description)
                    WHERE NOT EXISTS (SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey = source.FeatureKey);

                    INSERT dbo.MenuPermissions(MenuId, PermissionId)
                    SELECT @fax, feature.PermissionId
                    FROM dbo.Features feature
                    WHERE feature.FeatureKey IN (@add, @edit, @process)
                      AND NOT EXISTS (
                          SELECT 1 FROM dbo.MenuPermissions existing
                          WHERE existing.MenuId = @fax AND existing.PermissionId = feature.PermissionId);

                    DECLARE @parent int = (SELECT ParentId FROM dbo.Menus WHERE Id = @fax);
                    UPDATE faxGrant
                    SET CanAdd = CASE WHEN parentGrant.CanAdd = 1 THEN 1 ELSE faxGrant.CanAdd END,
                        CanEdit = CASE WHEN parentGrant.CanEdit = 1 THEN 1 ELSE faxGrant.CanEdit END
                    FROM dbo.TenantMenuPermissions faxGrant
                    INNER JOIN dbo.TenantMenuPermissions parentGrant
                        ON parentGrant.TenantId = faxGrant.TenantId AND parentGrant.MenuId = @parent
                    WHERE faxGrant.MenuId = @fax;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaxAttachmentMetadata");

            migrationBuilder.DropTable(
                name: "FaxStatusEvents");

            migrationBuilder.DropTable(
                name: "FaxTransmissions");

            migrationBuilder.DropTable(
                name: "FaxRecipients");
        }
    }
}
