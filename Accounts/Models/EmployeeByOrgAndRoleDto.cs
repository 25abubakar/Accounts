namespace Accounts.Models
{
    /// <summary>
    /// Result row from dbo.usp_GetEmployeesByOrgAndRole (filled positions only).
    /// </summary>
    public class EmployeeByOrgAndRoleDto
    {
        public int OrganizationId { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string VacancyCode { get; set; } = string.Empty;
        public string? JobTitle { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
