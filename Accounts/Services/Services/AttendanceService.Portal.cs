using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed partial class AttendanceService
{
    private DateTime PortalNow(Person person) =>
        BusinessClock.ToLocal(_timeProvider.GetUtcNow().UtcDateTime, person.TimeZoneId);

    private static EffectiveAttendanceRule RequireApprovedPortalRule(EffectiveAttendanceRule? rule)
    {
        if (rule == null)
            throw new InvalidOperationException("Your attendance type and shift are not mapped. Ask an administrator to configure Map Attendance.");
        if (!rule.RuleSettingId.HasValue)
            throw new InvalidOperationException("Your mapped attendance rule must be active and approved in Attendance Rules before check-in/check-out.");
        return rule;
    }

    private Task RefreshPortalAttendanceAsync(Person person, DateOnly date, CancellationToken ct) =>
        _finalization.RefreshPeriodAsync(person.TenantId, date.Year, date.Month, [person.PersonId], ct);

    private sealed record PortalStatusChoice(string Name, int? ConfiguredActionStatusId = null);

    // Preserve the saved evaluator's grace, completion and late/early band decisions.
    // Only the status-ID namespace changes: master Settings status, not legacy FK IDs.
    private static PortalStatusChoice ResolvePortalStatus(
        AttendanceRecord record, EffectiveTiming timing, EffectiveAttendanceRule rule, bool expired)
    {
        if (expired) return new("Pending Review");
        if (rule.IsOpenAttendance) return new("Present");
        if (!timing.IsOn) return new(timing.HolidayType is HolidayCode or "ANNUAL_HOLIDAY" ? "Holiday" : "Day Off");
        var window = ResolveShiftWindow(record.AttendanceDate, timing.TimeFrom ?? rule.TimeFrom, timing.TimeTo ?? rule.TimeTo);
        var checkIn = record.CheckInUtc!.Value;
        var late = checkIn > window.Start.AddMinutes(rule.CheckInAdjustMinutes!.Value);
        var lateMinutes = Math.Max(0, (int)Math.Floor((checkIn - window.Start).TotalMinutes));
        var extremeLate = lateMinutes >= rule.ExtremeLateAfterMinutes!.Value;
        var lateStatus = new PortalStatusChoice(extremeLate ? "2 Hr Late" : "1 Hr Late",
            extremeLate ? rule.PlatformExtremeLateStatusId : rule.PlatformLateStatusId);
        if (!record.CheckOutUtc.HasValue) return late ? lateStatus : new("Present");

        var checkOut = record.CheckOutUtc.Value;
        var earlyMinutes = Math.Max(0, (int)Math.Floor((window.End - checkOut).TotalMinutes));
        if (checkOut < checkIn ||
            checkOut > window.End.AddMinutes(rule.MissingCheckoutAfterShiftEndMinutes!.Value) ||
            earlyMinutes > rule.EarlyCheckoutAbsentAfterMinutes!.Value)
            return new("Absent");
        var worked = Math.Max(0, (int)Math.Floor((checkOut - checkIn).TotalMinutes) - Math.Max(0, record.TotalBreakMinutes));
        var required = ResolveRequiredWorkingMinutes(ShiftMinutes(timing.TimeFrom ?? rule.TimeFrom, timing.TimeTo ?? rule.TimeTo), rule.WorkingMinutes);
        var complete = worked >= Math.Max(0, required - rule.CheckOutAdjustMinutes!.Value);
        if (late) return complete ? new("T-Present") : lateStatus;
        if (complete) return new("Present");
        var extremeEarly = earlyMinutes >= rule.ExtremeEarlyDepartureAfterMinutes!.Value;
        return new(extremeEarly ? "2 Hr Early" : "1 Hr Early",
            extremeEarly ? rule.PlatformExtremeEarlyDepartureStatusId : rule.PlatformEarlyDepartureStatusId);
    }

    private async Task ApplyPortalStatusAsync(
        AttendanceRecord record, EffectiveTiming timing, EffectiveAttendanceRule rule, CancellationToken ct, bool expired = false)
    {
        var choice = ResolvePortalStatus(record, timing, rule, expired);
        var mappings = _db.PlatformSettingProcessStatuses
            .Where(x => x.Process.Name == AttendanceStatusProcessName && x.Process.IsActive && x.Status.IsActive);
        PlatformSettingStatus? status;
        if (choice.ConfiguredActionStatusId is int actionStatusId)
        {
            // Older Rule fields store ActionStatus IDs. Resolve their master status
            // explicitly; never write a mapping ID into CanonicalStatusId.
            status = await (from mapping in mappings
                            join actionStatus in _db.PlatformSettingActionStatuses
                                on mapping.StatusId equals actionStatus.StatusId
                            where actionStatus.Id == actionStatusId && actionStatus.Action.Name == AttendanceStatusProcessName
                            select mapping.Status).AsTracking().SingleOrDefaultAsync(ct);
        }
        else
        {
            status = await mappings.Where(x => x.Status.Name == choice.Name)
                .Select(x => x.Status).AsTracking().SingleOrDefaultAsync(ct);
        }
        if (status == null)
            throw new InvalidOperationException($"Configure the '{choice.Name}' status under the Attendance process in Settings / Configuration before recording attendance.");
        record.CanonicalStatusId = status.Id;
        record.CanonicalStatus = status;
        record.PlatformActionStatusId = null;
        record.PlatformActionStatus = null;
        record.AttendanceStatusId = null;
        record.AttendanceStatus = null;
    }
}
