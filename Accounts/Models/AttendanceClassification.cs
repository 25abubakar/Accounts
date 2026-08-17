using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceWorkModes")]
public sealed class AttendanceWorkMode
{
    public int Id { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
}
