namespace Accounts.Models;

public sealed class StatusConfigurationManagementRow
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ColorName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
    public string FontColor { get; set; } = string.Empty;
    public string FontSize { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int? TenantId { get; set; }
}
