namespace Accounts.Models
{
    /// <summary>
    /// Marker interface for all tenant-owned entities.
    /// EF Core Global Query Filters in ApplicationDbContext use this to
    /// automatically scope every query to the current tenant.
    ///
    /// All operational tables (Persons, Vacancies, StaffVacancy, JobTitles, AppNotes)
    /// implement this interface so tenant data is never accidentally cross-contaminated.
    ///
    /// Super Admin accounts do NOT own a tenant and therefore never query these tables
    /// through the normal application flow.
    /// </summary>
    public interface ITenantEntity
    {
        /// <summary>
        /// FK to Tenants.Id — identifies which tenant owns this row.
        /// Stamped automatically by the service layer when a row is created.
        /// </summary>
        int TenantId { get; set; }
    }
}
