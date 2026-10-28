using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class AttendanceStatusCheck(BusinessReadConnection business) : IModuleJob
{
    public ModuleJobInfo Info => new("Attendance.Check","Attendance","Attendance status check",true,
        "Read-only check. Existing Accounts attendance timers and calculations remain unchanged.");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {
        await using var connection=await business.OpenAsync(ct);
        await using var command=connection.CreateCommand();command.CommandTimeout=30;
        command.CommandText="""
            SELECT COUNT(*) AS Records,
                   COALESCE(SUM(CASE WHEN record.CheckInUtc IS NOT NULL AND record.CheckOutUtc IS NULL THEN 1 ELSE 0 END),0) AS OpenSessions,
                   COALESCE(SUM(CASE WHEN record.CheckInUtc IS NOT NULL AND record.CheckOutUtc IS NOT NULL THEN 1 ELSE 0 END),0) AS Completed
            FROM dbo.AttendanceRecords record
            JOIN dbo.Persons person ON person.PersonId=record.PersonId AND person.TenantId=record.TenantId
            WHERE record.TenantId=@tenant AND record.AttendanceDate=@date AND person.IsActive=1;
            """;
        var date=DateOnly.FromDateTime(ScheduleTiming.LocalNow);
        command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@date",date.ToDateTime(TimeOnly.MinValue));
        await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);
        return $"{date:yyyy-MM-dd}: {reader.GetInt32(0)} attendance records, {reader.GetInt32(1)} open sessions, {reader.GetInt32(2)} completed. Read only; no statuses/punches changed.";
    }
}
