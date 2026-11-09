namespace Accounts.DTOs;

public sealed class BillingReceiptRozListRow
{
    public long Id { get; set; }
    public int? SNo { get; set; }
    public int? SerialNumber => SNo;
    public string? Ref { get; set; }
    public string? OldRef { get; set; }
    public string TypeName { get; set; } = "Receipt";
    public string Type { get; set; } = "Receipt";
    public string? Category { get; set; }
    public string? Cat_Name => Category;
    public int? CategoryId { get; set; }
    public int? from_AccId { get; set; }
    public string? AccountName { get; set; }
    public string? AcctName => AccountName;
    public string? FromAccountNumber { get; set; }
    public string? AcctNo => FromAccountNumber;
    public int? To_AcctId { get; set; }
    public string? ToAccountName { get; set; }
    public string? ToAcctName => ToAccountName;
    public string? ToAccountNumber { get; set; }
    public string? ToCategory { get; set; }
    public string? To_CatName => ToCategory;
    public string? TransTypeName { get; set; }
    public string? TransModeName { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? InstrumentNumber { get; set; }
    public string? Descriptions { get; set; }
    public string? Discriptions => Descriptions;
    public string? BankReference { get; set; }
    public string? BankLT_Ref => BankReference;
    public string? Bank_Ref => BankReference;
    public string? CheckNumber { get; set; }
    public string? EobCheckNo => CheckNumber;
    public string? CheckNo => CheckNumber;
    public DateOnly TransDate { get; set; }
    public string TransactionDate => TransDate.ToString("yyyy-MM-dd");
    public decimal? Adjustment { get; set; }
    public decimal? Credit { get; set; }
    public decimal? Cr_DbAmt => Credit;
    public decimal? UsdCredit { get; set; }
    public decimal? UsdAmt => UsdCredit;
    public decimal? Qty { get; set; }
    public decimal? Rate { get; set; }
    public string? Status { get; set; }
    public string? StatusName => Status;
    public int? StatusId { get; set; }
    public int? EnterStatusId => StatusId;
    public bool IsDeleted { get; set; }
    public bool IsApproved { get; set; }
    public bool IsSettled { get; set; }
    public string? LibRef { get; set; }
    public string? Lib_Ref => LibRef;
    public string? Remarks { get; set; }
    public string? PatientName { get; set; }
    public string? FullName => PatientName;
    public string? Attachment { get; set; }
    public string? Image { get; set; }
    public int AttachmentCount { get; set; }
}

public sealed class BillingSettleRequest
{
    public List<long> Ids { get; set; } = [];
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? Remarks { get; set; }
}

public sealed class BillingTransferAccountRequest
{
    public long Id { get; set; }
    public int TransferAccountId { get; set; }
}

public sealed class BillingProcessLogRequest
{
    public long EntryId { get; set; }
    public string? EntryRef { get; set; }
    public int? StatusId { get; set; }
    public string? Remarks { get; set; }
    public DateTime? RemindDate { get; set; }
    public int? DepartmentId { get; set; }
    public string? StaffId { get; set; }
    public string? CheckNo { get; set; }
}

public sealed class BillingStatusUpdateRequest
{
    public int StatusId { get; set; }
    public string? Remarks { get; set; }
    public string? PolicyNumber { get; set; }
}

// ── Child 2: Claims DTOs ───────────────────────────────────────────────────────

public sealed class BillingPaymentRowDto
{
    public int Id { get; set; }
    public string? AppointmentId { get; set; } = string.Empty;
    public string? PatientName { get; set; } = string.Empty;
    public string? PatientNumber { get; set; } = string.Empty;
    public string? PatientType { get; set; } = string.Empty;
    public string? DateOfService { get; set; } = string.Empty;
    public string? Office { get; set; } = string.Empty;
    public string? Carrier { get; set; } = string.Empty;
    public string? Insurance { get; set; } = string.Empty;
    public string? PolicyNumber { get; set; } = string.Empty;
    public string? SubmissionDate { get; set; } = string.Empty;
    public string? CptIcd { get; set; } = string.Empty;
    public string? GroupCodes { get; set; } = string.Empty;
    public string? Modifier { get; set; } = string.Empty;
    public decimal? ClaimAmount { get; set; }
    public decimal? InsuranceAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? PatientAmount { get; set; }
    public decimal? BalanceAmount { get; set; }
    public decimal? CalculatedBalance { get; set; }
    public string? Status { get; set; } = string.Empty;
    public string? EobStatus { get; set; } = string.Empty;
    public bool EobUploaded { get; set; }
    public string? EobCheckNumber { get; set; } = string.Empty;
    public string? Attachment { get; set; } = string.Empty;
    public int? StatusId { get; set; }
    public int? EobStatusId { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? EobStatusColorCode { get; set; }
    public string? EobStatusFontColor { get; set; }
    public string? EobUploadDate { get; set; }
}

public sealed class BillingEobRowDto
{
    public int Id { get; set; }
    public string? CheckEft { get; set; } = string.Empty;
    public string? StatementDate { get; set; } = string.Empty;
    public string? CreatedDate { get; set; } = string.Empty;
    public string? StatusName { get; set; } = string.Empty;
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? Attachment { get; set; } = string.Empty;
    public decimal? TotalAmount { get; set; }
    public string? Source { get; set; } = string.Empty;
}

public sealed class CreateBillingClaimRequest
{
    public int? AppointmentId { get; set; }
    public int? PatientId { get; set; }
    public int? CarrierId { get; set; }
    public string? PolicyNumber { get; set; }
    public decimal? TotalBilled { get; set; }
    public decimal? TotalAllowed { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public DateTime? SubmittedDate { get; set; }
    public string? Remarks { get; set; }
    public List<CreateBillingApptCodeRequest> CptCodes { get; set; } = [];
}

public sealed class CreateBillingApptCodeRequest
{
    public int? ProcedureCodeId { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public int Unit { get; set; } = 1;
    public int? TypeId { get; set; }
    public decimal? ClaimAmount { get; set; }
    public decimal? InsuranceRate { get; set; }
    public int? CarrierId { get; set; }
    public int? InsuranceCompanyId { get; set; }
    public string? PolicyNumber { get; set; }
    public string? Modifiers { get; set; }
    public List<CreateBillingGroupCodeRequest> GroupCodes { get; set; } = [];
}

public sealed class CreateBillingGroupCodeRequest
{
    public string GroupCode { get; set; } = string.Empty;
    public decimal? GroupAmount { get; set; }
}

public sealed class PostClaimPaymentRequest
{
    public int? ApptCodeId { get; set; }
    public string? EobCheckNo { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? Coinsurance { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? AllowedAmount { get; set; }
    public DateTime? LoadDate { get; set; }
    public string? Remarks { get; set; }
    public bool IsClosed { get; set; }
    public bool IsReProcess { get; set; }
    public List<CreateBillingGroupCodeRequest> GroupCodes { get; set; } = [];
}

public sealed class BillingClaimCodeDetailDto
{
    public int DetailId { get; set; }
    public int ApptCodeId { get; set; }
    public int ClaimId { get; set; }
    public int? AppointmentId { get; set; }
    public long RowNumber { get; set; }
    public int? Unit { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public string Modifiers { get; set; } = string.Empty;
    public decimal? ClaimAmount { get; set; }
    public decimal? TotalClaimAmount { get; set; }
    public decimal? InsuranceRate { get; set; }
    public decimal? InsuranceAmount { get; set; }
    public decimal? AllowedAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? Coinsurance { get; set; }
    public decimal? Deductible { get; set; }
    public string GroupCodes { get; set; } = string.Empty;
    public decimal? TotalGroupAmount { get; set; }
    public decimal? CalculatedBalance { get; set; }
    public decimal? AdjustmentBalance { get; set; }
    public string EobCheckNumber { get; set; } = string.Empty;
    public string FillSource { get; set; } = string.Empty;
    public string? EobStatus { get; set; }
    public string? EobStatusColorCode { get; set; }
    public string? EobStatusFontColor { get; set; }
    public string? LoadDate { get; set; }
    public string? Remarks { get; set; }
    public string? CarrierName { get; set; }
    public string? InsuranceName { get; set; }
    public string? PolicyNumber { get; set; }
    public string? InsuranceType { get; set; }
    public DateTime? CreatedDate { get; set; }
}

public sealed class BillingClaimLookupsDto
{
    public IReadOnlyList<object> Carriers { get; set; } = [];
    public IReadOnlyList<object> ProcedureCodes { get; set; } = [];
    public IReadOnlyList<object> Statuses { get; set; } = [];
    public IReadOnlyList<object> Offices { get; set; } = [];
    public IReadOnlyList<object> PatientTypes { get; set; } = [];
}

// ── Child 3: Patients & Appointments DTOs ──────────────────────────────────────

public sealed class BillingPatientRowDto
{
    public int Id { get; set; }
    public string? PatientId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? PatientType { get; set; }
    public string? Office { get; set; }
    public string? PatientNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? DateOfBirth { get; set; }
    public string? DateOfService { get; set; }
    public string? AppointmentStatus { get; set; }
    public string? ProviderName { get; set; }
    public bool IsClaimed { get; set; }
}

public sealed class CreatePatientAppointmentRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PatientNo { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime DateOfService { get; set; }
    public int? OfficeId { get; set; }
    public int? PtTypeId { get; set; }
    public string? ProviderName { get; set; }
    public string? InsuranceName { get; set; }
}

// ── Child 4: Billing Roz Excel DTOs ────────────────────────────────────────────

public sealed class BillingRozExcelRowDto
{
    public int Id { get; set; }
    public string Ref { get; set; } = string.Empty;
    public string Practice { get; set; } = string.Empty;
    public string InsuranceName { get; set; } = string.Empty;
    public string ChequeNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string TransactionDate { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSettled { get; set; }
}

public sealed class BillingRozExcelSummaryDto
{
    public int TotalImported { get; set; }
    public int PendingSettlement { get; set; }
    public int SettledCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal PendingAmount { get; set; }
}

public sealed class BillingClaimLogRowDto
{
    public long Id { get; set; }
    public int ClaimId { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public string? ColorCode { get; set; }
    public string? FontColor { get; set; }
    public string? Remarks { get; set; }
    public string? LogDate { get; set; }
    public DateTime? CreatedDate { get; set; }
}

public sealed class BillingClaimDocumentRowDto
{
    public long Id { get; set; }
    public int ClaimId { get; set; }
    public string? EobCheckNo { get; set; }
    public string? Attachment { get; set; }
    public string? UploadDate { get; set; }
    public DateTime? CreatedDate { get; set; }
}

public sealed class BillingBatchActionResultDto
{
    public bool Success { get; set; } = true;
    public int AffectedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class BillingBatchStatusRequest
{
    public List<int> ClaimIds { get; set; } = new();
    public int StatusId { get; set; }
    public string? Remarks { get; set; }
    public DateTime? PaymentDate { get; set; }
}

public sealed class BillingBatchFlagRequest
{
    public List<int> ClaimIds { get; set; } = new();
    public bool Value { get; set; } = true;
    public string? Remarks { get; set; }
}

public sealed class BillingDateRangeActionRequest
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
}

// ── Billing Services: Child 1 - Re Process DTO ──────────────────────────────
public sealed class BillingReProcessRowDto
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public int AppointmentId { get; set; }
    public bool IsClaimed { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string CptIcd { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public string? SubmissionDate { get; set; }
    public decimal PaidAmount { get; set; }
    public string SubmissionStatus { get; set; } = string.Empty;
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public string GroupCodes { get; set; } = string.Empty;
    public string Attachment { get; set; } = string.Empty;
}

// ── Billing Services: Child 2 - CPT's Processed DTO ─────────────────────────
public sealed class BillingCptsProcessedRowDto
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public int AppointmentId { get; set; }
    public bool IsClaimed { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string CptIcd { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public string? SubmissionDate { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal PatientAmount { get; set; }
    public string SubmissionStatus { get; set; } = string.Empty;
    public string GroupCodes { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public DateTime? ProcessDate { get; set; }
    public DateTime? ReceiveDate { get; set; }
    public string CheckNumber { get; set; } = string.Empty;
    public string Attachment { get; set; } = string.Empty;
}

// ── Billing Services: Child 3 - AR Report DTO ───────────────────────────
public sealed class BillingArReportRowDto
{
    public int Id { get; set; }
    public int? ApptId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public DateTime? SubmissionDate { get; set; }
    public string CptCodes { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public int DaysSinceDos { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
    public string FontColor { get; set; } = string.Empty;
    public string ArStatusName { get; set; } = string.Empty;
    public string ArColorCode { get; set; } = string.Empty;
    public string ArFontColor { get; set; } = string.Empty;
    public DateTime? ProcessDate { get; set; }
    public string LogStatus { get; set; } = string.Empty;
    public string LogStatusColor { get; set; } = string.Empty;
    public string LogStatusFont { get; set; } = string.Empty;
    public DateTime? RemindDate { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

// ── Child 4: Status Rules DTOs ──────────────────────────────────────────────────

public sealed class BillingStatusRuleDto
{
    public int Id { get; set; }
    public string? RuleKey { get; set; }
    public int StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int? MixedDefaultStatusId { get; set; }
    public int? SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class BillingStatusRuleSaveRequestDto
{
    public int Id { get; set; }
    public string? RuleKey { get; set; }
    public int StatusId { get; set; }
    public int? MixedDefaultStatusId { get; set; }
    public int? SortOrder { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class BillingStatusOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class BillingServiceActionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? Id { get; set; }
}






// ── Billing Services: Child 4 - Call List ───────────────────────────────────
public sealed class BillingCallListRowDto
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public int AppointmentId { get; set; }
    public bool IsClaimed { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string CptIcd { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public DateTime? EobDate { get; set; }
    public decimal PaidAmount { get; set; }
    public string EobStatus { get; set; } = string.Empty;
    public string? EobStatusColorCode { get; set; }
    public string? EobStatusFontColor { get; set; }
    public string GroupCodes { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public DateTime? ProcessDate { get; set; }
    public DateTime? RemindDate { get; set; }
}

// ── Billing Services: Child 5 - Closed List ─────────────────────────────────
public sealed class BillingClosedListRowDto
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public int AppointmentId { get; set; }
    public bool IsClaimed { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string CptIcd { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public DateTime? EobDate { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal PatientAmount { get; set; }
    public string EobStatus { get; set; } = string.Empty;
    public string? EobStatusColorCode { get; set; }
    public string? EobStatusFontColor { get; set; }
    public string CheckNumber { get; set; } = string.Empty;
    public string GroupCodes { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public DateTime? ProcessDate { get; set; }
    public DateTime? RemindDate { get; set; }
}

// ── Billing Services: Child 6 - Master List (patient-appointment level) ──────
public sealed class BillingMasterListRowDto
{
    public int Id { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public string Office { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string DateOfService { get; set; } = string.Empty;
    public string AppointmentStatus { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public bool IsClaimed { get; set; }
}

// ── Billing Services: Child 8 - Mics Report ─────────────────────────────────
public sealed class BillingMicsReportRowDto
{
    public int Id { get; set; }
    public int? ApptId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public DateTime? SubmittedDate { get; set; }
    public decimal BilledAmount { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public bool IsCreatedFromEob { get; set; }
    public string Attachment { get; set; } = string.Empty;
    public string EobCheckNo { get; set; } = string.Empty;
    public decimal TotalClaimAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public string CptCodes { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string ArRemarks { get; set; } = string.Empty;
    public int? UpdatedStatusId { get; set; }
    public string UpdatedStatus { get; set; } = string.Empty;
    public string? UpdatedStatusColor { get; set; }
    public string? UpdatedStatusFont { get; set; }
    public DateTime? RemindDate { get; set; }
    public DateTime? ProcessDate { get; set; }
}

// ── Billing Services: Child 9 - Claim Master List (CPT code level) ───────────
public sealed class BillingClaimMasterListRowDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public string Codes { get; set; } = string.Empty;
    public decimal BalanceAmount { get; set; }
    public decimal ClaimAmount { get; set; }
    public decimal InsuranceRate { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? ProcessDate { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public int? PatientId { get; set; }
    public int? CodeId { get; set; }
    public decimal PatientResponsibility { get; set; }
    public int? InsuranceCompanyId { get; set; }
    public int? TypeId { get; set; }
    public decimal TotalClaimAmount { get; set; }
    public int? Unit { get; set; }
    public int? CarrierId { get; set; }
    public int? ClaimId { get; set; }
    public string InsuranceType { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public string InsuranceName { get; set; } = string.Empty;
    public decimal InsuranceAmount { get; set; }
    public string Modifiers { get; set; } = string.Empty;
    public decimal EobAllowedAmount { get; set; }
    public bool IsCreatedFromEobs { get; set; }
    public string PolicyNumber { get; set; } = string.Empty;
    public DateTime? DateOfService { get; set; }
    public decimal CoinsuranceAmount { get; set; }
    public decimal DeductibleAmount { get; set; }
    public int? EobStatusId { get; set; }
    public string EobStatusName { get; set; } = string.Empty;
    public string? EobColorCode { get; set; }
    public string? EobFontColor { get; set; }
    public string FillSource { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public bool IsUpload { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public decimal ClaimPaidAmount { get; set; }
    public decimal ClaimPatientResponsibility { get; set; }
    public DateTime? LoadDate { get; set; }
    public int? PaymentId { get; set; }
    public string EobRemarks { get; set; } = string.Empty;
    public bool IsReProcess { get; set; }
    public bool IsClosed { get; set; }
    public string CheckNumber { get; set; } = string.Empty;
    public string GroupCodes { get; set; } = string.Empty;
    public DateTime? WorkProcessDate { get; set; }
    public DateTime? RemindDate { get; set; }
}

// ── Billing Services: Child 11 - Code Usage ──────────────────────────────────
public sealed class BillingCodeUsageRowDto
{
    public int RowNo { get; set; }
    public int? ApptCodeId { get; set; }
    public int? AppointmentId { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public decimal TotalClaimAmount { get; set; }
    public decimal InsuranceRate { get; set; }
    public decimal InsuranceAmount { get; set; }
    public decimal EobAllowedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal PatientResponsibility { get; set; }
    public decimal CoinsuranceAmount { get; set; }
    public decimal DeductibleAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string Modifiers { get; set; } = string.Empty;
    public int? Unit { get; set; }
    public string PatientNumber { get; set; } = string.Empty;
    public int? PatientId { get; set; }
    public DateTime? DateOfService { get; set; }
    public string CarrierName { get; set; } = string.Empty;
    public string InsuranceName { get; set; } = string.Empty;
    public string InsuranceType { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public int? EobStatusId { get; set; }
    public string FillSource { get; set; } = string.Empty;
    public DateTime? ProcessDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public int? AmountDetailId { get; set; }
    public int? ClaimId { get; set; }
    public decimal DetailPaidAmount { get; set; }
    public decimal DetailPatientResponsibility { get; set; }
    public decimal DetailAllowedAmount { get; set; }
    public decimal DetailCoinsurance { get; set; }
    public decimal DetailDeductible { get; set; }
    public string EobCheckNumber { get; set; } = string.Empty;
    public DateTime? LoadDate { get; set; }
    public string EobRemarks { get; set; } = string.Empty;
    public bool IsReProcess { get; set; }
    public bool IsClosed { get; set; }
    public decimal? RemovedAmount { get; set; }
    public int? InactiveDays { get; set; }
}
