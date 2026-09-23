using System.Data;
using Accounts.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

/// <summary>Helpers for Wave-1 list stored procedures (SqlQueryRaw).</summary>
public static class SpListQuery
{
    public static SqlParameter TenantId(int tenantId) =>
        new("@TenantId", SqlDbType.Int) { Value = tenantId };

    public static SqlParameter Int(string name, int value) =>
        new(name, SqlDbType.Int) { Value = value };

    public static SqlParameter IntNullable(string name, int? value) =>
        new(name, SqlDbType.Int) { Value = value.HasValue ? value.Value : DBNull.Value };

    public static SqlParameter NVarChar(string name, string? value) =>
        new(name, SqlDbType.NVarChar) { Value = (object?)value ?? DBNull.Value };

    public static SqlParameter Date(string name, DateOnly value) =>
        new(name, SqlDbType.Date) { Value = value.ToDateTime(TimeOnly.MinValue) };

    public static SqlParameter DateNullable(string name, DateOnly? value) =>
        new(name, SqlDbType.Date) { Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value };

    public static SqlParameter Bit(string name, bool value) =>
        new(name, SqlDbType.Bit) { Value = value };

    public static SqlParameter Guid(string name, Guid? value) =>
        new(name, SqlDbType.UniqueIdentifier) { Value = value.HasValue ? value.Value : DBNull.Value };

    public static Task<List<T>> ExecAsync<T>(
        ApplicationDbContext db,
        string sql,
        CancellationToken ct,
        params object[] parameters) =>
        db.Database.SqlQueryRaw<T>(sql, parameters).ToListAsync(ct);
}
