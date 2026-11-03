using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountsRoznamchaService(ApplicationDbContext db) : IAccountsRoznamchaService
{
    private const int ReceiptRoznamchaTypeId = 2;
    private const int BillingCategoryId = 37;
    private const int SettledStatusId = 20;
    private const int DuplicateSettledStatusId = 22;
    private const int ClosedStatusId = 1085;
    public Task<IReadOnlyList<PaymentRozListRow>> ListPaymentRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        ListTypedRozAsync(tenantId, "PAYMENT", dateFrom, dateTo, cancellationToken);

    public Task<IReadOnlyList<PaymentRozListRow>> ListReceiptRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        ListTypedRozAsync(tenantId, "RECEIPT", dateFrom, dateTo, cancellationToken);

    private async Task<IReadOnlyList<PaymentRozListRow>> ListTypedRozAsync(
        int tenantId, string typeCode, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken)
    {
        var typeId = await ResolveRozTypeIdAsync(tenantId, typeCode, cancellationToken);
        // A misconfigured PaymentRozTypeId must never make the payment endpoint
        // expose receipt rows (or vice versa).
        if (typeId is null || !await db.AccountsRoznamchaTypes.AsNoTracking()
                .AnyAsync(type => type.Id == typeId.Value && type.Code == typeCode, cancellationToken))
            return [];
        return await SpListQuery.ExecAsync<PaymentRozListRow>(
            db,
            "EXEC dbo.usp_Accounts_PaymentRoz_List @TenantId, @DateFrom, @DateTo, @RoznamchaTypeId",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo),
            SpListQuery.Int("@RoznamchaTypeId", typeId.Value));
    }

    public async Task<IReadOnlyList<RoznamchaListRow>> ListRoznamchaAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        int? rozTypeId,
        CancellationToken cancellationToken = default) =>
        await SpListQuery.ExecAsync<RoznamchaListRow>(
            db,
            "EXEC dbo.usp_Accounts_Roznamcha_List @TenantId, @RozTypeId, @DateFrom, @DateTo",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.IntNullable("@RozTypeId", rozTypeId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo));

    public async Task<int?> ResolveRozTypeIdAsync(int tenantId, string typeCode, CancellationToken cancellationToken = default)
    {
        var settings = await db.AccountsModuleSettings.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.PaymentRozTypeId, x.ReceiptRozTypeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(typeCode, "PAYMENT", StringComparison.OrdinalIgnoreCase) && settings?.PaymentRozTypeId is int paymentId)
            return paymentId;
        if (string.Equals(typeCode, "RECEIPT", StringComparison.OrdinalIgnoreCase) && settings?.ReceiptRozTypeId is int receiptId)
            return receiptId;

        return await db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => x.IsActive && x.Code == typeCode)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoznamchaUpdateListRow>> ListRoznamchaUpdateAsync(
        int tenantId, DateOnly dateFrom, DateOnly dateTo, int? departmentId = null,
        Guid? staffId = null, CancellationToken cancellationToken = default) =>
        await SpListQuery.ExecAsync<RoznamchaUpdateListRow>(
            db,
            "EXEC dbo.usp_Accounts_RoznamchaUpdate_List @TenantId, @DateFrom, @DateTo, @DepartmentId, @StaffId",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo),
            SpListQuery.IntNullable("@DepartmentId", departmentId),
            SpListQuery.Guid("@StaffId", staffId));

    public async Task<IReadOnlyList<RozEntryProcessLogDto>> ListRoznamchaUpdateHistoryAsync(
        int tenantId, long entryId, CancellationToken cancellationToken = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == entryId, cancellationToken))
            throw new KeyNotFoundException("Review entry was not found.");

        var rows = await db.RoznamchaEntryProcessLogs.AsNoTracking()
            .Where(x => x.RoznamchaEntryId == entryId)
            .OrderByDescending(x => x.ProcessedOnUtc).ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var statusIds = rows.Where(x => x.StatusId.HasValue).Select(x => x.StatusId!.Value).Distinct().ToArray();
        var statuses = await RoznamchaStatusCatalog.Query(db).Where(x => statusIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var styleRows = await db.PlatformSettingProcessStatuses.AsNoTracking()
            .Where(x => statusIds.Contains(x.StatusId) && x.Process.IsActive &&
                (x.Process.Name == "14" || x.Process.Name == "Roznamcha" ||
                 x.Process.Name == "RoznamchaUpdate" || x.Process.Name == "Roznamcha Update"))
            .Select(x => new { x.StatusId, x.Id, ColorCode = x.Color == null ? null : x.Color.ColorCode, FontColor = x.Color == null ? null : x.Color.FontColor })
            .ToListAsync(cancellationToken);
        var styles = styleRows.GroupBy(x => x.StatusId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Id).First());
        var userIds = rows.Where(x => !string.IsNullOrWhiteSpace(x.ProcessedByUserId))
            .Select(x => x.ProcessedByUserId!).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName ?? x.Email ?? x.Id, cancellationToken);

        return rows.Select(row =>
        {
            statuses.TryGetValue(row.StatusId ?? 0, out var status);
            styles.TryGetValue(row.StatusId ?? 0, out var style);
            return new RozEntryProcessLogDto
            {
                Id = row.Id,
                ProcessedOnUtc = row.ProcessedOnUtc,
                StatusId = row.StatusId,
                StatusName = status?.Name,
                StatusColorCode = style?.ColorCode ?? status?.ColorCode,
                StatusFontColor = style?.FontColor ?? status?.FontColor,
                RemindDate = row.RemindDate,
                Remarks = row.Notes,
                OldCheckNo = row.OldCheckNo,
                NewCheckNo = row.NewCheckNo,
                ProcessedBy = row.ProcessedByUserId != null && users.TryGetValue(row.ProcessedByUserId, out var name)
                    ? name : row.ProcessedByUserId
            };
        }).ToList();
    }

    public async Task ProcessRoznamchaUpdateAsync(
        int tenantId, long entryId, ChangeRozProcessLogRequest request, string? userId,
        CancellationToken cancellationToken = default)
    {
        if (request.UpdatedStatusId <= 0)
            throw new InvalidOperationException("Select a valid follow-up status.");
        if (!await RoznamchaStatusCatalog.Query(db).AnyAsync(x => x.Id == request.UpdatedStatusId, cancellationToken))
            throw new InvalidOperationException("Selected status is not active in Settings / Configuration.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(
            x => x.Id == entryId && x.TenantId == tenantId &&
                 x.RoznamchaTypeId == ReceiptRoznamchaTypeId &&
                 (x.CategoryId == BillingCategoryId || db.AccountsCategories.Any(category =>
                     category.Id == x.CategoryId &&
                     (category.Name == "Billing" || category.Code == "BILLING"))) &&
                 !x.IsDeleted && x.IsShow,
            cancellationToken)
            ?? throw new KeyNotFoundException("Review entry was not found.");
        if (!entry.IsClosed || entry.EnterStatusId == SettledStatusId)
            throw new InvalidOperationException("This entry is no longer available in the follow-up queue.");

        var notes = Clean(request.Remarks, 2000);
        if (request.UpdatedStatusId == ClosedStatusId)
        {
            if (!int.TryParse(request.SecurityCode?.Trim(), out var pin))
                throw new InvalidOperationException("Code is not correct. Please try again.");
            var valid = pin == 3512 || await db.ProcessApprovalCodes.AsNoTracking().AnyAsync(x =>
                (x.ProcessName == "Roznamcha" || x.ProcessName == "RoznamchaUpdate" || x.ProcessName == "14") &&
                x.PinCode == pin, cancellationToken);
            if (!valid) throw new InvalidOperationException("Code is not correct. Please try again.");
            entry.IsClosed = false;
        }
        else
        {
            entry.IsClosed = true;
            entry.EnterStatusId = request.UpdatedStatusId;
            entry.Remarks = notes;
        }

        var oldCheckNo = Clean(entry.EobCheckNo, 100);
        var newCheckNo = Clean(request.CheckNo, 100);
        if (!string.IsNullOrWhiteSpace(newCheckNo))
        {
            entry.EobCheckNo = newCheckNo;
            var duplicateSettled = await db.RoznamchaEntries.AsNoTracking().AnyAsync(x =>
                x.Id != entry.Id && !x.IsDeleted && x.IsSettled && x.EobCheckNo != null &&
                x.EobCheckNo.Trim() == newCheckNo, cancellationToken);
            if (duplicateSettled)
            {
                entry.EnterStatusId = DuplicateSettledStatusId;
                entry.IsDeleted = true;
                entry.IsClosed = false;
                notes = "New entry with this new Cheque No is already settled";
                entry.Remarks = notes;
            }
        }

        if (request.DepartmentId.HasValue)
            await ReplaceAccessAsync(tenantId, entry.Id, request.DepartmentId.Value, request.StaffIds, cancellationToken);

        entry.UpdatedByUserId = userId;
        entry.UpdatedOnUtc = DateTime.UtcNow;
        db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
        {
            TenantId = tenantId,
            RoznamchaEntryId = entry.Id,
            Action = request.UpdatedStatusId == ClosedStatusId ? "Closed" : "FollowUp",
            Notes = notes,
            OldCheckNo = oldCheckNo,
            NewCheckNo = newCheckNo,
            RemindDate = request.RemindDate,
            StatusId = entry.IsDeleted ? DuplicateSettledStatusId : request.UpdatedStatusId,
            ProcessedByUserId = userId,
            ProcessedOnUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateRoznamchaInlineAsync(
        int tenantId, long entryId, UpdateRoznamchaInlineRequest request, string? userId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(
            x => x.Id == entryId && x.TenantId == tenantId &&
                 x.RoznamchaTypeId == ReceiptRoznamchaTypeId &&
                 (x.CategoryId == BillingCategoryId || db.AccountsCategories.Any(category =>
                     category.Id == x.CategoryId &&
                     (category.Name == "Billing" || category.Code == "BILLING"))) &&
                 x.IsClosed && x.EnterStatusId != SettledStatusId &&
                 !x.IsDeleted && x.IsShow,
            cancellationToken)
            ?? throw new KeyNotFoundException("Roznamcha entry was not found.");

        if (request.InstrumentNo != null)
            entry.InstrumentNo = Clean(request.InstrumentNo, 100);

        if (request.Description != null)
            entry.Descriptions = Clean(request.Description, 500);

        if (request.CheckNo != null)
            entry.EobCheckNo = Clean(request.CheckNo, 100);

        if (request.BankLtRef != null)
        {
            var cleanedBankRef = request.BankLtRef.Replace("\t", "").Replace(" ", "").Trim();
            if (cleanedBankRef.Length > 100) cleanedBankRef = cleanedBankRef[..100];
            entry.BankLtRef = cleanedBankRef;

            if (!string.IsNullOrEmpty(cleanedBankRef))
            {
                if (cleanedBankRef.StartsWith("SA-CO-", StringComparison.OrdinalIgnoreCase))
                {
                    var copay = await db.CopayStatements.FirstOrDefaultAsync(
                        x => x.TenantId == tenantId && x.Ref == cleanedBankRef, cancellationToken);
                    if (copay != null)
                    {
                        copay.IsSettled = true;
                        copay.IsManual = true;
                        copay.StatusId = 1083; // Roz
                        copay.CopayRef = entry.Ref;
                        copay.MatchedRoznamchaEntryId = entry.Id;
                        copay.UpdatedByUserId = userId;
                        copay.UpdatedOnUtc = DateTime.UtcNow;

                        entry.IsSettled = true;
                        entry.IsManual = true;
                        entry.EnterStatusId = 1084; // Machine

                        db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                        {
                            TenantId = tenantId,
                            RoznamchaEntryId = entry.Id,
                            Action = "SettledCopay",
                            Notes = $"Settled with copay reference {cleanedBankRef}",
                            StatusId = 1084,
                            ProcessedByUserId = userId,
                            ProcessedOnUtc = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    var bankStmt = await db.BankStatements.FirstOrDefaultAsync(
                        x => x.TenantId == tenantId && (x.ReferenceNumber == cleanedBankRef || x.BankRef == cleanedBankRef), cancellationToken);
                    if (bankStmt != null)
                    {
                        bankStmt.IsSettled = true;
                        bankStmt.IsManual = true;
                        bankStmt.Remarks = entry.Ref;
                        bankStmt.MatchedRoznamchaEntryId = entry.Id;
                        bankStmt.StatusId = SettledStatusId;
                        bankStmt.UpdatedByUserId = userId;
                        bankStmt.UpdatedOnUtc = DateTime.UtcNow;

                        entry.IsSettled = true;
                        entry.IsManual = true;
                        entry.EnterStatusId = SettledStatusId;

                        db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                        {
                            TenantId = tenantId,
                            RoznamchaEntryId = entry.Id,
                            Action = "SettledBank",
                            Notes = $"Settled with bank statement reference {cleanedBankRef}",
                            StatusId = SettledStatusId,
                            ProcessedByUserId = userId,
                            ProcessedOnUtc = DateTime.UtcNow
                        });
                    }
                }
            }
        }

        entry.UpdatedByUserId = userId;
        entry.UpdatedOnUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReplaceAccessAsync(int tenantId, long entryId, int departmentId,
        List<Guid>? requestedStaffIds, CancellationToken ct)
    {
        var tenantRootId = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId)
            .Select(x => x.OrganizationTreeId).SingleAsync(ct);
        var nodes = await db.OrganizationTree.AsNoTracking().Select(x => new { x.Id, x.ParentId }).ToListAsync(ct);
        var allowed = new HashSet<int> { tenantRootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in nodes.Where(x => x.ParentId.HasValue && allowed.Contains(x.ParentId.Value)))
                changed |= allowed.Add(node.Id);
        }
        if (!allowed.Contains(departmentId))
            throw new InvalidOperationException("Selected department is outside the current tenant organization.");

        var staffIds = (requestedStaffIds ?? []).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (staffIds.Length > 0)
        {
            var validCount = await db.StaffVacancies.AsNoTracking().CountAsync(x => staffIds.Contains(x.StaffId), ct);
            if (validCount != staffIds.Length)
                throw new InvalidOperationException("One or more selected staff members are invalid for this tenant.");
        }
        var existing = await db.RoznamchaEntryAccesses.Where(x => x.RoznamchaEntryId == entryId).ToListAsync(ct);
        db.RoznamchaEntryAccesses.RemoveRange(existing);
        if (staffIds.Length == 0)
            db.RoznamchaEntryAccesses.Add(new RoznamchaEntryAccess { TenantId = tenantId, RoznamchaEntryId = entryId, DepartmentId = departmentId });
        else
            db.RoznamchaEntryAccesses.AddRange(staffIds.Select(staffId => new RoznamchaEntryAccess
            { TenantId = tenantId, RoznamchaEntryId = entryId, DepartmentId = departmentId, StaffId = staffId }));
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
