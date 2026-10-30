namespace Accounts.Services.Ocr.HealthNet.Models;

public enum HealthNetPageType
{
    Unknown,
    RemittanceHeader,
    ClaimEop,
    RemittanceSummary,
    TermsInformation
}

public enum HealthNetProcessingStatus
{
    Parsing,
    Parsed,
    Failed
}

public sealed class HealthNetWordModel
{
    public string Text { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }
    public double Bottom { get; set; }
}

public sealed class HealthNetTextLineModel
{
    public int LineNo { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }
    public double Bottom { get; set; }
    public List<HealthNetWordModel> Words { get; set; } = [];
}

public sealed class HealthNetPageData
{
    public int PageNumber { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string CleanText { get; set; } = string.Empty;
    public bool IsScannedPage { get; set; }
    public bool UsedOcrFallback { get; set; }
    public HealthNetPageType PageType { get; set; }
    public List<HealthNetWordModel> Words { get; set; } = [];
    public List<HealthNetTextLineModel> Lines { get; set; } = [];
}

public sealed class HealthNetEopBlock
{
    public int PageNumber { get; set; }
    public double Top { get; set; }
    public double Bottom { get; set; }
    public double PageWidth { get; set; }
    public List<HealthNetTextLineModel> Lines { get; set; } = [];
    public List<HealthNetWordModel> Words { get; set; } = [];
}

public sealed class HealthNetDocumentHeaderModel
{
    public string? Title { get; set; }
    public string? InsuranceName { get; set; }
    public string? InsuranceAddress { get; set; }
    public List<string> InsuranceAddressLines { get; set; } = [];
    public DateTime? ProcessDate { get; set; }
    public string? RemittanceNo { get; set; }
    public string? CheckNumber { get => RemittanceNo; set => RemittanceNo = value; }
    public DateTime? EobDate { get => ProcessDate; set => ProcessDate = value; }
    public string? PayeeNo { get; set; }
    public string? ServiceProviderNo { get; set; }
    public string? ServiceProviderName { get; set; }
    public string? ProviderAddress { get; set; }
    public List<string> ProviderAddressLines { get; set; } = [];
    public string? ProviderNpi { get; set; }
    public string? MemberPlanCode { get; set; }
}

public sealed class HealthNetClaimLineModel
{
    public int LineNo { get; set; }
    public int PageNumber { get; set; }
    public string? RawLineText { get; set; }
    public DateTime? ServiceDateFrom { get; set; }
    public DateTime? ServiceDateThru { get; set; }
    public string? CPTRev { get; set; }
    public List<string> Modifiers { get; set; } = [];
    public decimal? BilledAmount { get; set; }
    public decimal? AmountNotAllowed { get; set; }
    public decimal? AllowedAmount { get; set; }
    public decimal? CobAllowedAmount { get; set; }
    public decimal? CobPaidAmount { get; set; }
    public decimal? PatientCostShareAmount { get; set; }
    public string? PatientCostShareRaw { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? BenefitPayable { get; set; }
    public List<string> ExCodes { get; set; } = [];
    public string? QPA { get; set; }
}

public sealed class HealthNetClaimTotalsModel
{
    public bool HasPrintedTotalRow { get; set; }
    public decimal? BilledAmount { get; set; }
    public decimal? AmountNotAllowed { get; set; }
    public decimal? AllowedAmount { get; set; }
    public decimal? CobAllowedAmount { get; set; }
    public decimal? CobPaidAmount { get; set; }
    public decimal? PatientCostShareAmount { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? BenefitPayable { get; set; }
}

public sealed class HealthNetClaimPageFinancials
{
    public decimal? PreviouslyPaidAmount { get; set; }
    public decimal? TotalOverpayment { get; set; }
    public HealthNetClaimTotalsModel? Totals { get; set; }
}

public sealed class HealthNetClaimModel
{
    public int ClaimIndex { get; set; }
    public int PageNumber { get; set; }
    public int? HeaderPageNumber { get; set; }
    public List<int> ServicePageNumbers { get; set; } = [];
    public string? PatientNameRaw { get; set; }
    public string? AccountNoRaw { get; set; }
    public string? SubIdCinRaw { get; set; }
    public string? ClaimNo { get; set; }
    public DateTime? ReceiptDate { get; set; }
    public string? Npi { get; set; }
    public decimal? ValueCodeAmount { get; set; }
    public decimal? PreviouslyPaidAmount { get; set; }
    public decimal? TotalOverpayment { get; set; }
    public string? RawClaimText { get; set; }
    public List<HealthNetClaimLineModel> ServiceLines { get; set; } = [];
    public HealthNetClaimTotalsModel Totals { get; set; } = new();
}

public sealed class HealthNetSummaryModel
{
    public string? MemberPlanCode { get; set; }
    public int? TotalClaims { get; set; }
    public decimal? TotalClaimsPayable { get; set; }
    public decimal? TotalCheckAmount { get; set; }
}

public sealed class HealthNetExplanationCodeModel
{
    public string? Code { get; set; }
    public string? Description { get; set; }
}

public sealed class HealthNetOCRResultViewModel
{
    public string? FileName { get; set; }
    public string? FileHash { get; set; }
    public int PageCount { get; set; }
    public HealthNetProcessingStatus ProcessingStatus { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public HealthNetDocumentHeaderModel Header { get; set; } = new();
    public List<HealthNetClaimModel> Claims { get; set; } = [];
    public HealthNetSummaryModel Summary { get; set; } = new();
    public List<HealthNetExplanationCodeModel> ExplanationCodes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
