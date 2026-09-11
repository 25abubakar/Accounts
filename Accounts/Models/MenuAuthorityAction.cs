using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

/// <summary>
/// Platform workflow stages available on a menu screen (Create / Verify / Approve / Pay, etc.).
/// Menus without rows still get a default APPROVE stage at runtime.
/// </summary>
[Table("MenuAuthorityActions")]
public sealed class MenuAuthorityAction
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int MenuId { get; set; }

    [Required, MaxLength(30)]
    public string ActionCode { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Lower rank is earlier in the pipeline. Cumulative visibility uses max assigned rank.</summary>
    public int RankOrder { get; set; } = 1;

    public bool SupportsPin { get; set; }

    /// <summary>Key in ProcessApprovalCodes when SupportsPin is used (e.g. DeductionAdjustment).</summary>
    [MaxLength(80)]
    public string? PinProcessName { get; set; }

    public bool IsActive { get; set; } = true;

    public Menu? Menu { get; set; }
}
