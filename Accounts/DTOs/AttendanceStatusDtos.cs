using System.ComponentModel.DataAnnotations;

namespace Accounts.DTOs;

public sealed class AttendanceStatusDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = "Attendance";
    public string ColorName { get; set; } = string.Empty;
    public string FontColor { get; set; } = "#FFFFFF";
    public string FontSize { get; set; } = "12px";
    public string? Description { get; set; }
    public string? ColorCode { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

public class AttendanceStatusWriteDto
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string ProcessName { get; set; } = "Attendance";

    [Required, StringLength(10, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "Code can contain only letters, numbers, hyphens, and underscores.")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string StatusName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(20)]
    [RegularExpression(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", ErrorMessage = "Color must be a valid hex value.")]
    public string? ColorCode { get; set; }

    [Required, StringLength(100)]
    public string ColorName { get; set; } = "Default";

    [Required, StringLength(20)]
    public string FontColor { get; set; } = "#FFFFFF";

    [Required, StringLength(20)]
    public string FontSize { get; set; } = "12px";

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; set; }

    public bool IsPaid { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CreateAttendanceStatusDto : AttendanceStatusWriteDto;
public sealed class UpdateAttendanceStatusDto : AttendanceStatusWriteDto;
