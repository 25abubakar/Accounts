using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accounts.DTOs
{
    public sealed class SetPersonStatusDto
    {
        public bool IsActive { get; set; }
    }

    // ── Address ───────────────────────────────────────────────────────────────

    public class AddressDto
    {
        [JsonPropertyName("addressLine")]
        public string? AddressLine { get; set; }
        [JsonPropertyName("country")]
        public string? Country     { get; set; }
        [JsonPropertyName("province")]
        public string? Province    { get; set; }
        [JsonPropertyName("district")]
        public string? District    { get; set; }
        [JsonPropertyName("city")]
        public string? City        { get; set; }
        [JsonPropertyName("postalCode")]
        public string? PostalCode  { get; set; }
    }

    public class AddressResponseDto
    {
        public string? AddressLine { get; set; }
        public string? Country     { get; set; }
        public string? Province    { get; set; }
        public string? District    { get; set; }
        public string? City        { get; set; }
        public string? PostalCode  { get; set; }
    }

    // ── Register / Update ─────────────────────────────────────────────────────

    public class RegisterPersonDto
    {
        [Required]
        public string    FullName      { get; set; } = string.Empty;
        
        public string?   Phone         { get; set; }
        public string?   Email         { get; set; }
        [EmailAddress]
        [MaxLength(256)]
        public string?   PersonalEmail { get; set; }
        public string?   Gender        { get; set; }
        public DateTime? DateOfBirth   { get; set; }
        public string?   MaritalStatus { get; set; }
        public string? ShiftStartTime { get; set; }
        public string? ShiftEndTime { get; set; }
        public string? TimeZoneId { get; set; }
        
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "BranchId is required")]
        public int       BranchId      { get; set; }
        
        // Password is NOT required — auto-generated as LoginId@

        // Use object to accept either JSON object or JSON string from frontend
        [JsonPropertyName("currentAddress")]
        public object? CurrentAddressRaw { get; set; }

        [JsonPropertyName("permanentAddress")]
        public object? PermanentAddressRaw { get; set; }

        [JsonIgnore]
        public AddressDto? CurrentAddress => ParseAddress(CurrentAddressRaw);

        [JsonIgnore]
        public AddressDto? PermanentAddress => ParseAddress(PermanentAddressRaw);

        private static AddressDto? ParseAddress(object? raw)
        {
            if (raw == null) return null;

            try
            {
                // Handle JsonElement (when sent as proper JSON object)
                if (raw is JsonElement el)
                {
                    if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined)
                        return null;
                        
                    if (el.ValueKind == JsonValueKind.Object)
                        return JsonSerializer.Deserialize<AddressDto>(el.GetRawText());
                        
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var inner = el.GetString();
                        if (string.IsNullOrWhiteSpace(inner)) return null;
                        return JsonSerializer.Deserialize<AddressDto>(inner);
                    }
                }

                // Handle string (when sent as double-serialized JSON string)
                if (raw is string str)
                {
                    if (string.IsNullOrWhiteSpace(str)) return null;
                    return JsonSerializer.Deserialize<AddressDto>(str);
                }

                // Handle already-deserialized AddressDto (shouldn't happen but just in case)
                if (raw is AddressDto addr)
                    return addr;

                // Last resort: try to serialize and deserialize
                var json = JsonSerializer.Serialize(raw);
                return JsonSerializer.Deserialize<AddressDto>(json);
            }
            catch
            {
                // If all parsing attempts fail, return null
                return null;
            }
        }
    }

    public class UpdatePersonDto
    {
        public string    FullName      { get; set; } = string.Empty;
        public string?   Phone         { get; set; }
        public string?   Email         { get; set; }
        public string?   PersonalEmail { get; set; }
        public string?   Gender        { get; set; }
        public DateTime? DateOfBirth   { get; set; }
        public string?   MaritalStatus { get; set; }
        public string? ShiftStartTime { get; set; }
        public string? ShiftEndTime { get; set; }
        public string? TimeZoneId { get; set; }

        [JsonPropertyName("currentAddress")]
        public JsonElement? CurrentAddressRaw { get; set; }

        [JsonPropertyName("permanentAddress")]
        public JsonElement? PermanentAddressRaw { get; set; }

        [JsonIgnore]
        public AddressDto? CurrentAddress => ParseAddress(CurrentAddressRaw);

        [JsonIgnore]
        public AddressDto? PermanentAddress => ParseAddress(PermanentAddressRaw);

        private static AddressDto? ParseAddress(JsonElement? raw)
        {
            if (raw is null) return null;
            var el = raw.Value;
            if (el.ValueKind == JsonValueKind.Object)
                return JsonSerializer.Deserialize<AddressDto>(el.GetRawText());
            if (el.ValueKind == JsonValueKind.String)
            {
                var inner = el.GetString();
                if (string.IsNullOrWhiteSpace(inner)) return null;
                return JsonSerializer.Deserialize<AddressDto>(inner);
            }
            return null;
        }
    }

    // ── Response ──────────────────────────────────────────────────────────────

    public class PersonDto
    {
        public Guid      PersonId      { get; set; }
        public string    LoginId       { get; set; } = string.Empty;
        public string    FullName      { get; set; } = string.Empty;
        public string?   Gender        { get; set; }
        public DateTime? DateOfBirth   { get; set; }
        public string?   MaritalStatus { get; set; }
        public string?   Phone         { get; set; }
        public string?   Email         { get; set; }
        public string?   PersonalEmail { get; set; }
        public string? ShiftStartTime { get; set; }
        public string? ShiftEndTime { get; set; }
        public string? TimeZoneId { get; set; }
        public string?   PhotoUrl      { get; set; }
        public bool      IsHired       { get; set; }
        public bool      IsActive      { get; set; }
        public string    RegisteredAt  { get; set; } = string.Empty;
        public int?      BranchId      { get; set; }
        public string?   BranchName    { get; set; }
        public string?   CompanyName   { get; set; }
        public string?   CountryName   { get; set; }
        public string?   VacancyCode   { get; set; }
        public string?   JobTitle      { get; set; }
        public string?   Department    { get; set; }
        public AddressResponseDto CurrentAddress   { get; set; } = new();
        public AddressResponseDto PermanentAddress { get; set; } = new();
        public bool               SameAddress      { get; set; }
    }

    public class PersonProfileDto
    {
        public Guid      PersonId      { get; set; }
        public string    LoginId       { get; set; } = string.Empty;
        public string    FullName      { get; set; } = string.Empty;
        public string    Initials      { get; set; } = string.Empty;
        public string?   Gender        { get; set; }
        public DateTime? DateOfBirth   { get; set; }
        public string?   MaritalStatus { get; set; }
        public string?   Phone         { get; set; }
        public string?   UserName         { get; set; }
        public string?   PhotoUrl      { get; set; }
        public DateTime  RegisteredAt  { get; set; }
        public int?      BranchId      { get; set; }
        public string?   BranchName    { get; set; }
        public string?   CompanyName   { get; set; }
        public string?   CountryName   { get; set; }
        public string?   CountryFlag   { get; set; }
        public bool      IsHired       { get; set; }
        public Guid?     StaffId       { get; set; }
        public DateTime? JoiningDate   { get; set; }
        public Guid?     VacancyId     { get; set; }
        public string?   VacancyCode   { get; set; }
        public string?   JobTitle      { get; set; }
        public string?   Department    { get; set; }
        public string? ShiftStartTime { get; set; }
        public string? ShiftEndTime { get; set; }
        public string? TimeZoneId { get; set; }
        public AddressResponseDto CurrentAddress   { get; set; } = new();
        public AddressResponseDto PermanentAddress { get; set; } = new();
    }

    public class PersonEducationDto
    {
        public Guid? EducationId { get; set; }
        public string? EducationLevel { get; set; }
        public string? DegreeTitle { get; set; }
        public string? Institute { get; set; }
        public string? PassingYear { get; set; }
        public string? Grade { get; set; }
        public int SortOrder { get; set; }
    }

    public class PersonExperienceDto
    {
        public Guid? ExperienceId { get; set; }
        public string? CompanyName { get; set; }
        public string? Role { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Summary { get; set; }
        public int SortOrder { get; set; }
    }

    public class PersonHrProfileDto
    {
        public Guid PersonId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? PersonalEmail { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? MaritalStatus { get; set; }
        public string? ShiftStartTime { get; set; }
        public string? ShiftEndTime { get; set; }
        public string? TimeZoneId { get; set; }

        public string? CnicOrLicense { get; set; }
        public string? Nationality { get; set; }
        public string? Race { get; set; }
        public string? Language { get; set; }
        public string? BloodGroup { get; set; }
        public string? Disability { get; set; }
        public string? PoliceStation { get; set; }
        public string? EmergencyContactNo { get; set; }

        public DateTime? MedicalFrom { get; set; }
        public DateTime? MedicalTo { get; set; }
        public string? Treatment { get; set; }
        public string? DiagnosisDisease { get; set; }
        public string? Doctor { get; set; }
        public string? DoctorContactNo { get; set; }

        public string? BankName { get; set; }
        public string? BankBranchName { get; set; }
        public string? BankBranchCode { get; set; }
        public string? SwiftCode { get; set; }
        public string? AccountTitle { get; set; }
        public string? AccountNo { get; set; }
        public string? IbanNo { get; set; }
        public string? BankBranchContactNo { get; set; }
        public string? TaxNumber { get; set; }
        public string? PaymentMode { get; set; }

        public string? InductionType { get; set; }
        public DateTime? JoiningDate { get; set; }
        public DateTime? TrainingFrom { get; set; }
        public DateTime? TrainingTo { get; set; }
        public DateTime? ProbationFrom { get; set; }
        public DateTime? ProbationTo { get; set; }
        public DateTime? ContractFrom { get; set; }
        public DateTime? ContractTo { get; set; }

        public string? WorkingDays { get; set; }
        public string? WorkingHours { get; set; }
        public string? TimingFrom { get; set; }
        public string? TimingTo { get; set; }
        public decimal? PostingPerHour { get; set; }
        public decimal? PostingPerDay { get; set; }
        public DateTime? PromotionFrom { get; set; }
        public DateTime? PromotionTo { get; set; }

        public string? Scale { get; set; }
        public DateTime? ScaleDate { get; set; }
        public decimal? BasicSalary { get; set; }
        public decimal? IncrementSalary { get; set; }
        public decimal? MaxSalary { get; set; }
        public decimal? CurrentPay { get; set; }
        public decimal? AccountsPerDay { get; set; }
        public decimal? AccountsPerHour { get; set; }

        public DateTime? LeaveFrom { get; set; }
        public DateTime? LeaveTo { get; set; }
        public decimal? LeaveEntitled { get; set; }
        public decimal? LeaveAvailed { get; set; }

        public List<PersonEducationDto> Educations { get; set; } = new();
        public List<PersonExperienceDto> Experiences { get; set; } = new();
    }

    // ── Password ──────────────────────────────────────────────────────────────

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword     { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        /// <summary>Leave empty to auto-generate (LoginId@)</summary>
        public string? NewPassword { get; set; }
    }
}
