using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accounts.DTOs
{
    // ── Address ───────────────────────────────────────────────────────────────

    public class AddressDto
    {
        public string? AddressLine { get; set; }
        public string? Country     { get; set; }
        public string? Province    { get; set; }
        public string? District    { get; set; }
        public string? City        { get; set; }
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
        public string    FullName      { get; set; } = string.Empty;
        public string?   Phone         { get; set; }
        public string?   Email         { get; set; }
        public string?   Gender        { get; set; }
        public DateTime? DateOfBirth   { get; set; }
        public string?   MaritalStatus { get; set; }
        public int       BranchId      { get; set; }
        // Password is NOT required — auto-generated as LoginId@

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

    public class UpdatePersonDto
    {
        public string    FullName      { get; set; } = string.Empty;
        public string?   Phone         { get; set; }
        public string?   Email         { get; set; }
        public string?   Gender        { get; set; }
        public DateTime? DateOfBirth   { get; set; }
        public string?   MaritalStatus { get; set; }
        public AddressDto? CurrentAddress   { get; set; }
        public AddressDto? PermanentAddress { get; set; }
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
        public string?   PhotoUrl      { get; set; }
        public bool      IsHired       { get; set; }
        public string    RegisteredAt  { get; set; } = string.Empty;
        public int?      BranchId      { get; set; }
        public string?   BranchName    { get; set; }
        public string?   CompanyName   { get; set; }
        public string?   CountryName   { get; set; }
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
        public string?   Email         { get; set; }
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
        public AddressResponseDto CurrentAddress   { get; set; } = new();
        public AddressResponseDto PermanentAddress { get; set; } = new();
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
