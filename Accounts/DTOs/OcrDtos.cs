namespace Accounts.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
//  OCR EOB Module — Data Transfer Objects
// ─────────────────────────────────────────────────────────────────────────────

// ── Parsed intermediate objects (parser → pipeline) ──────────────────────────

public sealed class ParsedEobHeader
{
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? DocTitle { get; set; }
    public string? InsuranceName { get; set; }
    public string? InsuranceAddress { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderAddress { get; set; }
    public string? ProviderId { get; set; }
    public string? NPI { get; set; }
    public string? CheckEFT { get; set; }
    public string? StatementDate { get; set; }    // stored as string, parsed by pipeline
    public DateTime? StatementDateParsed { get; set; }
    public string? TaxId { get; set; }
    public string? PaymentWeek { get; set; }
    public string? Attachment { get; set; }       // file name only (set by pipeline)
    public string? Source { get; set; }           // source string constant
    public List<ParsedPatient> Patients { get; set; } = [];
    public ParsedDocumentTotals? DocumentTotals { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class ParsedPatient
{
    public string? PatientName { get; set; }
    public string? MID { get; set; }
    public string? PatientNo { get; set; }        // account number
    public string? ICN { get; set; }
    public decimal? Billed { get; set; }
    public decimal? Allowed { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? CoInsurance { get; set; }
    public decimal? RcAmount { get; set; }
    public decimal? ProviderPaid { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? AdjustmentTotal { get; set; }
    public decimal? Interest { get; set; }
    public decimal? LateFiling { get; set; }
    public decimal? Net { get; set; }
    public decimal? ProviderDiscount { get; set; }
    public decimal? OtherPlanPayment { get; set; }
    public decimal? OtherAdjustments { get; set; }
    public decimal? NonCovered { get; set; }
    public List<ParsedProcedure> Procedures { get; set; } = [];
}

public sealed class ParsedProcedure
{
    public string? RenderingProviderId { get; set; }
    public string? DayMonth { get; set; }
    public string? DateOfService { get; set; }   // raw string; parsed separately
    public DateTime? DateOfServiceParsed { get; set; }
    public string? POS { get; set; }
    public string? NOS { get; set; }
    public string? ProcedureCode { get; set; }
    public string? Modifiers { get; set; }
    public decimal? Billed { get; set; }
    public decimal? Allowed { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? CoInsurance { get; set; }
    public decimal? NOS_Val { get; set; }
    public decimal? ProviderPaid { get; set; }
    public string? SubNOS { get; set; }
    public decimal? ProviderDiscount { get; set; }
    public decimal? OtherPlan { get; set; }
    public decimal? OtherAdjustments { get; set; }
    public decimal? Copay { get; set; }
    public decimal? NonCovered { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public List<ParsedGrp> GrpRows { get; set; } = [];
    public List<string> Remarks { get; set; } = [];
}

public sealed class ParsedGrp
{
    public string? GRPCode { get; set; }
    public decimal? RcAmt { get; set; }
    public string? Remark { get; set; }
}

public sealed class ParsedDocumentTotals
{
    public int? ClaimCount { get; set; }
    public decimal? Billed { get; set; }
    public decimal? Allowed { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? CoInsurance { get; set; }
    public decimal? RC { get; set; }
    public decimal? ProviderPaid { get; set; }
    public decimal? Adjustment { get; set; }
    public decimal? CheckAmount { get; set; }
    public decimal? ProviderDiscount { get; set; }
    public decimal? OtherPlanPayment { get; set; }
}

// ── API response objects ─────────────────────────────────────────────────────

public sealed class OcrUploadResult
{
    public bool Success { get; set; }
    public bool IsAlreadyExist { get; set; }
    public int? SavedDocumentId { get; set; }
    public string? CheckEFT { get; set; }
    public int PostedClaimCount { get; set; }
    public int SkippedClaimCount { get; set; }
    public List<string> Warnings { get; set; } = [];
    public string? Error { get; set; }
    public string? FileName { get; set; }
}

public sealed class AllCareParseResult
{
    public bool Success { get; set; }
    public string? FileName { get; set; }
    public ParsedEobHeader? ParsedData { get; set; }
    public string? Error { get; set; }
}

public sealed class HealthNetFileResult
{
    public string? FileName { get; set; }
    /// <summary>"Successful", "Partial", or "Failed"</summary>
    public string Status { get; set; } = "Failed";
    public int PostedClaimCount { get; set; }
    public int SkippedClaimCount { get; set; }
    public List<string> Warnings { get; set; } = [];
    public string? Error { get; set; }
    public int? SavedDocumentId { get; set; }
}

// ── Grid & detail DTOs (API → React) ─────────────────────────────────────────

public sealed class OcrDocumentListItem
{
    public int Id { get; set; }
    public string? CheckEFT { get; set; }
    public string? EobDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public string? ColorCode { get; set; }
    public string? FontColor { get; set; }
    public string? Attachment { get; set; }
    public decimal? TotalPaid { get; set; }
    public string? Source { get; set; }
}

public sealed class OcrDocumentDetail
{
    public int Id { get; set; }
    public string? CheckEFT { get; set; }
    public string? EobDate { get; set; }
    public string? InsuranceName { get; set; }
    public string? InsuranceAddress { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderAddress { get; set; }
    public string? ProviderId { get; set; }
    public string? NPI { get; set; }
    public string? TaxId { get; set; }
    public string? PaymentWeek { get; set; }
    public string? Attachment { get; set; }
    public string? Source { get; set; }
    public OcrDocTotalsDto? DocumentTotals { get; set; }
    public List<OcrPatientDto> Patients { get; set; } = [];
}

public sealed class OcrDocTotalsDto
{
    public int? ClaimCount { get; set; }
    public decimal? Billed { get; set; }
    public decimal? Allowed { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? CoInsurance { get; set; }
    public decimal? RC { get; set; }
    public decimal? ProviderPaid { get; set; }
    public decimal? Adjustment { get; set; }
    public decimal? CheckAmount { get; set; }
    public decimal? ProviderDiscount { get; set; }
    public decimal? OtherPlanPayment { get; set; }
}

public sealed class OcrPatientDto
{
    public int Id { get; set; }
    public string? PatientName { get; set; }
    public string? MID { get; set; }
    public string? PatientNo { get; set; }
    public string? ICN { get; set; }
    public decimal? Billed { get; set; }
    public decimal? Allowed { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? CoInsurance { get; set; }
    public decimal? RcAmount { get; set; }
    public decimal? ProviderPaid { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? ProviderDiscount { get; set; }
    public decimal? OtherPlanPayment { get; set; }
    public decimal? NonCovered { get; set; }
    public List<OcrProcedureDto> Procedures { get; set; } = [];
}

public sealed class OcrProcedureDto
{
    public int Id { get; set; }
    public string? ProcedureCode { get; set; }
    public string? DateOfService { get; set; }
    public string? POS { get; set; }
    public string? NOS { get; set; }
    public string? Modifiers { get; set; }
    public decimal? Billed { get; set; }
    public decimal? Allowed { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? CoInsurance { get; set; }
    public decimal? ProviderPaid { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? Copay { get; set; }
    public decimal? ProviderDiscount { get; set; }
    public decimal? OtherPlan { get; set; }
    public decimal? OtherAdjustments { get; set; }
    public decimal? NonCovered { get; set; }
    public List<OcrGrpDto> GrpRows { get; set; } = [];
    public List<string> Remarks { get; set; } = [];
}

public sealed class OcrGrpDto
{
    public int Id { get; set; }
    public string? GRPCode { get; set; }
    public decimal? RcAmt { get; set; }
    public string? Remark { get; set; }
}

public sealed class ProcedureCodeUpdateItem
{
    public int Id { get; set; }
    public string? ProcedureCode { get; set; }
}

public sealed class ProcedureCodeUpdateRequest
{
    public List<ProcedureCodeUpdateItem> Items { get; set; } = [];
}

