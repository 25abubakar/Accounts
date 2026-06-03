namespace Accounts.Models
{
    /// <summary>
    /// Result row from dbo.vw_OrganizationVacancyPersons and dbo.usp_GetPersonsByOrgNode_Clean.
    /// </summary>
    public class OrganizationVacancyPersonDto
    {
        public int OrganizationId { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string? OrganizationCode { get; set; }
        public Guid VacancyId { get; set; }
        public string VacancyCode { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string? Department { get; set; }
        public bool IsFilled { get; set; }
        public Guid? PersonId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }
}
