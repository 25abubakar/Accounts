using Microsoft.EntityFrameworkCore;

namespace Accounts.Data;

/// <summary>Idempotent compatibility guard for deployments that started before the latest EF migration ran.</summary>
public static class AssessmentSchema
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _ready;

    public static async Task EnsureCurrentAsync(ApplicationDbContext db)
    {
        if (_ready || !db.Database.IsSqlServer()) return;
        await Gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_ready) return;
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.AssessmentBonusRules', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.AssessmentBonusRules', N'DecrementAmount') IS NULL
                    ALTER TABLE dbo.AssessmentBonusRules
                    ADD DecrementAmount decimal(18,2) NOT NULL
                        CONSTRAINT DF_AssessmentBonusRules_Decrement DEFAULT(0);
                """, CancellationToken.None);

            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.StaffAssessments', N'Amount') IS NULL
                    ALTER TABLE dbo.StaffAssessments ADD Amount decimal(18,2) NULL;
                """, CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("""
                IF EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.StaffAssessments')
                      AND name = N'UX_StaffAssessments_UniqueMonthlyRank'
                )
                    DROP INDEX UX_StaffAssessments_UniqueMonthlyRank ON dbo.StaffAssessments;
                """, CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.StaffAssessments', N'Rating') IS NOT NULL
                    ALTER TABLE dbo.StaffAssessments ALTER COLUMN Rating tinyint NULL;
                """, CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.StaffAssessments', N'Remarks') IS NOT NULL
                    ALTER TABLE dbo.StaffAssessments DROP COLUMN Remarks;
                """, CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.StaffAssessments', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.indexes
                       WHERE object_id = OBJECT_ID(N'dbo.StaffAssessments')
                         AND name = N'UX_StaffAssessments_UniqueMonthlyRank'
                   )
                    CREATE UNIQUE INDEX UX_StaffAssessments_UniqueMonthlyRank
                        ON dbo.StaffAssessments(TenantId,AssessorPersonId,AssessmentYear,AssessmentMonth,Rating)
                        WHERE Rating IS NOT NULL;
                """, CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.AssessmentSchedules',N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AssessmentSchedules
                    (
                        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssessmentSchedules PRIMARY KEY,
                        TenantId int NOT NULL, AssessmentYear int NOT NULL, AssessmentMonth tinyint NOT NULL,
                        OpenDay tinyint NOT NULL CONSTRAINT DF_AssessmentSchedules_OpenDay DEFAULT(25),
                        IsManualOverride bit NOT NULL CONSTRAINT DF_AssessmentSchedules_Manual DEFAULT(0),
                        IsActive bit NOT NULL CONSTRAINT DF_AssessmentSchedules_Active DEFAULT(1),
                        CreatedDateUtc datetime2 NOT NULL CONSTRAINT DF_AssessmentSchedules_Created DEFAULT SYSUTCDATETIME(),
                        CreatedByUserId nvarchar(450) NULL,
                        CONSTRAINT FK_AssessmentSchedules_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                        CONSTRAINT CK_AssessmentSchedules_Month CHECK(AssessmentMonth BETWEEN 1 AND 12),
                        CONSTRAINT CK_AssessmentSchedules_OpenDay CHECK(OpenDay BETWEEN 1 AND 31),
                        CONSTRAINT UQ_AssessmentSchedules_Period UNIQUE(TenantId,AssessmentYear,AssessmentMonth)
                    );
                END
                """, CancellationToken.None);

            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.AssessmentBonusRules', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.AssessmentBonusRules', N'MinimumBonusAmount') IS NULL
                    ALTER TABLE dbo.AssessmentBonusRules
                    ADD MinimumBonusAmount decimal(18,2) NOT NULL
                        CONSTRAINT DF_AssessmentBonusRules_Minimum DEFAULT(0);
                """, CancellationToken.None);

            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.AssessmentBonusRules', N'U') IS NOT NULL
                BEGIN
                    ;WITH duplicates AS
                    (
                        SELECT Id, ROW_NUMBER() OVER
                            (PARTITION BY TenantId ORDER BY CASE WHEN RankNumber=1 THEN 0 ELSE 1 END, Id) AS rn
                        FROM dbo.AssessmentBonusRules
                    )
                    DELETE FROM duplicates WHERE rn > 1;

                    UPDATE dbo.AssessmentBonusRules
                    SET RankNumber=1, AppliesToHigherRanks=1,
                        MinimumBonusAmount=CASE
                            WHEN MinimumBonusAmount=0 THEN BonusAmount
                            ELSE MinimumBonusAmount END;
                END
                """, CancellationToken.None);
            _ready = true;
        }
        finally { Gate.Release(); }
    }
}
