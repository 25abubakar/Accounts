using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceRecords")]
public sealed class AttendanceRecord : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public int? AttendanceStatusId { get; set; }
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public DateTime? BreakStartedUtc { get; set; }
    public int TotalBreakMinutes { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public Person Person { get; set; } = null!;
    public StatusMaster? AttendanceStatus { get; set; }
}
