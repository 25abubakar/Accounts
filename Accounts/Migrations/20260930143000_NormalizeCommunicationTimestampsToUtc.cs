using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Reverses the historical Pakistan wall-clock conversion for communication
/// timestamps. These columns are explicitly UTC and are shared across users in
/// different countries, so keeping one local wall time is ambiguous.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930143000_NormalizeCommunicationTimestampsToUtc")]
public sealed class NormalizeCommunicationTimestampsToUtc : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AccountsDataFixMarkers', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsDataFixMarkers
                (
                    MarkerKey nvarchar(150) NOT NULL CONSTRAINT PK_AccountsDataFixMarkers PRIMARY KEY,
                    AppliedOn datetime2 NOT NULL CONSTRAINT DF_AccountsDataFixMarkers_AppliedOn DEFAULT(SYSUTCDATETIME())
                );
            END;

            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.AccountsDataFixMarkers
                WHERE MarkerKey = N'20260930143000_NormalizeCommunicationTimestampsToUtc'
            )
            BEGIN
                IF OBJECT_ID(N'dbo.ApplicationLoginSessions', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.ApplicationLoginSessions
                    SET LoginUtc = CONVERT(datetime2, (LoginUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC'),
                        LogoutUtc = CASE WHEN LogoutUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (LogoutUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                        CreatedDate = CONVERT(datetime2, (CreatedDate AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC'),
                        ModifiedDate = CASE WHEN ModifiedDate IS NULL THEN NULL ELSE CONVERT(datetime2, (ModifiedDate AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END;
                END;

                IF OBJECT_ID(N'dbo.AppNotes', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.AppNotes
                    SET CreatedOnUtc = CONVERT(datetime2, (CreatedOnUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC'),
                        UpdatedOnUtc = CASE WHEN UpdatedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (UpdatedOnUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                        DeletedOnUtc = CASE WHEN DeletedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (DeletedOnUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                        StartDateUtc = CASE WHEN StartDateUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (StartDateUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                        EndDateUtc = CASE WHEN EndDateUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (EndDateUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END;
                END;

                IF OBJECT_ID(N'dbo.AppNoteUserStates', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.AppNoteUserStates
                    SET ReadOnUtc = CASE WHEN ReadOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (ReadOnUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                        AcknowledgedOnUtc = CASE WHEN AcknowledgedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (AcknowledgedOnUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                        DismissedOnUtc = CASE WHEN DismissedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (DismissedOnUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END;
                END;

                INSERT dbo.AccountsDataFixMarkers(MarkerKey)
                VALUES (N'20260930143000_NormalizeCommunicationTimestampsToUtc');
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Timestamp normalization is intentionally irreversible. Reintroducing
        // a shared local wall clock would make multi-country records ambiguous.
    }
}
