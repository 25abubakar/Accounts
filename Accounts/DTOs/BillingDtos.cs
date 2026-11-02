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
}

// ── Child 2: Claims DTOs ───────────────────────────────────────────────────────

public sealed class BillingPaymentRowDto
{
    public int Id { get; set; }
    public string AppointmentId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string PatientType { get; set; } = string.Empty;
    public string DateOfService { get; set; } = string.Empty;
    public string Office { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string Insurance { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string SubmissionDate { get; set; } = string.Empty;
    public string CptIcd { get; set; } = string.Empty;
    public string GroupCodes { get; set; } = string.Empty;
    public string Modifier { get; set; } = string.Empty;
    public decimal? ClaimAmount { get; set; }
    public decimal? InsuranceAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? PatientAmount { get; set; }
    public decimal? BalanceAmount { get; set; }
    public decimal? CalculatedBalance { get; set; }
    public string Status { get; set; } = string.Empty;
    public string EobStatus { get; set; } = string.Empty;
    public bool EobUploaded { get; set; }
    public string EobCheckNumber { get; set; } = string.Empty;
    public string Attachment { get; set; } = string.Empty;
}

public sealed class BillingEobRowDto
{
    public int Id { get; set; }
    public string CheckEft { get; set; } = string.Empty;
    public string StatementDate { get; set; } = string.Empty;
    public string CreatedDate { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string Attachment { get; set; } = string.Empty;
    public decimal? TotalAmount { get; set; }
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
    public string ProcedureCode { get; set; } = string.Empty;
    public decimal? ClaimAmount { get; set; }
    public decimal? InsuranceAmount { get; set; }
    public string? Modifiers { get; set; }
}

public sealed class PostClaimPaymentRequest
{
    public string? EobCheckNo { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public string? Remarks { get; set; }
    public bool IsClosed { get; set; }
    public bool IsReProcess { get; set; }
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

