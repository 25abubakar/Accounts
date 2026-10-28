using Accounts.Models.Scheduler;
using Microsoft.Data.SqlClient;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerCalendar(SchedulerStorage storage)
{
    public async Task<DateTime?> FirstDueAsync(ScheduleDefinition value,DateTime now,CancellationToken ct)
    {
        var first=await NextAfterAsync(value,ScheduleTiming.StartUtc(value).AddTicks(-1),ct);
        return first<=now&&value.MisfirePolicy=="Skip"?await NextAfterAsync(value,now,ct):first;
    }
    public async Task<DateTime?> NextAfterAsync(ScheduleDefinition value,DateTime after,CancellationToken ct)
    {
        if(value.ManagedKey!="assessment-monthly"||value.Mode!="Monthly")return ScheduleTiming.NextAfter(value,after);
        await using var connection=new SqlConnection(storage.BusinessConnection);await connection.OpenAsync(ct);
        await using var rule=connection.CreateCommand();
        rule.CommandText="SELECT TOP(1) OpenDay FROM dbo.AssessmentBonusRules WHERE TenantId=@tenant AND IsActive=1 ORDER BY Id";
        rule.Parameters.AddWithValue("@tenant",value.TenantId);
        var result=await rule.ExecuteScalarAsync(ct);
        if(result==null||result==DBNull.Value)return null;
        var openDay=Convert.ToInt32(result);
        var overrides=new Dictionary<(int,int),int>();
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT AssessmentYear,AssessmentMonth,OpenDay FROM dbo.AssessmentSchedules WHERE TenantId=@tenant AND IsActive=1";
        command.Parameters.AddWithValue("@tenant",value.TenantId);
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct))overrides[(reader.GetInt32(0),Convert.ToInt32(reader.GetValue(1)))]=Convert.ToInt32(reader.GetValue(2));
        var anchor=ScheduleTiming.StartUtc(value);
        var lower=after>=anchor?after:anchor.AddTicks(-1);
        var local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(lower,DateTimeKind.Utc),ScheduleTiming.Pakistan);
        var month=new DateTime(local.Year,local.Month,1);
        var time=TimeOnly.ParseExact(value.Time,"HH:mm");
        for(var i=0;i<14;i++,month=month.AddMonths(1))
        {
            var day=overrides.GetValueOrDefault((month.Year,month.Month),openDay);
            day=Math.Clamp(day,1,DateTime.DaysInMonth(month.Year,month.Month));
            var candidate=TimeZoneInfo.ConvertTimeToUtc(month.AddDays(day-1).Add(time.ToTimeSpan()),ScheduleTiming.Pakistan);
            if(candidate>lower)return candidate;
        }
        return null;
    }
}
