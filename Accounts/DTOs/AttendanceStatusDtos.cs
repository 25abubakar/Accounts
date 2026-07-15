using System.ComponentModel.DataAnnotations;

namespace Accounts.DTOs;

public sealed class AttendanceStatusDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
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

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; set; }

    public bool IsPaid { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CreateAttendanceStatusDto : AttendanceStatusWriteDto;
public sealed class UpdateAttendanceStatusDto : AttendanceStatusWriteDto;
