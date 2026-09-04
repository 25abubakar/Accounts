using Microsoft.EntityFrameworkCore;

namespace Accounts.Data;

public static class ApplicationLoginSessionSchema
{
    private const string MigrationId = "20260722123000_AddApplicationLoginSessions";
    private static readonly SemaphoreSlim LocalGate = new(1, 1);

    // Once the schema is verified for this process lifetime, skip the expensive SQL on every request.
    private static volatile bool _ensured;

    public static async Task EnsureCreatedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        // Fast path: already verified in this process — no DB round-trip.
        if (_ensured) return;

        await LocalGate.WaitAsync(ct);
        try
        {
            // Double-checked locking: another thread may have set the flag while we waited.
            if (_ensured) return;

            await db.Database.ExecuteSqlRawAsync(
                $$"""
                DECLARE @lockResult int;
                EXEC @lockResult = sp_getapplock
                    @Resource = N'Accounts.ApplicationLoginSessions.Schema',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 15000;

                IF @lockResult < 0
                    THROW 51000, N'Could not acquire ApplicationLoginSessions schema lock.', 1;

                BEGIN TRY
                    IF OBJECT_ID(N'[dbo].[ApplicationLoginSessions]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[ApplicationLoginSessions](
                            [Id] [bigint] IDENTITY(1,1) NOT NULL,
                            [TenantId] [int] NOT NULL,
                            [StaffId] [uniqueidentifier] NULL,
                            [PersonId] [uniqueidentifier] NULL,
                            [IdentityUserId] [nvarchar](450) NOT NULL,
                            [SessionDate] [date] NOT NULL,
                            [LoginUtc] [datetime2] NOT NULL,
                            [LogoutUtc] [datetime2] NULL,
                            [WorkingMinutes] [int] NOT NULL CONSTRAINT [DF_ApplicationLoginSessions_WorkingMinutes] DEFAULT(0),
                            [IpAddress] [nvarchar](45) NULL,
                            [UserAgent] [nvarchar](300) NULL,
                            [Source] [nvarchar](50) NOT NULL CONSTRAINT [DF_ApplicationLoginSessions_Source] DEFAULT(N'Software'),
                            [Remarks] [nvarchar](500) NULL,
                            [CreatedDate] [datetime2] NOT NULL CONSTRAINT [DF_ApplicationLoginSessions_CreatedDate] DEFAULT(SYSUTCDATETIME()),
                            [ModifiedDate] [datetime2] NULL,
                            CONSTRAINT [PK_ApplicationLoginSessions] PRIMARY KEY CLUSTERED ([Id] ASC)
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[ApplicationLoginSessions]', N'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ApplicationLoginSessions_IdentityUserId_LogoutUtc' AND [object_id] = OBJECT_ID(N'[dbo].[ApplicationLoginSessions]'))
                            CREATE INDEX [IX_ApplicationLoginSessions_IdentityUserId_LogoutUtc] ON [dbo].[ApplicationLoginSessions] ([IdentityUserId], [LogoutUtc]);

                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ApplicationLoginSessions_PersonId' AND [object_id] = OBJECT_ID(N'[dbo].[ApplicationLoginSessions]'))
                            CREATE INDEX [IX_ApplicationLoginSessions_PersonId] ON [dbo].[ApplicationLoginSessions] ([PersonId]);

                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ApplicationLoginSessions_StaffId_SessionDate' AND [object_id] = OBJECT_ID(N'[dbo].[ApplicationLoginSessions]'))
                            CREATE INDEX [IX_ApplicationLoginSessions_StaffId_SessionDate] ON [dbo].[ApplicationLoginSessions] ([StaffId], [SessionDate]);

                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ApplicationLoginSessions_TenantId_SessionDate' AND [object_id] = OBJECT_ID(N'[dbo].[ApplicationLoginSessions]'))
                            CREATE INDEX [IX_ApplicationLoginSessions_TenantId_SessionDate] ON [dbo].[ApplicationLoginSessions] ([TenantId], [SessionDate]);

                        IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL
                           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ApplicationLoginSessions_AspNetUsers_IdentityUserId')
                            ALTER TABLE [dbo].[ApplicationLoginSessions] WITH NOCHECK ADD CONSTRAINT [FK_ApplicationLoginSessions_AspNetUsers_IdentityUserId]
                            FOREIGN KEY([IdentityUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE;

                        IF OBJECT_ID(N'[dbo].[Persons]', N'U') IS NOT NULL
                           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ApplicationLoginSessions_Persons_PersonId')
                            ALTER TABLE [dbo].[ApplicationLoginSessions] WITH NOCHECK ADD CONSTRAINT [FK_ApplicationLoginSessions_Persons_PersonId]
                            FOREIGN KEY([PersonId]) REFERENCES [dbo].[Persons] ([PersonId]) ON DELETE SET NULL;

                        IF OBJECT_ID(N'[dbo].[StaffVacancy]', N'U') IS NOT NULL
                           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ApplicationLoginSessions_StaffVacancy_StaffId')
                            ALTER TABLE [dbo].[ApplicationLoginSessions] WITH NOCHECK ADD CONSTRAINT [FK_ApplicationLoginSessions_StaffVacancy_StaffId]
                            FOREIGN KEY([StaffId]) REFERENCES [dbo].[StaffVacancy] ([StaffId]) ON DELETE SET NULL;
                    END;

                    IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{{MigrationId}}')
                        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                        VALUES (N'{{MigrationId}}', N'9.0.5');

                    EXEC sp_releaseapplock
                        @Resource = N'Accounts.ApplicationLoginSessions.Schema',
                        @LockOwner = N'Session';
                END TRY
                BEGIN CATCH
                    EXEC sp_releaseapplock
                        @Resource = N'Accounts.ApplicationLoginSessions.Schema',
                        @LockOwner = N'Session';
                    THROW;
                END CATCH;
                """,
                ct);

            // Mark as done for the lifetime of this process so subsequent calls return immediately.
            _ensured = true;
        }
        finally
        {
            LocalGate.Release();
        }
    }
}
