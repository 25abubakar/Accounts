namespace Accounts.Models.SpListRows;

public sealed class PayBenefitRuleListRow
{
    public int Id { get; set; }
    public string BenRef { get; set; } = "";
    public string BenefitsType { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Company { get; set; }
    public string? Entitled { get; set; }
    public string? Contract { get; set; }
    public string? Frequency { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal MaxExp { get; set; }
    public string? SerStatus { get; set; }
    public string? Scale { get; set; }
    public DateOnly? Wef { get; set; }
    public decimal MinService { get; set; }
    public decimal MinSalary { get; set; }
    public decimal MaxPh { get; set; }
    public decimal MinPh { get; set; }
    public bool Ineligible { get; set; }
    public string? ShareType { get; set; }
    public decimal CovShare { get; set; }
    public decimal StaffShare { get; set; }
    public int? OrganizationId { get; set; }
    public string? CompName { get; set; }
}

public sealed class PayBenefitParameterListRow
{
    public int Id { get; set; }
    public string RuleName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ref { get; set; } = "";
    public string? Entitled { get; set; }
    public int BenefitId { get; set; }
    public string? FreqId { get; set; }
    public decimal MinSer { get; set; }
    public string AmtType { get; set; } = "";
    public string PayTypeId { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
    public decimal MaxPh { get; set; }
    public decimal MinPh { get; set; }
    public decimal CoyShare { get; set; }
    public decimal StaffShare { get; set; }
    public string BenefitsType { get; set; } = "";
    public int? BonusMonth { get; set; }
    public DateOnly? InstallmentStart { get; set; }
    public DateOnly? InstallmentEnd { get; set; }
    public decimal BasicPercentage { get; set; }
    public decimal ServicePercentage { get; set; }
    public decimal ServiceYears { get; set; }
    public decimal AssessmentPercentage { get; set; }
    public decimal AttendancePercentage { get; set; }
    public decimal LeavePercentage { get; set; }
    public decimal DisciplinePercentage { get; set; }
    public int Installments { get; set; }
}

public sealed class PayBonusRuleListRow
{
    public int Id { get; set; }
    public string reference { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Scale { get; set; }
    public string? Frequency { get; set; }
    public decimal maximumExpense { get; set; }
    public decimal MinimumService { get; set; }
    public decimal MinimumSalary { get; set; }
    public int? OrganizationId { get; set; }
    public string? Company { get; set; }
    public string? Entitled { get; set; }
    /// <summary>Resolved from Bonus Distribution InstallmentStart / Month + rule ValidFrom (no UI calendar).</summary>
    public int periodYear { get; set; }
    public int periodMonth { get; set; }
    public DateOnly? installmentStart { get; set; }
    public DateOnly? installmentEnd { get; set; }
    public int installments { get; set; }
}

// Stored-procedure projection only. Keep this flat: SqlQueryRaw cannot materialize
// EF entity navigation properties such as PayrollBonusLine.BonusRun.
public sealed class PayBonusLineListRow
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long BonusRunId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? StaffId { get; set; }
    public string EmployeeNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public DateOnly? DateOfJoining { get; set; }
    public string? Scale { get; set; }
    public bool IsValid { get; set; }
    public string? ValidationMessage { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal BasicBonus { get; set; }
    public decimal AttendanceBonus { get; set; }
    public decimal LeaveBonus { get; set; }
    public decimal DisciplineBonus { get; set; }
    public decimal AssessmentBonus { get; set; }
    public decimal ServiceBonus { get; set; }
    public decimal ServiceYears { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal TotalBonus { get; set; }
    public decimal BasicPercent { get; set; }
    public decimal ServicePercent { get; set; }
    public decimal AttendancePercent { get; set; }
    public decimal AssessmentPercent { get; set; }
    public decimal LeavePercent { get; set; }
    public decimal DisciplinePercent { get; set; }
    public decimal InstallmentAmount { get; set; }
    public int Installment { get; set; }
    public int CurrentInstallmentNo { get; set; }
    public int PaidInstallmentCount { get; set; }
    public bool IsApproved { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidOnUtc { get; set; }
    public bool IsInactive { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

/// <summary>
/// Result of dbo.usp_Pay_BonusGenerate_Candidates.
/// Min_Service is months from DOJ; ServiceMonths is completed calendar months as of period end.
/// </summary>
public sealed class PayBonusGenerateCandidateRow
{
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public string EmployeeNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public DateOnly? DateOfJoining { get; set; }
    public string? Scale { get; set; }
    public int ServiceMonths { get; set; }
    public decimal ServiceYears { get; set; }
    public decimal BaseSalary { get; set; }
    public int? ParameterId { get; set; }
    public bool IsValid { get; set; }
    public string? ValidationMessage { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal BasicPercent { get; set; }
    public decimal ServicePercent { get; set; }
    public decimal AttendancePercent { get; set; }
    public decimal AssessmentPercent { get; set; }
    public decimal LeavePercent { get; set; }
    public decimal DisciplinePercent { get; set; }
    public int Installment { get; set; }
    public int CurrentInstallmentNo { get; set; }
}

public sealed class PayEobiSettingListRow
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public decimal EmployeeRatePercentage { get; set; }
    public decimal EmployerRatePercentage { get; set; }
    public decimal MinimumWage { get; set; }
    public decimal? MaximumContributionBase { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

public sealed class PayEobiEligibilityListRow
{
    public int id { get; set; }
    public Guid personId { get; set; }
    public string staffId { get; set; } = "";
    public string fullName { get; set; } = "";
    public string? eobiNo { get; set; }
    public string? eobiNumber { get; set; }
    public string? department { get; set; }
    public DateOnly? doj { get; set; }
    public bool isOn { get; set; }
    public bool isEligible { get; set; }
    public DateOnly effectiveFrom { get; set; }
    public DateOnly? effectiveTo { get; set; }
    public string? remarks { get; set; }
}

public sealed class PayStaffMonthlyEobiListRow
{
    public long id { get; set; }
    public string? eobiRef { get; set; }
    public string staffId { get; set; } = "";
    public string fullName { get; set; } = "";
    public string? department { get; set; }
    public DateOnly? doj { get; set; }
    public decimal coyShare { get; set; }
    public decimal staffShare { get; set; }
    public decimal totAmount { get; set; }
    public string? remarks { get; set; }
    public bool isApproved { get; set; }
    public bool isPaid { get; set; }
}

public sealed class PayStaffTaxListRow
{
    public long id { get; set; }
    public string taxId { get; set; } = "";
    public Guid personId { get; set; }
    public Guid staffGuid { get; set; }
    public string staffId { get; set; } = "";
    public string fullName { get; set; } = "";
    public string? department { get; set; }
    public string? designation { get; set; }
    public DateOnly dateFrom { get; set; }
    public DateOnly dateTo { get; set; }
    public string? frequency { get; set; }
    public decimal min { get; set; }
    public decimal net { get; set; }
    public decimal maxSalary { get; set; }
    public decimal incomePay { get; set; }
    public int taxMonths { get; set; }
    public decimal adjustment { get; set; }
    public decimal taxableIncome { get; set; }
    public decimal taxAmount { get; set; }
    public decimal monthlyTaxAmt { get; set; }
    public int payMonth { get; set; }
    public decimal netTax { get; set; }
    public decimal monthlyNetTax { get; set; }
    public decimal extraAmount { get; set; }
    public decimal monthlyPay { get; set; }
    public int totMonth { get; set; }
    public decimal dedPercentage { get; set; }
    public bool isActive { get; set; }
}

public sealed class PayStaffTaxCandidateListRow
{
    public Guid PersonId { get; set; }
    public Guid StaffGuid { get; set; }
    public string StaffId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Designation { get; set; } = "";
    public decimal MonthlyPay { get; set; }
}

public sealed class PayTaxParameterListRow
{
    public int Id { get; set; }
    public decimal minTaxAmt { get; set; }
    public decimal minMonthlyPay { get; set; }
    public decimal dedPercentage { get; set; }
    public bool isActive { get; set; }
}

public sealed class PayTaxSlabListRow
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string TaxYear { get; set; } = "";
    public string SlabName { get; set; } = "";
    public decimal FromAmount { get; set; }
    public decimal? ToAmount { get; set; }
    public string RateType { get; set; } = "";
    public decimal FixedTaxAmount { get; set; }
    public decimal RatePercentage { get; set; }
    public decimal TotTax { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

public sealed class PaySalaryScaleListRow
{
    public int Id { get; set; }
    public string ScaleName { get; set; } = "";
    public int DisplayOrder { get; set; }
    public int? RuleRegistrationId { get; set; }
    public string? ApplicableType { get; set; }
    public int? ApplyAfter { get; set; }
    public int? IncrementMonth { get; set; }
    public string? IncrementMonths { get; set; }
    public string? ScaleType { get; set; }
    public string? PayMode { get; set; }
    public string? FrequencyType { get; set; }
    public string? ContractType { get; set; }
    public string? RateType { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal MaximumSalary { get; set; }
    public decimal YearlyIncrement { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal CurrentPay { get; set; }
    public decimal MedicalAllowance { get; set; }
    public decimal TravellingAllowance { get; set; }
    public decimal Other { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PayRuleRegistrationListRow
{
    public int Id { get; set; }
    public string RuleType { get; set; } = "";
    public string Name { get; set; } = "";
    // PayScaleRuleRegistrations stores these as SQL datetime/DateTime. SqlQueryRaw
    // requires the projection CLR type to match the provider value exactly.
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

public sealed class PayAllowanceListRow
{
    public int Id { get; set; }
    public string AllowanceRef { get; set; } = "";
    public string AllowName { get; set; } = "";
    public int? SalaryScaleId { get; set; }
    public string? Scale { get; set; }
    public int AllowanceTypeId { get; set; }
    public string? AllowanceType { get; set; }
    public string? ContractType { get; set; }
    public string? FrequencyType { get; set; }
    public string? RateType { get; set; }
    public string? PayType { get; set; }
    public decimal PayValue { get; set; }
    public decimal CalculatedValue { get; set; }
    public string AllowanceCategory { get; set; } = "";
    public int? DesignationId { get; set; }
    public string? DesignationName { get; set; }
    public int? ShiftLookupValueId { get; set; }
    public string? ShiftCode { get; set; }
    public string? ShiftName { get; set; }
}

public sealed class PayTadaListRow
{
    public int Id { get; set; }
    public string TadaRef { get; set; } = "";
    public string Name { get; set; } = "";
    public int SalaryScaleId { get; set; }
    public string? SalaryScaleName { get; set; }
    public int TadaTypeId { get; set; }
    public string? TadaType { get; set; }
    public string? ContractType { get; set; }
    public string? FrequencyType { get; set; }
    public string? RateType { get; set; }
    public decimal PayValue { get; set; }
    public decimal CalculatedValue { get; set; }
}

public sealed class PayLeaveListRow
{
    public int Id { get; set; }
    public string LeaveRef { get; set; } = "";
    public string Name { get; set; } = "";
    public int SalaryScaleId { get; set; }
    public string? SalaryScaleName { get; set; }
    public int LeaveTypeId { get; set; }
    public string? LeaveType { get; set; }
    public string? ContractType { get; set; }
    public string? FrequencyType { get; set; }
    public string? RateType { get; set; }
    public decimal TotalLeave { get; set; }
    public string? ApplicableType { get; set; }
    public decimal ApplicableAfter { get; set; }
    public string? ValueType { get; set; }
    public string? Type { get; set; }
    public decimal ApplicableValue { get; set; }
}

public sealed class PayPackageListRow
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SalaryScaleId { get; set; }
    public string? SalaryScaleName { get; set; }
    public int PayRuleId { get; set; }
    public string? PayRuleName { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public string? AllowanceRef { get; set; }
    public string? TadaRef { get; set; }
    public string? LeaveRef { get; set; }
}

public sealed class AttendanceRuleSettingListRow
{
    public int Id { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string? AttendanceTypeCode { get; set; }
    public string? AttendanceTypeName { get; set; }
    public string? Reference { get; set; }
    public string? RuleName { get; set; }
    public int WorkingMinutes { get; set; }
    public int BeforeCheckInMinutes { get; set; }
    public int AfterCheckOutMinutes { get; set; }
    public int CheckInAdjustMinutes { get; set; }
    public int CheckOutAdjustMinutes { get; set; }
    public int AbsentAfterShiftStartMinutes { get; set; }
    public int EarlyCheckoutAbsentAfterMinutes { get; set; }
    public int MissingCheckoutAfterShiftEndMinutes { get; set; }
    public int CameraVerificationToleranceMinutes { get; set; }
    public int AccountLockAbsentDays { get; set; }
    public decimal WeekendChargeValue { get; set; }
    public int AdjustAbsentDays { get; set; }
    public int ExtremeLateAfterMinutes { get; set; }
    public int? PlatformLateStatusId { get; set; }
    public int? PlatformExtremeLateStatusId { get; set; }
    public int ExtremeEarlyDepartureAfterMinutes { get; set; }
    public int? PlatformEarlyDepartureStatusId { get; set; }
    public int? PlatformExtremeEarlyDepartureStatusId { get; set; }
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; }
    public bool IsOvertimeBonusActive { get; set; }
    public bool IsCompletedLateDeductionActive { get; set; }
    public decimal CompletedLateDeductionPercentage { get; set; }
    public string? Remarks { get; set; }
}

public sealed class AttendanceMapRuleListRow
{
    public int Id { get; set; }
    public Guid StaffId { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string? AttendanceTypeCode { get; set; }
    public string? AttendanceTypeName { get; set; }
    public string? ShiftCode { get; set; }
    public string? ShiftName { get; set; }
    public string? TimeFrom { get; set; }
    public string? TimeTo { get; set; }
    public bool IsOpenAttendance { get; set; }
}

public sealed class AttendanceLoginReportRow
{
    public long Id { get; set; }
    public Guid? StaffId { get; set; }
    public Guid? PersonId { get; set; }
    public string EmployeeNumber { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Designation { get; set; } = "";
    public DateOnly Date { get; set; }
    public string LoginTime { get; set; } = "";
    public string? LogoutTime { get; set; }
    public int WorkingMinutes { get; set; }
    public string? Source { get; set; }
    public string? IpAddress { get; set; }
    public string? Remarks { get; set; }
}

public sealed class AttendanceTimingChartStaffRow
{
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public string EmployeeId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Designation { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public bool IsCurrentUser { get; set; }
    public bool CanEditTiming { get; set; }
}

public sealed class AttendanceTimingChartScheduleRow
{
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public string EmployeeId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Designation { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftEndTime { get; set; }
    public long? ScheduleId { get; set; }
    public DateOnly? ScheduleDate { get; set; }
    public int? HolidayTypeId { get; set; }
    public string? HolidayTypeCode { get; set; }
    public string? HolidayTypeName { get; set; }
    public string? TimeFrom { get; set; }
    public string? TimeTo { get; set; }
    public int? WorkingMinutes { get; set; }
    public bool? IsOn { get; set; }
    public bool IsOverride { get; set; }
}

/// <summary>Result of dbo.usp_Assessment_FinalList — tenant-wide staff assessment grid.</summary>
public sealed class AssessmentFinalListRow
{
    public int Id { get; set; }
    public long? AssessmentId { get; set; }
    public Guid PersonId { get; set; }
    public Guid StaffGuid { get; set; }
    public string StaffId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Department { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public int AssessmentYear { get; set; }
    public int AssessmentMonth { get; set; }
    public byte? Rating { get; set; }
    public decimal? Amount { get; set; }
    public string? Remarks { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? SubmittedDateUtc { get; set; }
    public Guid? AssessorPersonId { get; set; }
    public string AssessorName { get; set; } = "";
    public bool IsFinalApproved { get; set; }
    public string? FinalApprovedByName { get; set; }
    public DateTime? FinalApprovedDateUtc { get; set; }
    public bool IsPostedToPayroll { get; set; }
    public long? PostedPayrollRunId { get; set; }
    public DateTime? PostedToPayrollDateUtc { get; set; }
    public string Status { get; set; } = "";
}
