namespace Accounts.DTOs.CommCenter
{
    public class InstructionTargetStaffDto
    {
        public Guid StaffId { get; set; }
        public Guid PersonId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? LoginId { get; set; }
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }
        public string? CountryName { get; set; }
        public int? OrganizationId { get; set; }
        public int? TenantId { get; set; }
    }

    public class InstructionAudienceScopeDto
    {
        public bool CanBroadcastToEveryone { get; set; }
        public string ScopeLabel { get; set; } = "Hierarchy";
        public List<InstructionTargetStaffDto> Staff { get; set; } = new();
    }
}
