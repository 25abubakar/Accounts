using System.Data;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaSettingsService(
    ApplicationDbContext db,
    ICurrentUserService current) : ISaleRoznamchaSettingsService
{
    private static readonly HashSet<string> AllowedTypes =
    [
        "transaction-type", "charge-type", "transaction-date", "paid-status", "status",
    ];

    public async Task<IReadOnlyList<SaleRoznamchaSettingRow>> ListAsync(
        string settingType,
        CancellationToken ct = default)
    {
        var type = NormalizeType(settingType);
        return await TranslateSqlAsync(() => SpListQuery.ExecAsync<SaleRoznamchaSettingRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_SettingList @TenantId, @SettingType",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.NVarChar("@SettingType", type)));
    }

    public async Task<SaleRoznamchaSettingRow> SaveAsync(
        string settingType,
        int? id,
        SaveSaleRoznamchaSettingRequest request,
        CancellationToken ct = default)
    {
        var type = NormalizeType(settingType);
        var name = Required(request.Name, "Name", 100);
        var code = NormalizeCode(request.Code, name);
        var colorCode = type is "paid-status" or "status"
            ? NormalizeColor(request.ColorCode)
            : null;
        var sortOrder = type is "charge-type" or "transaction-date"
            ? Math.Max(0, request.SortOrder)
            : 0;

        if (type == "status" && request.InventoryEffect is not (0 or -1))
            throw new InvalidOperationException("Status inventory effect must be No stock change (0) or Deduct stock (-1).");

        var rows = await TranslateSqlAsync(() => SpListQuery.ExecAsync<SaleRoznamchaSettingRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_SettingSave @TenantId, @SettingType, @Id, @Code, @Name, @ColorCode, @SortOrder, @InventoryEffect, @IsActive",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.NVarChar("@SettingType", type),
            SpListQuery.IntNullable("@Id", id),
            SpListQuery.NVarChar("@Code", code),
            SpListQuery.NVarChar("@Name", name),
            SpListQuery.NVarChar("@ColorCode", colorCode),
            SpListQuery.Int("@SortOrder", sortOrder),
            new SqlParameter("@InventoryEffect", SqlDbType.SmallInt) { Value = type == "status" ? request.InventoryEffect : 0 },
            SpListQuery.Bit("@IsActive", request.IsActive)));

        return rows.Single();
    }

    public async Task DeactivateAsync(string settingType, int id, CancellationToken ct = default)
    {
        if (id <= 0) throw new InvalidOperationException("A valid setting is required.");
        await TranslateSqlAsync(() => db.Database.ExecuteSqlRawAsync(
                "EXEC dbo.usp_SaleRoznamcha_SettingDeactivate @TenantId, @SettingType, @Id",
                [
                    SpListQuery.TenantId(current.TenantId),
                    SpListQuery.NVarChar("@SettingType", NormalizeType(settingType)),
                    SpListQuery.Int("@Id", id),
                ],
                ct));
    }

    private static string NormalizeType(string? settingType)
    {
        var value = (settingType ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(value))
            throw new InvalidOperationException("Unknown Sale Roznamcha setting type.");
        return value;
    }

    private static string Required(string? value, string label, int maxLength)
    {
        var clean = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean))
            throw new InvalidOperationException($"{label} is required.");
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string NormalizeCode(string? value, string name)
    {
        var source = string.IsNullOrWhiteSpace(value) ? name : value;
        var code = Regex.Replace(source.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Code is required.");
        return code.Length <= 30 ? code : code[..30];
    }

    private static string NormalizeColor(string? value)
    {
        var color = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!Regex.IsMatch(color, @"^#[0-9A-F]{6}$"))
            throw new InvalidOperationException("Badge color must be a valid hex color such as #F20544.");
        return color;
    }

    private static async Task<T> TranslateSqlAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }
}
