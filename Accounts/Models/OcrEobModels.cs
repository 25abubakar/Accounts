using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  OCR EOB Module — Database Entities
// ─────────────────────────────────────────────────────────────────────────────

[Table("tblUploadedOcrEobRecords")]
public sealed class OcrUploadedRecord
{
    [Key] public int Id { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(100)] public string? Eob_Date { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [MaxLength(200)] public string? Check_EFT { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Tot_PaidAmt { get; set; }
    public int? StatusId { get; set; }
    [MaxLength(100)] public string? Source { get; set; }
    public DateTime? CreatedDate { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
}

[Table("tblOCRDocHeaderInfo")]
public sealed class OcrDocHeader
{
    [Key] public int Id { get; set; }
    public int? EobAttId { get; set; }            // FK → tblUploadedOcrEobRecords.Id
    public int? OrgId { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    [MaxLength(100)] public string? Phone { get; set; }
    [MaxLength(100)] public string? Fax { get; set; }
    [MaxLength(500)] public string? DocTitle { get; set; }
    [MaxLength(500)] public string? InsuranceName { get; set; }
    [MaxLength(1000)] public string? InsuranceAddress { get; set; }
    [MaxLength(500)] public string? ProviderName { get; set; }
    [MaxLength(1000)] public string? ProviderAddress { get; set; }
    [MaxLength(100)] public string? ProviderId { get; set; }
    [MaxLength(100)] public string? NPI { get; set; }
    [MaxLength(200)] public string? Check_EFT { get; set; }
    [MaxLength(100)] public string? StatementDate { get; set; }
    [MaxLength(100)] public string? TaxId { get; set; }
    [MaxLength(100)] public string? PaymentWeek { get; set; }
    [MaxLength(100)] public string? Source { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblOcrPatientDetail")]
public sealed class OcrPatientDetail
{
    [Key] public int Id { get; set; }
    public int? OcrHeaderId { get; set; }          // FK → tblOCRDocHeaderInfo.Id
    public int? OrgId { get; set; }
    [MaxLength(500)] public string? PatientName { get; set; }
    [MaxLength(200)] public string? MID { get; set; }
    [MaxLength(200)] public string? PatientNo { get; set; }   // account number
    [MaxLength(200)] public string? ICN { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Billed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Allowed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Deductible { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CoInsurance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? RcAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ProviderPaid { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PatientResponsibility { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? AdjustmentTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Interest { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? LateFiling { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Net { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ProviderDiscount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? OtherPlanPayment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? OtherAdjustments { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? NonCovered { get; set; }
    [MaxLength(500)] public string? Source { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblOcrProcedureDetails")]
public sealed class OcrProcedureDetail
{
    [Key] public int Id { get; set; }
    public int? OcrPatientId { get; set; }         // FK → tblOcrPatientDetail.Id
    public int? OrgId { get; set; }
    [MaxLength(100)] public string? RenderingProviderId { get; set; }
    [MaxLength(50)] public string? DayMonth { get; set; }
    [MaxLength(50)] public string? DateOfService { get; set; }
    [MaxLength(10)] public string? POS { get; set; }
    [MaxLength(10)] public string? NOS { get; set; }
    [MaxLength(50)] public string? ProcedureCode { get; set; }
    [MaxLength(100)] public string? Modifiers { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Billed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Allowed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Deductible { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CoInsurance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? NOS_Val { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ProviderPaid { get; set; }
    [MaxLength(50)] public string? SubNOS { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ProviderDiscount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? OtherPlan { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? OtherAdjustments { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Copay { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? NonCovered { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PatientResponsibility { get; set; }
    [MaxLength(100)] public string? FillSource { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblOcrGrpAmtDetails")]
public sealed class OcrGrpAmtDetail
{
    [Key] public int Id { get; set; }
    public int? PatientId { get; set; }            // FK → tblOcrPatientDetail.Id  (OCR patient, NOT tblPatient)
    public int? ProcCodeId { get; set; }           // FK → tblOcrProcedureDetails.Id
    [MaxLength(50)] public string? GRPCode { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Rc_Amt { get; set; }
    [MaxLength(500)] public string? REM { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblOcrProREMDetails")]
public sealed class OcrProRemDetail
{
    [Key] public int Id { get; set; }
    public int? OcrPatientId { get; set; }
    public int? OcrProcCodeId { get; set; }
    [MaxLength(500)] public string? REM { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblOcrTotalClaimInfo")]
public sealed class OcrTotalClaimInfo
{
    [Key] public int Id { get; set; }
    public int? OcrHeaderId { get; set; }          // FK → tblOCRDocHeaderInfo.Id  (NOT upload record id)
    public int? OrgId { get; set; }
    public int? ClaimCount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Billed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Allowed { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Deductible { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CoInsurance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? RC { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ProviderPaid { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Adjustment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CheckAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ProviderDiscount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? OtherPlanPayment { get; set; }
    public DateTime? CreatedDate { get; set; }
}

[Table("tblEOBGrpCodeRules")]
public sealed class EobGrpCodeRule
{
    [Key] public int Id { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(200)] public string? ExplanationCodeKey { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(50)] public string? Type { get; set; }   // "HealthNet"
    public DateTime? CreatedDate { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Read-only view (EF keyless)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Grid view: uploaded record + status name/colors.</summary>
[Keyless]
public sealed class OcrDocumentView
{
    public int Id { get; set; }
    public int? OrgId { get; set; }
    [MaxLength(200)] public string? Check_EFT { get; set; }
    [MaxLength(100)] public string? Eob_Date { get; set; }
    [MaxLength(100)] public string? Source { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? StatusId { get; set; }
    [MaxLength(200)] public string? StatusName { get; set; }
    [MaxLength(50)] public string? ColorCode { get; set; }
    [MaxLength(50)] public string? FontColor { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Tot_PaidAmt { get; set; }
}


