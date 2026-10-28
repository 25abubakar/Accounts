using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class AssessmentCycleCheck(BusinessReadConnection business) : IModuleJob
{
    public ModuleJobInfo Info => new("Assessment.Check","Assessment","Assessment cycle check",true,
        "Read-only cycle and pending-entry check. Does not create assessments, change ratings or run legacy global timers.");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {
        await using var connection=await business.OpenAsync(ct);
        var today=DateOnly.FromDateTime(ScheduleTiming.LocalNow);
        int openDay,closeDay;
        await using(var rule=connection.CreateCommand())
        {
            rule.CommandText="SELECT TOP(1) OpenDay,CloseDay FROM dbo.AssessmentBonusRules WHERE TenantId=@tenant AND IsActive=1 ORDER BY Id DESC";
            rule.Parameters.AddWithValue("@tenant",tenantId);
            await using var reader=await rule.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))return "No active assessment rule is configured for this tenant. Read only.";
            openDay=Convert.ToInt32(reader[0]);closeDay=Convert.ToInt32(reader[1]);
        }
        var month=new DateOnly(today.Year,today.Month,1);
        foreach(var period in new[]{month,month.AddMonths(-1)})
        {
            var open=openDay;var close=closeDay;
            await using(var schedule=connection.CreateCommand())
            {
                schedule.CommandText="SELECT TOP(1) OpenDay,CloseDay FROM dbo.AssessmentSchedules WHERE TenantId=@tenant AND AssessmentYear=@year AND AssessmentMonth=@month AND IsActive=1 ORDER BY Id DESC";
                schedule.Parameters.AddWithValue("@tenant",tenantId);schedule.Parameters.AddWithValue("@year",period.Year);schedule.Parameters.AddWithValue("@month",period.Month);
                await using var scheduleReader=await schedule.ExecuteReaderAsync(ct);
                if(await scheduleReader.ReadAsync(ct)){open=Convert.ToInt32(scheduleReader[0]);close=Convert.ToInt32(scheduleReader[1]);}
            }
            var start=period.AddDays(Math.Min(open,DateTime.DaysInMonth(period.Year,period.Month))-1);
            var next=period.AddMonths(1);
            var end=next.AddDays(Math.Min(close,DateTime.DaysInMonth(next.Year,next.Month))-1);
            if(today<start || today>end)continue;
            await using var count=connection.CreateCommand();
            count.CommandText="SELECT COUNT(*) AS Entries,COALESCE(SUM(CASE WHEN Rating IS NULL THEN 1 ELSE 0 END),0) AS Pending FROM dbo.StaffAssessments WHERE TenantId=@tenant AND AssessmentYear=@year AND AssessmentMonth=@month";
            count.Parameters.AddWithValue("@tenant",tenantId);count.Parameters.AddWithValue("@year",period.Year);count.Parameters.AddWithValue("@month",period.Month);
            await using var reader=await count.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);
            return $"{period:MMMM yyyy}: cycle {start:dd MMM}–{end:dd MMM yyyy}; {reader.GetInt32(0)} entries, {reader.GetInt32(1)} pending. Read only; ratings and amounts unchanged.";
        }
        return "The assessment entry window is currently closed. Read only; no assessment data changed.";
    }
}
