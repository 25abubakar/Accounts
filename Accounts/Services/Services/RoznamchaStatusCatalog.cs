using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

/// <summary>Roznamcha status IDs are the shared Settings / Configuration Status IDs.</summary>
public static class RoznamchaStatusCatalog
{
    public sealed class Lookup
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string? ColorCode { get; init; }
        public string? FontColor { get; init; }
    }

    public static IQueryable<Lookup> Query(ApplicationDbContext db, string? processName = null)
    {
        var mappings = db.PlatformSettingProcessStatuses.AsNoTracking()
            .Where(x => processName != null && x.Process.Name == processName && x.Process.IsActive);
        return from status in db.PlatformSettingStatuses.AsNoTracking()
               where status.IsActive
               join mapping in mappings on status.Id equals mapping.StatusId into mapped
               from mapping in mapped.DefaultIfEmpty()
               where processName == null || !mappings.Any() || mapping != null
               select new Lookup
               {
                   Id = status.Id, Code = status.Name.ToUpper(), Name = status.Name,
                   ColorCode = mapping != null && mapping.Color != null ? mapping.Color.ColorCode : null,
                   FontColor = mapping != null && mapping.Color != null ? mapping.Color.FontColor : null
               };
    }

    public static async Task<string?> ProcessForEntryAsync(ApplicationDbContext db, int? typeId, CancellationToken ct)
    {
        var code = await db.AccountsRoznamchaTypes.AsNoTracking().Where(x => x.Id == typeId)
            .Select(x => x.Code).FirstOrDefaultAsync(ct);
        return code?.ToUpperInvariant() switch { "PAYMENT" => "Payment", "RECEIPT" => "Receipt", _ => null };
    }
}
