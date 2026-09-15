namespace Accounts.Models.SpListRows;

public sealed class HrDesignationListRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int AttendanceVisibilityScope { get; set; }
    public int Count { get; set; }
}

public sealed class HrStaffListRow
{
    public Guid StaffId { get; set; }
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? PhotoUrl { get; set; }
    public bool IsActive { get; set; }
    public string? LoginId { get; set; }
    public Guid? VacancyId { get; set; }
    public string? VacancyCode { get; set; }
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public string? BranchName { get; set; }
    public string? CompanyName { get; set; }
    public string? CountryName { get; set; }
    public string? GroupName { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftEndTime { get; set; }
    public DateTime JoiningDate { get; set; }
}

public sealed class HrVacancyListRow
{
    public Guid VacancyId { get; set; }
    public int OrganizationId { get; set; }
    public string BranchName { get; set; } = "-";
    public string CompanyName { get; set; } = "-";
    public string CountryName { get; set; } = "-";
    public string NodeLabel { get; set; } = "-";
    public string VacancyCode { get; set; } = "";
    public int? DesignationId { get; set; }
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public bool IsFilled { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? EmployeeStaffId { get; set; }
    public string? EmployeeFullName { get; set; }
    public string? EmployeeEmail { get; set; }
    public string? EmployeePhone { get; set; }
    public string? EmployeePhotoUrl { get; set; }
}

public sealed class HrReportToListRow
{
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = "";
    public string? ProfilePhotoUrl { get; set; }
    public bool IsActive { get; set; }
    public Guid StaffId { get; set; }
    public string? EmployeeId { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public Guid? ReportsToPersonId { get; set; }
    public string? ReportsToName { get; set; }
    public string? ReportsToDepartment { get; set; }
    public string? ReportsToDesignation { get; set; }
    public Guid? AlternativeReportsToPersonId { get; set; }
    public string? AlternativeReportsToName { get; set; }
    public string? AlternativeReportsToDepartment { get; set; }
    public string? AlternativeReportsToDesignation { get; set; }
}

public sealed class HrPersonListRow
{
    public Guid PersonId { get; set; }
    public string LoginId { get; set; } = "-";
    public string FullName { get; set; } = "";
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PersonalEmail { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftEndTime { get; set; }
    public string? TimeZoneId { get; set; }
    public string? PhotoUrl { get; set; }
    public bool IsHired { get; set; }
    public bool IsActive { get; set; }
    public string EmploymentStatus { get; set; } = "Registered";
    public DateTime? TerminationDateUtc { get; set; }
    public string? TerminationReason { get; set; }
    public string RegisteredAt { get; set; } = "";
    public DateTime? JoiningDate { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? CompanyName { get; set; }
    public string? CountryName { get; set; }
    public string? VacancyCode { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public string? CurrentAddressLine { get; set; }
    public string? CurrentCountry { get; set; }
    public string? CurrentCity { get; set; }
    public string? PermanentAddressLine { get; set; }
    public string? PermanentCountry { get; set; }
    public string? PermanentCity { get; set; }
}

public sealed class LibraryDocumentListRow
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int LibraryTypeId { get; set; }
    public string? LibraryTypeName { get; set; }
    public string AssetKind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileExtension { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public bool IsActive { get; set; }
    public string? UploadedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

public sealed class LibraryTemplateListRow
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int LibraryTypeId { get; set; }
    public string? LibraryTypeName { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Content { get; set; } = "";
    public bool IsActive { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

public sealed class LibraryInvoiceListRow
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Currency { get; set; } = "PKR";
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}
