using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Billing Domain — existing SQL tables mapped for OCR EOB posting.
//  These tables already exist; NO EF migrations will run on them.
//  Added to AppDbContext with manual table mapping only.
// ─────────────────────────────────────────────────────────────────────────────

[Table("tblPatient")]
public sealed class BillingPatient
{
    [Key] public int PatientId { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(200)] public string? PatientNo { get; set; }
    public int? PersonId { get; set; }
    [MaxLength(200)] public string? PatientName { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblPerson")]
public sealed class BillingPerson
{
    [Key] public int PersonId { get; set; }
    [MaxLength(200)] public string? FirstName { get; set; }
    [MaxLength(200)] public string? LastName { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblAppointment")]
public sealed class BillingAppointment
{
    [Key] public int AppointmentId { get; set; }
    public int? PatientId { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(50)] public string? AppointmentDate { get; set; }
    public int? StatusId { get; set; }
    public bool? IsClaimed { get; set; }
    public bool? IsCreatedFromEob { get; set; }
    public DateTime? CreatedDate { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
}

[Table("tblInsPTClaims")]
public sealed class BillingClaim
{
    [Key] public int ClaimId { get; set; }
    public int? AppointmentId { get; set; }
    public int? PatientId { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(200)] public string? EobCheckNo { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PatientResponsibility { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TotalAllowed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TotalBilled { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TotalPaid { get; set; }
    public int? StatusId { get; set; }
    public int? EobStatusId { get; set; }
    public bool? IsCreatedFromEob { get; set; }
    public DateTime? SubmittedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
}

/// <summary>View row from vgetApptCodes used for appointment-code lookups.</summary>
[Keyless]
[Table("vgetApptCodes")]
public sealed class BillingApptCodeView
{
    public int ApptCodeId { get; set; }
    public int? AppointmentId { get; set; }
    public int? ClaimId { get; set; }
    public int? PatientId { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(50)] public string? ProcedureCode { get; set; }
    public int? ProcedureCodeId { get; set; }
    [MaxLength(50)] public string? DateOfService { get; set; }
    [MaxLength(200)] public string? PolicyNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Paid { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PatientResponsibility { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CoInsurance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Deductible { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? EobAllowed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Billed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Allowed { get; set; }
    [MaxLength(100)] public string? Modifiers { get; set; }
    [MaxLength(100)] public string? FillSource { get; set; }
    public bool? IsUpload { get; set; }
    public int? LineEobStatusId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Balance { get; set; }
    public bool? IsCreatedFromEobs { get; set; }
    public int? CarrierId { get; set; }
    public int? InsuranceCompanyId { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblApptCodes")]
public sealed class BillingApptCode
{
    [Key] public int ApptCodeId { get; set; }
    public int? AppointmentId { get; set; }
    public int? ClaimId { get; set; }
    public int? PatientId { get; set; }
    public int? OrgId { get; set; }
    public int? ProcedureCodeId { get; set; }
    [MaxLength(50)] public string? ProcedureCode { get; set; }
    [MaxLength(50)] public string? DateOfService { get; set; }
    [MaxLength(200)] public string? PolicyNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ClaimAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TotalClaimAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? InsuranceRate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? InsuranceAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? EobAllowed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Paid { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PatientResponsibility { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CoInsurance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Deductible { get; set; }
    [MaxLength(100)] public string? Modifiers { get; set; }
    [MaxLength(200)] public string? PolicyNumber { get; set; }
    [MaxLength(100)] public string? FillSource { get; set; }
    public bool? IsUpload { get; set; }
    public bool? IsCreatedFromEobs { get; set; }
    public int? LineEobStatusId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Balance { get; set; }
    public int? CarrierId { get; set; }
    public int? InsuranceCompanyId { get; set; }
    public DateTime? CreatedDate { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
}

[Table("tblClaimCodeAmountDetails")]
public sealed class BillingPaymentRow
{
    [Key] public int Id { get; set; }
    public int? ApptCodeId { get; set; }
    public int? ClaimId { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(200)] public string? EobCheckNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PaidAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PatientResponsibility { get; set; }
    [MaxLength(200)] public string? Remark { get; set; }
    public bool? IsClosed { get; set; }
    public bool? IsReProcess { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblClaimEobDocuments")]
public sealed class BillingClaimEobDoc
{
    [Key] public int Id { get; set; }
    public int? ClaimId { get; set; }
    [MaxLength(200)] public string? EobCheckNo { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [MaxLength(100)] public string? UploadDate { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblClaimsLog")]
public sealed class BillingClaimsLog
{
    [Key] public int Id { get; set; }
    public int? ClaimId { get; set; }
    public int? StatusId { get; set; }
    [MaxLength(50)] public string? LogDate { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblCarrier")]
public sealed class BillingCarrier
{
    [Key] public int CarrierId { get; set; }
    [MaxLength(500)] public string? CarrierName { get; set; }
    public int? InsuranceGroupId { get; set; }
    public int? OrgId { get; set; }
    public int? BaseInsuranceId { get; set; }
    public int? InsuranceNewId { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblBaseDetailsOfInsurance")]
public sealed class BillingBaseInsurance
{
    [Key] public int Id { get; set; }
    [MaxLength(500)] public string? InsuranceName { get; set; }
}

[Table("tblInsuranceNew")]
public sealed class BillingInsuranceNew
{
    [Key] public int Id { get; set; }
    public int? BaseInsuranceId { get; set; }
    [MaxLength(500)] public string? Name { get; set; }
}

[Table("tblProcedureCodes")]
public sealed class BillingProcedureCode
{
    [Key] public int ProcCodeId { get; set; }
    [MaxLength(50)] public string? Codes { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

[Table("tblDocumentsProcess")]
public sealed class BillingDocumentProcess
{
    [Key] public int Id { get; set; }
    [MaxLength(200)] public string? EntryRef { get; set; }
    [MaxLength(200)] public string? D_Ref { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    public DateTime? CreatedDate { get; set; }
}


