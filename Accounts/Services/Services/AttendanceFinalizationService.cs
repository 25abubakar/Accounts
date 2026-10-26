using System.Globalization;
using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AttendanceFinalizationService(
    ApplicationDbContext db,
    ILogger<AttendanceFinalizationService> logger)
{
    // Synchronize only expired, open attendance statuses. No policy, mapping,
    // schedule, punch, or financial value is created/changed by this operation.
    public async Task<int> ExpireMissingCheckoutStatusesAsync(
        int tenantId, DateOnly dateFrom, DateOnly dateTo,
        IReadOnlyCollection<Guid>? scopedPersonIds = null,
        CancellationToken cancellationToken = default)
    {
        if (scopedPersonIds is { Count: 0 }) return 0;
        var query =
            from record in db.AttendanceRecords.IgnoreQueryFilters().AsNoTracking()
            join person in db.Persons.IgnoreQueryFilters().AsNoTracking() on record.PersonId equals person.PersonId
            join staff in db.StaffVacancies.IgnoreQueryFilters().AsNoTracking() on person.PersonId equals staff.PersonId
            join map in db.AttendanceMapRules.IgnoreQueryFilters().AsNoTracking() on staff.StaffId equals map.StaffId
            join type in db.AttendanceTypes.IgnoreQueryFilters().AsNoTracking() on map.AttendanceEntryTypeId equals type.Id
            join rule in db.AttendanceRuleSettings.IgnoreQueryFilters().AsNoTracking() on type.Id equals rule.AttendanceEntryTypeId
            where record.TenantId == tenantId && person.TenantId == tenantId && staff.TenantId == tenantId &&
                  map.TenantId == tenantId && type.TenantId == tenantId && rule.TenantId == tenantId &&
                  person.IsActive && type.IsActive && rule.IsActive && rule.IsApproved && !map.IsOpenAttendance &&
                  record.AttendanceDate >= dateFrom && record.AttendanceDate <= dateTo &&
                  record.CheckInUtc != null && record.CheckOutUtc == null && record.EffectiveCheckOutUtc == null
            select new
            {
                Record = record, staff.StaffId, MapId = map.Id, RuleId = rule.Id,
                map.TimeFrom, map.TimeTo, person.TimeZoneId, type.Code,
                rule.MissingCheckoutAfterShiftEndMinutes,
                StatusName = record.CanonicalStatus != null ? record.CanonicalStatus.Name :
                    record.PlatformActionStatus != null ? record.PlatformActionStatus.Status.Name :
                    record.AttendanceStatus != null ? record.AttendanceStatus.Status.StatusName : null
            };
        if (scopedPersonIds != null)
            query = query.Where(item => scopedPersonIds.Contains(item.Record.PersonId));
        var candidates = (await query.ToListAsync(cancellationToken))
            .GroupBy(item => item.Record.Id)
            .Select(group => group.OrderByDescending(item => item.MapId).ThenByDescending(item => item.RuleId).First())
            .ToList();
        if (candidates.Count == 0) return 0;
        var absentId = await db.PlatformSettingStatuses.AsNoTracking()
            .Where(status => status.IsActive && status.Name == "Absent")
            .Select(status => (int?)status.Id).SingleOrDefaultAsync(cancellationToken);
        if (!absentId.HasValue) return 0; // Never invent a status/configuration.
        var staffIds = candidates.Select(item => item.StaffId).Distinct().ToArray();
        var schedules = await db.EmployeeTimingSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && staffIds.Contains(item.StaffId) &&
                           item.ScheduleDate >= dateFrom && item.ScheduleDate <= dateTo)
            .ToDictionaryAsync(item => (item.StaffId, item.ScheduleDate), cancellationToken);
        var changed = 0;
        foreach (var item in candidates)
        {
            // Preserve leave/off/holiday, absence, manual review and unknown statuses.
            if (Normalize(item.StatusName) is not ("P" or "PRESENT" or "TP" or "TPRESENT" or
                "LATE" or "1HRLATE" or "2HRLATE" or "1HOURLATE" or "2HOURLATE")) continue;
            if (IsNotRequiredEntryType(item.Code)) continue;
            var record = item.Record;
            schedules.TryGetValue((item.StaffId, record.AttendanceDate), out var schedule);
            if (!(schedule?.IsOn ?? !IsWeekend(record.AttendanceDate))) continue;
            var from = schedule?.TimeFrom ?? item.TimeFrom;
            var to = schedule?.TimeTo ?? item.TimeTo;
            if (!TimeOnly.TryParse(from, out _) || !TimeOnly.TryParse(to, out _)) continue;
            if (!MissingCheckoutDeadlinePassed(record.AttendanceDate, from, to,
                item.MissingCheckoutAfterShiftEndMinutes, BusinessClock.Now(item.TimeZoneId))) continue;
            // Guard against a checkout or manual status correction arriving concurrently.
            changed += await db.AttendanceRecords.IgnoreQueryFilters()
                .Where(current => current.TenantId == tenantId && current.Id == record.Id &&
                    current.CheckInUtc == record.CheckInUtc && current.CheckOutUtc == null && current.EffectiveCheckOutUtc == null &&
                    current.CanonicalStatusId == record.CanonicalStatusId && current.AttendanceStatusId == record.AttendanceStatusId &&
                    current.PlatformActionStatusId == record.PlatformActionStatusId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.CanonicalStatusId, absentId)
                    .SetProperty(current => current.ModifiedDate, (DateTime?)DateTime.UtcNow), cancellationToken);
        }
        return changed;
    }

    private static bool MissingCheckoutDeadlinePassed(DateOnly date, string from, string to, int minutes, DateTime localNow) =>
        localNow > ResolveShiftWindow(date, from, to).End.AddMinutes(Math.Max(0, minutes));

    public async Task<IReadOnlyDictionary<int, int>> RefreshCurrentPeriodsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.IsActive)
            .Select(tenant => new
            {
                tenant.Id,
                TimeZoneId = db.AttendancePolicies.IgnoreQueryFilters()
                    .Where(policy => policy.IsActive && (policy.TenantId == tenant.Id || policy.TenantId == null))
                    .OrderByDescending(policy => policy.TenantId == tenant.Id)
                    .Select(policy => policy.TimeZoneId)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        var changesByTenant = new Dictionary<int, int>();

        foreach (var tenant in tenants)
        {
            try
            {
                var today = BusinessClock.Today(tenant.TimeZoneId);
                var changed = await RefreshPeriodAsync(
                    tenant.Id,
                    today.Year,
                    today.Month,
                    cancellationToken);
                if (changed > 0)
                    changesByTenant[tenant.Id] = changed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Attendance finalization failed for tenant {TenantId}.",
                    tenant.Id);
            }
        }

        return changesByTenant;
    }

    public async Task<int> RefreshPeriodAsync(
        int tenantId,
        int year,
        int month,
        CancellationToken cancellationToken = default) =>
        await RefreshPeriodAsync(tenantId, year, month, scopedPersonIds: null, cancellationToken);

    public async Task<int> RefreshPeriodAsync(
        int tenantId,
        int year,
        int month,
        IReadOnlyCollection<Guid>? scopedPersonIds,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        var tenantTimeZoneId = await db.AttendancePolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(policy => policy.IsActive && (policy.TenantId == tenantId || policy.TenantId == null))
            .OrderByDescending(policy => policy.TenantId == tenantId)
            .Select(policy => policy.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var today = BusinessClock.Today(tenantTimeZoneId);
        var dateTo = monthEnd < today ? monthEnd : today;
        if (monthStart > dateTo)
            return 0;

        var expiredStatuses = await ExpireMissingCheckoutStatusesAsync(
            tenantId, monthStart, dateTo, scopedPersonIds, cancellationToken);

        var personFilter = scopedPersonIds is { Count: > 0 }
            ? scopedPersonIds.ToHashSet()
            : null;

        var employees = await (
            from staff in db.StaffVacancies.IgnoreQueryFilters().AsNoTracking()
            join person in db.Persons.IgnoreQueryFilters().AsNoTracking()
                on staff.PersonId equals person.PersonId
            join profile in db.PersonHrProfiles.IgnoreQueryFilters().AsNoTracking()
                on new { staff.TenantId, PersonId = person.PersonId }
                equals new { profile.TenantId, profile.PersonId } into profiles
            from profile in profiles.DefaultIfEmpty()
            where staff.TenantId == tenantId && person.TenantId == tenantId && person.IsActive
            select new EmployeeRow(
                person.PersonId,
                staff.StaffId,
                person.ShiftStartTime,
                person.ShiftEndTime,
                person.TimeZoneId,
                profile == null ? null : profile.JoiningDate,
                person.TerminationDateUtc))
            .ToListAsync(cancellationToken);

        if (personFilter is not null)
            employees = employees.Where(employee => personFilter.Contains(employee.PersonId)).ToList();

        if (employees.Count == 0)
            return 0;

        var staffIds = employees.Select(employee => employee.StaffId).ToArray();
        var personIds = employees.Select(employee => employee.PersonId).ToArray();

        var mapRules = (await db.AttendanceMapRules.IgnoreQueryFilters().AsNoTracking()
                .Where(rule => rule.TenantId == tenantId && staffIds.Contains(rule.StaffId))
                .ToListAsync(cancellationToken))
            .GroupBy(rule => rule.StaffId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.Id).First());

        var entryTypeIds = mapRules.Values.Select(rule => rule.AttendanceEntryTypeId).Distinct().ToArray();
        var ruleSettings = (await db.AttendanceRuleSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(rule =>
                    rule.TenantId == tenantId &&
                    rule.IsActive &&
                    rule.IsApproved &&
                    entryTypeIds.Contains(rule.AttendanceEntryTypeId))
                .ToListAsync(cancellationToken))
            .GroupBy(rule => rule.AttendanceEntryTypeId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.Id).First());

        var entryTypeCodes = await db.AttendanceTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(type => type.TenantId == tenantId && entryTypeIds.Contains(type.Id))
            .ToDictionaryAsync(type => type.Id, type => type.Code, cancellationToken);

        var schedules = await db.EmployeeTimingSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(schedule =>
                schedule.TenantId == tenantId &&
                staffIds.Contains(schedule.StaffId) &&
                schedule.ScheduleDate >= monthStart &&
                schedule.ScheduleDate <= dateTo)
            .ToListAsync(cancellationToken);
        var schedulesByDay = schedules.ToDictionary(
            schedule => (schedule.StaffId, schedule.ScheduleDate));

        var records = await db.AttendanceRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                personIds.Contains(record.PersonId) &&
                record.AttendanceDate >= monthStart &&
                record.AttendanceDate <= dateTo)
            .ToListAsync(cancellationToken);
        var recordsByDay = records.ToDictionary(record => (record.PersonId, record.AttendanceDate));

        var statusIds = records
            .Where(record => record.AttendanceStatusId.HasValue)
            .Select(record => record.AttendanceStatusId!.Value)
            .Distinct()
            .ToArray();
        var statuses = statusIds.Length == 0
            ? new Dictionary<int, StatusRow>()
            : await db.ProcessStatusStyles.IgnoreQueryFilters().AsNoTracking()
                .Where(status => statusIds.Contains(status.Id))
                .Select(status => new StatusRow(
                    status.Id,
                    status.Code,
                    status.Status.StatusName,
                    status.IsPaid))
                .ToDictionaryAsync(status => status.Id, cancellationToken);

        var platformStatusIds = records
            .Where(record => record.PlatformActionStatusId.HasValue)
            .Select(record => record.PlatformActionStatusId!.Value)
            .Distinct()
            .ToArray();
        var platformStatuses = platformStatusIds.Length == 0
            ? new Dictionary<int, StatusRow>()
            : await db.PlatformSettingActionStatuses.IgnoreQueryFilters().AsNoTracking()
                .Where(status => platformStatusIds.Contains(status.Id))
                .Select(status => new StatusRow(
                    status.Id,
                    status.Status.Name,
                    status.Status.Name,
                    false))
                .ToDictionaryAsync(status => status.Id, cancellationToken);

        var canonicalStatusIds = records.Where(record => record.CanonicalStatusId.HasValue)
            .Select(record => record.CanonicalStatusId!.Value).Distinct().ToArray();
        var canonicalStatuses = canonicalStatusIds.Length == 0
            ? new Dictionary<int, StatusRow>()
            : await db.PlatformSettingStatuses.AsNoTracking()
                .Where(status => canonicalStatusIds.Contains(status.Id))
                .Select(status => new StatusRow(status.Id, status.Name, status.Name, false))
                .ToDictionaryAsync(status => status.Id, cancellationToken);

        var existingRows = await db.Set<AttendanceDailyFinalization>()
            .IgnoreQueryFilters()
            .Where(row =>
                row.TenantId == tenantId &&
                personIds.Contains(row.PersonId) &&
                row.AttendanceDate >= monthStart &&
                row.AttendanceDate <= dateTo)
            .ToListAsync(cancellationToken);
        // Prefer newest Id if legacy duplicates somehow exist (unique index should prevent this).
        var existingByDay = existingRows
            .GroupBy(row => (row.PersonId, row.AttendanceDate))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.Id).First());

        var utcNow = BusinessClock.UtcNow();
        var changed = 0;

        // One StaffVacancy row per PersonId — avoids double Add for the same day key.
        foreach (var employee in employees
                     .GroupBy(e => e.PersonId)
                     .Select(g => g.OrderByDescending(e => e.StaffId).First()))
        {
            var localNow = BusinessClock.Now(employee.TimeZoneId ?? tenantTimeZoneId);
            mapRules.TryGetValue(employee.StaffId, out var mapRule);
            AttendanceRuleSetting? rule = null;
            if (mapRule is not null)
                ruleSettings.TryGetValue(mapRule.AttendanceEntryTypeId, out rule);

            var isNotRequired = mapRule is not null &&
                entryTypeCodes.TryGetValue(mapRule.AttendanceEntryTypeId, out var entryTypeCode) &&
                IsNotRequiredEntryType(entryTypeCode);

            for (var date = monthStart; date <= dateTo; date = date.AddDays(1))
            {
                if (!IsInsideEmploymentWindow(employee, date))
                    continue;

                schedulesByDay.TryGetValue((employee.StaffId, date), out var schedule);
                recordsByDay.TryGetValue((employee.PersonId, date), out var attendance);
                var isWorkingDay = schedule?.IsOn ?? !IsWeekend(date);
                var timeFrom = schedule?.TimeFrom ?? mapRule?.TimeFrom ?? employee.ShiftStartTime;
                var timeTo = schedule?.TimeTo ?? mapRule?.TimeTo ?? employee.ShiftEndTime;
                var requiredMinutes = isWorkingDay
                    ? ResolveRequiredMinutes(schedule, rule, timeFrom, timeTo)
                    : 0;
                var shiftWindow = ResolveShiftWindow(date, timeFrom, timeTo);
                var deadline = shiftWindow.End.AddMinutes(
                    Math.Max(0, rule?.MissingCheckoutAfterShiftEndMinutes ?? 120));
                var effectiveCheckIn = attendance?.EffectiveCheckInUtc ?? attendance?.CheckInUtc;
                var effectiveCheckOut = attendance?.EffectiveCheckOutUtc ?? attendance?.CheckOutUtc;
                var isExplicitAbsent = !isNotRequired &&
                    IsAbsentAttendance(attendance, statuses, platformStatuses, canonicalStatuses);
                var isExcused = isNotRequired ||
                    (!isExplicitAbsent && IsExcusedAttendance(attendance, statuses, canonicalStatuses));
                var calculation = AttendanceDailyFinalizationCalculator.Calculate(
                    new AttendanceDayCalculationInput(
                        isWorkingDay,
                        isExcused,
                        requiredMinutes,
                        localNow,
                        deadline,
                        effectiveCheckIn,
                        effectiveCheckOut,
                        attendance?.TotalBreakMinutes ?? 0,
                        shiftWindow.Start,
                        rule?.CheckInAdjustMinutes ?? 0,
                        rule?.ExtremeLateAfterMinutes ?? 60,
                        rule?.IsCompletedLateDeductionActive == true,
                        rule?.CompletedLateDeductionPercentage ?? 50m,
                        isExplicitAbsent));

                if (!existingByDay.TryGetValue((employee.PersonId, date), out var row))
                {
                    row = new AttendanceDailyFinalization
                    {
                        TenantId = tenantId,
                        PersonId = employee.PersonId,
                        StaffId = employee.StaffId,
                        AttendanceDate = date
                    };
                    db.Set<AttendanceDailyFinalization>().Add(row);
                    existingByDay[(employee.PersonId, date)] = row;
                    Apply(row, attendance?.Id, calculation, utcNow);
                    changed++;
                    continue;
                }

                if (!HasChanged(row, attendance?.Id, calculation))
                    continue;

                Apply(row, attendance?.Id, calculation, utcNow);
                changed++;
            }
        }

        if (changed > 0)
            await SaveFinalizationsWithDuplicateRetryAsync(tenantId, monthStart, dateTo, cancellationToken);

        return changed + expiredStatuses;
    }

    private async Task SaveFinalizationsWithDuplicateRetryAsync(
        int tenantId,
        DateOnly monthStart,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (DbUpdateException exception) when (IsDuplicateFinalizationKey(exception))
        {
            logger.LogWarning(
                exception,
                "Concurrent attendance finalization for tenant {TenantId}; merging inserts as updates.",
                tenantId);
        }

        await MergePendingInsertsOntoExistingRowsAsync(tenantId, monthStart, dateTo, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MergePendingInsertsOntoExistingRowsAsync(
        int tenantId,
        DateOnly monthStart,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        var pendingAdds = db.ChangeTracker.Entries<AttendanceDailyFinalization>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        if (pendingAdds.Count == 0)
            return;

        foreach (var entry in db.ChangeTracker.Entries<AttendanceDailyFinalization>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        var personIds = pendingAdds.Select(row => row.PersonId).Distinct().ToArray();
        var existing = await db.Set<AttendanceDailyFinalization>()
            .IgnoreQueryFilters()
            .Where(row =>
                row.TenantId == tenantId &&
                personIds.Contains(row.PersonId) &&
                row.AttendanceDate >= monthStart &&
                row.AttendanceDate <= dateTo)
            .ToListAsync(cancellationToken);

        var byDay = existing
            .GroupBy(row => (row.PersonId, row.AttendanceDate))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.Id).First());

        foreach (var pending in pendingAdds)
        {
            if (!byDay.TryGetValue((pending.PersonId, pending.AttendanceDate), out var row))
            {
                db.Set<AttendanceDailyFinalization>().Add(pending);
                continue;
            }

            row.StaffId = pending.StaffId;
            row.AttendanceRecordId = pending.AttendanceRecordId;
            row.State = pending.State;
            row.IsWorkingDay = pending.IsWorkingDay;
            row.IsFinalized = pending.IsFinalized;
            row.IsFullDayAbsent = pending.IsFullDayAbsent;
            row.RequiredMinutes = pending.RequiredMinutes;
            row.WorkedMinutes = pending.WorkedMinutes;
            row.ShortMinutes = pending.ShortMinutes;
            row.OvertimeMinutes = pending.OvertimeMinutes;
            row.LateMinutes = pending.LateMinutes;
            row.LateBandMinutes = pending.LateBandMinutes;
            row.LatePenaltyMinutes = pending.LatePenaltyMinutes;
            row.FinalizedDateUtc = pending.FinalizedDateUtc;
            row.LastEvaluatedDateUtc = pending.LastEvaluatedDateUtc;
        }
    }

    private static bool IsDuplicateFinalizationKey(DbUpdateException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is not Microsoft.Data.SqlClient.SqlException sql)
                continue;

            // 2601 = duplicate key; 2627 = unique constraint violation
            if (sql.Number is not (2601 or 2627))
                continue;

            return sql.Message.Contains(
                       "IX_AttendanceDailyFinalizations_TenantId_PersonId_AttendanceDate",
                       StringComparison.OrdinalIgnoreCase)
                   || sql.Message.Contains(
                       "AttendanceDailyFinalizations",
                       StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool HasChanged(
        AttendanceDailyFinalization row,
        long? attendanceRecordId,
        AttendanceDayCalculation value) =>
        row.AttendanceRecordId != attendanceRecordId ||
        row.State != value.State ||
        row.IsWorkingDay != value.IsWorkingDay ||
        row.IsFinalized != value.IsFinalized ||
        row.IsFullDayAbsent != value.IsFullDayAbsent ||
        row.RequiredMinutes != value.RequiredMinutes ||
        row.WorkedMinutes != value.WorkedMinutes ||
        row.ShortMinutes != value.ShortMinutes ||
        row.OvertimeMinutes != value.OvertimeMinutes ||
        row.LateMinutes != value.LateMinutes ||
        row.LateBandMinutes != value.LateBandMinutes ||
        row.LatePenaltyMinutes != value.LatePenaltyMinutes;

    private static void Apply(
        AttendanceDailyFinalization row,
        long? attendanceRecordId,
        AttendanceDayCalculation value,
        DateTime utcNow)
    {
        row.AttendanceRecordId = attendanceRecordId;
        row.State = value.State;
        row.IsWorkingDay = value.IsWorkingDay;
        row.IsFinalized = value.IsFinalized;
        row.IsFullDayAbsent = value.IsFullDayAbsent;
        row.RequiredMinutes = value.RequiredMinutes;
        row.WorkedMinutes = value.WorkedMinutes;
        row.ShortMinutes = value.ShortMinutes;
        row.OvertimeMinutes = value.OvertimeMinutes;
        row.LateMinutes = value.LateMinutes;
        row.LateBandMinutes = value.LateBandMinutes;
        row.LatePenaltyMinutes = value.LatePenaltyMinutes;
        row.FinalizedDateUtc = value.IsFinalized ? row.FinalizedDateUtc ?? utcNow : null;
        row.LastEvaluatedDateUtc = utcNow;
    }

    private static int ResolveRequiredMinutes(
        EmployeeTimingSchedule? schedule,
        AttendanceRuleSetting? rule,
        string? timeFrom,
        string? timeTo)
    {
        // Timing Chart day override wins; otherwise Map Attendance TimeFrom/TimeTo
        // (announced shift) is the real one-day length — not a blanket rule WorkingMinutes.
        if (schedule?.WorkingMinutes > 0)
            return schedule.WorkingMinutes;
        if (TryTime(timeFrom, out _) && TryTime(timeTo, out _))
            return ShiftMinutes(timeFrom, timeTo);
        if (rule?.WorkingMinutes > 0)
            return rule.WorkingMinutes;
        return 540;
    }

    private static int ShiftMinutes(string? timeFrom, string? timeTo)
    {
        if (!TryTime(timeFrom, out var start) || !TryTime(timeTo, out var end))
            return 540;
        var minutes = (int)(end - start).TotalMinutes;
        return minutes > 0 ? minutes : minutes + 1440;
    }

    private static (DateTime Start, DateTime End) ResolveShiftWindow(
        DateOnly date,
        string? timeFrom,
        string? timeTo)
    {
        if (!TryTime(timeFrom, out var from))
            from = new TimeOnly(9, 0);
        if (!TryTime(timeTo, out var to))
            to = new TimeOnly(18, 0);
        var start = date.ToDateTime(from);
        var end = date.ToDateTime(to);
        if (end <= start)
            end = end.AddDays(1);
        return (start, end);
    }

    private static bool TryTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time) || TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time);

    private static bool IsInsideEmploymentWindow(EmployeeRow employee, DateOnly date)
    {
        if (employee.JoiningDate.HasValue && date < DateOnly.FromDateTime(employee.JoiningDate.Value))
            return false;
        if (employee.TerminationDateUtc.HasValue && date > DateOnly.FromDateTime(employee.TerminationDateUtc.Value))
            return false;
        return true;
    }

    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static bool IsNotRequiredEntryType(string? code)
    {
        var normalized = Normalize(code);
        return normalized is "NONE" or "NOTREQUIRED" or "NOREQUIREDATTENDANCE";
    }

    private static bool IsAbsentAttendance(
        AttendanceRecord? attendance,
        IReadOnlyDictionary<int, StatusRow> processStatuses,
        IReadOnlyDictionary<int, StatusRow> platformStatuses,
        IReadOnlyDictionary<int, StatusRow> canonicalStatuses)
    {
        if (attendance is null) return false;
        if (attendance.CanonicalStatusId is int canonicalId && canonicalStatuses.TryGetValue(canonicalId, out var canonical))
            return IsAbsentStatus(canonical);

        if (attendance.AttendanceStatusId is int processStatusId &&
            processStatuses.TryGetValue(processStatusId, out var processStatus) &&
            IsAbsentStatus(processStatus))
            return true;

        if (attendance.PlatformActionStatusId is int platformStatusId &&
            platformStatuses.TryGetValue(platformStatusId, out var platformStatus) &&
            IsAbsentStatus(platformStatus))
            return true;

        return false;
    }

    private static bool IsAbsentStatus(StatusRow status)
    {
        var absent = Normalize(nameof(AttendanceFinalizationStates.Absent));
        return Normalize(status.Code) == absent ||
               Normalize(status.Name).Contains(absent, StringComparison.Ordinal);
    }

    private static bool IsExcusedAttendance(
        AttendanceRecord? attendance,
        IReadOnlyDictionary<int, StatusRow> statuses,
        IReadOnlyDictionary<int, StatusRow> canonicalStatuses)
    {
        if (attendance == null) return false;
        StatusRow? status = null;
        if (attendance?.CanonicalStatusId is int canonicalId)
            canonicalStatuses.TryGetValue(canonicalId, out status);
        else if (attendance?.AttendanceStatusId is int statusId)
            statuses.TryGetValue(statusId, out status);
        if (status == null)
            return false;

        var code = Normalize(status.Code);
        var name = Normalize(status.Name);
        return code is "L" or "LEAVE" or "ONLEAVE" or "H" or "HOLIDAY" or "DO" or "DAYOFF" ||
               name.Contains("LEAVE", StringComparison.Ordinal) ||
               name.Contains("HOLIDAY", StringComparison.Ordinal) ||
               name.Contains("DAYOFF", StringComparison.Ordinal) ||
               status.IsPaid && (attendance.CheckInUtc is null && attendance.CheckOutUtc is null);
    }

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed record EmployeeRow(
        Guid PersonId,
        Guid StaffId,
        string ShiftStartTime,
        string ShiftEndTime,
        string? TimeZoneId,
        DateTime? JoiningDate,
        DateTime? TerminationDateUtc);

    private sealed record StatusRow(int Id, string Code, string Name, bool IsPaid);
}
