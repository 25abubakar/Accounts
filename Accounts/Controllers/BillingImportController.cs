using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[Authorize]
[ApiController]
[Route("[controller]/[action]")]
[Route("api/[controller]/[action]")]
public class BillingImportController : ControllerBase
    {
        // Existing constructor and fields...
        // NEW: Delete rows where patient name or patient number is missing (null/empty)
        // This endpoint removes orphaned patient/claim records for the current tenant.
        [HttpDelete]
        public async Task<IActionResult> DeleteBlankPatients(CancellationToken ct = default)
        {
            try
            {
                var tenantId = CurrentTenantId;
                // Delete claims that reference patients with missing PatientNo
                var deleteClaimsSql = @"
                    DELETE c
                    FROM dbo.tblInsPTClaims c
                    INNER JOIN dbo.tblPatient p ON c.PatientId = p.PatientId
                    WHERE p.TenantId = @TenantId
                      AND (p.PatientNo IS NULL OR LTRIM(RTRIM(p.PatientNo)) = '')
                ";
                // Delete patients lacking PatientNo
                var deletePatientsSql = @"
                    DELETE FROM dbo.tblPatient
                    WHERE TenantId = @TenantId
                      AND (PatientNo IS NULL OR LTRIM(RTRIM(PatientNo)) = '')
                ";
                // Delete persons lacking a full name (optional safety check)
                var deletePersonsSql = @"
                    DELETE FROM dbo.tblPerson
                    WHERE TenantId = @TenantId
                      AND (FullName IS NULL OR LTRIM(RTRIM(FullName)) = '')
                ";
                using var conn = _db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync(ct);

                // Execute in a single transaction for safety
                using var tran = await conn.BeginTransactionAsync(ct);
                using var cmd1 = conn.CreateCommand();
                cmd1.Transaction = tran;
                cmd1.CommandText = deleteClaimsSql;
                cmd1.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@TenantId", tenantId));
                await cmd1.ExecuteNonQueryAsync(ct);

                using var cmd2 = conn.CreateCommand();
                cmd2.Transaction = tran;
                cmd2.CommandText = deletePatientsSql;
                cmd2.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@TenantId", tenantId));
                await cmd2.ExecuteNonQueryAsync(ct);

                using var cmd3 = conn.CreateCommand();
                cmd3.Transaction = tran;
                cmd3.CommandText = deletePersonsSql;
                cmd3.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@TenantId", tenantId));
                await cmd3.ExecuteNonQueryAsync(ct);

                await tran.CommitAsync(ct);
                return Ok(new { success = true, message = "Blank patient/claim rows removed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteBlankPatients error");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenantService;
    private readonly ILogger<BillingImportController> _logger;

    public BillingImportController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ApplicationDbContext db,
        ITenantService tenantService,
        ILogger<BillingImportController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _db = db;
        _tenantService = tenantService;
        _logger = logger;
    }

    private int CurrentTenantId => _tenantService.TenantId
        ?? throw new InvalidOperationException("An authenticated tenant is required for billing import.");

    private async Task<HttpResponseMessage> SendToOldApiAsync(string path, CancellationToken ct)
    {
        var baseUrl = _configuration["LTAccounts:BaseUrl"];
        var apiKey = _configuration["LTAccounts:ApiKey"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("LT Accounts API is not configured in appsettings.json (LTAccounts:BaseUrl / LTAccounts:ApiKey).");
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);

        var targetUri = baseUrl.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path);
        using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
        request.Headers.Add("X-LT-Billing-Key", apiKey);

        return await client.SendAsync(request, ct);
    }

    [HttpGet]
    public async Task<IActionResult> GetBillingCount(
        [FromQuery] string fromDate,
        [FromQuery] string toDate,
        CancellationToken ct)
    {
        try
        {
            var path = $"/BillingSync/GetBillingCount?dateFrom={Uri.EscapeDataString(fromDate ?? "")}&dateTo={Uri.EscapeDataString(toDate ?? "")}";
            using var response = await SendToOldApiAsync(path, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Old LT Accounts count failed with status {StatusCode}: {Error}", response.StatusCode, errorText);
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    message = $"Old LT Accounts API error ({(int)response.StatusCode}): {errorText}"
                });
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var json = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: ct);
            return Ok(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetBillingCount error");
            return StatusCode(500, new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> FetchBillingBatch(
        [FromQuery] string fromDate,
        [FromQuery] string toDate,
        [FromQuery] long afterId,
        [FromQuery] long maxBillingId,
        [FromQuery] int pageSize = 1000,
        CancellationToken ct = default)
    {
        try
        {
            var path = $"/BillingSync/GetBillingData?dateFrom={Uri.EscapeDataString(fromDate ?? "")}&dateTo={Uri.EscapeDataString(toDate ?? "")}&afterId={afterId}&maxBillingId={maxBillingId}&pageSize={pageSize}";
            using var response = await SendToOldApiAsync(path, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Old LT Accounts fetch batch failed with status {StatusCode}: {Error}", response.StatusCode, errorText);
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    message = $"Old LT Accounts API error ({(int)response.StatusCode}): {errorText}"
                });
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
            if (!success)
            {
                var msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() : "Unable to fetch billing batch.";
                return BadRequest(new { success = false, message = msg });
            }

            var returnedCount = root.TryGetProperty("returnedCount", out var rcProp) ? rcProp.GetInt32() : 0;
            var nextId = root.TryGetProperty("nextId", out var nProp) ? nProp.GetInt64() : afterId;
            var hasMore = root.TryGetProperty("hasMore", out var hmProp) && hmProp.GetBoolean();

            var savedCount = 0;
            var repairedCount = 0;
            var skippedCount = 0;
            if (root.TryGetProperty("records", out var recordsElement) && recordsElement.ValueKind == JsonValueKind.Array)
            {
                var tenantId = CurrentTenantId;

                // Inspect whether the payload is Claims data or Roznamcha
                var firstRecord = recordsElement.EnumerateArray().FirstOrDefault();
                var isClaimsBatch = firstRecord.ValueKind != JsonValueKind.Undefined &&
                    (firstRecord.TryGetProperty("claimId", out _) || firstRecord.TryGetProperty("ClaimId", out _) ||
                     firstRecord.TryGetProperty("procedureCodes", out _) || firstRecord.TryGetProperty("ProcedureCodes", out _));

                if (isClaimsBatch)
                {
                    var result = await SaveClaimsBatchAsync(recordsElement, tenantId, ct);
                    savedCount = result.Inserted;
                    repairedCount = result.Repaired;
                    skippedCount = result.Skipped;
                }
                else
                {
                    savedCount = await SaveRoznamchaBatchAsync(recordsElement, tenantId, ct);
                }
            }

            return Ok(new
            {
                success = true,
                returnedCount,
                savedCount,
                repairedCount,
                skippedCount,
                nextId,
                hasMore
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchBillingBatch error");
            return StatusCode(500, new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    private sealed record ClaimsImportResult(int Inserted, int Repaired, int Skipped);

    private async Task<ClaimsImportResult> SaveClaimsBatchAsync(JsonElement recordsElement, int tenantId, CancellationToken ct)
    {
        var orgId = await _db.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => x.OrganizationTreeId)
            .SingleAsync(ct);
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        var inserted = 0;
        var repaired = 0;
        var skipped = 0;
        foreach (var rec in recordsElement.EnumerateArray())
        {
            var sourceClaimId = GetIntProp(rec, "ClaimId", "claimId", "Id", "id");
            if (!sourceClaimId.HasValue || sourceClaimId.Value <= 0)
            {
                skipped++;
                continue;
            }

            var patientId = GetIntProp(rec, "PatientId", "patientId");
            var personId = GetIntProp(rec, "PersonId", "personId");
            var apptId = GetIntProp(rec, "AppointmentId", "appointmentId", "ApptId", "apptId");

            var firstName = GetStringProp(rec, "FirstName", "firstName");
            var lastName = GetStringProp(rec, "LastName", "lastName");
            var fullName = GetStringProp(rec, "FullName", "fullName", "PatientName", "patientName", "PtName", "ptName");
            if (string.IsNullOrWhiteSpace(fullName) && (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName)))
            {
                fullName = $"{firstName} {lastName}".Trim();
            }

            var phone = GetStringProp(rec, "PhoneNumber", "phoneNumber");
            var email = GetStringProp(rec, "Email", "email");
            var dob = ParseDateTime(GetStringProp(rec, "DOB", "dob"));
            var gender = GetBoolProp(rec, "Gender", "gender");

            var patientNo = GetStringProp(rec, "PatientNo", "patientNo", "PtNo", "ptNo", "PatientNumber", "patientNumber");
            var patientOfficeId = GetIntProp(rec, "PatientOfficeId", "patientOfficeId");
            var officeName = GetStringProp(rec, "OfficeName", "officeName", "Office", "office");

            var dos = ParseDateTime(GetStringProp(rec, "DOS", "dos"));
            var providerName = GetStringProp(rec, "ProviderName", "providerName");
            var insuranceName = GetStringProp(rec, "InsuranceName", "insuranceName");
            var carrierName = GetStringProp(rec, "CarrierName", "carrierName");
            // Legacy numeric IDs are not stable across databases. Prefer the
            // business name and only retain the source ID as a last fallback.
            var sourceCarrierId = GetIntProp(rec, "CarrierId", "carrierId");
            var carrierId = !string.IsNullOrWhiteSpace(carrierName)
                ? await ResolveCarrierIdAsync(conn, tenantId, orgId, carrierName, ct)
                : sourceCarrierId;
            carrierId ??= sourceCarrierId;
            var appStatusId = GetIntProp(rec, "AppStatusId", "appStatusId");
            var ptTypeId = GetIntProp(rec, "PtTypeId", "ptTypeId");
            var patientTypeName = GetStringProp(rec, "PtType", "ptType", "PatientType", "patientType");
            var groupCodes = GetStringProp(rec, "GroupCodes", "groupCodes", "GrpCodes", "grpCodes");

            if (!string.IsNullOrWhiteSpace(officeName))
                patientOfficeId = await ResolveOfficeIdAsync(conn, tenantId, officeName, ct) ?? patientOfficeId;
            if (!string.IsNullOrWhiteSpace(patientTypeName))
                ptTypeId = await ResolvePatientTypeIdAsync(conn, tenantId, patientTypeName, ct) ?? ptTypeId;

            var existingClaimId = await ResolveExistingClaimIdAsync(
                conn, tenantId, orgId, sourceClaimId.Value, apptId, ct);
            var claimId = existingClaimId ?? sourceClaimId.Value;

            var claimStatusId = GetIntProp(rec, "Claim_StatusId", "claim_StatusId", "StatusId", "statusId");
            var submittedDate = ParseDateTime(GetStringProp(rec, "Submitted_Date", "submitted_Date", "SubmittedDate", "submittedDate"));
            var billedAmount = GetDecimalProp(rec, "Tot_Billed", "tot_Billed", "BilledAmount", "billedAmount");
            var allowedAmount = GetDecimalProp(rec, "Tot_Allowed", "tot_Allowed");
            var paidAmount = GetDecimalProp(rec, "Tot_Paid", "tot_Paid", "Tot_PaidAmt", "tot_PaidAmt");
            var ptResAmt = GetDecimalProp(rec, "PtRes_Amt", "ptRes_Amt", "PatientResponsibility", "patientResponsibility");
            var remarks = GetStringProp(rec, "Remarks", "remarks");
            var attachment = GetStringProp(rec, "Attachment", "attachment");
            var eobCheckNo = GetStringProp(rec, "EobCheckNo", "eobCheckNo");
            var eobStatusId = GetIntProp(rec, "EobStatusId", "eobStatusId");

            // 1. Upsert Person
            if (personId.HasValue && personId.Value > 0)
            {
                await UpsertPersonAsync(conn, tenantId, orgId, personId.Value, firstName, lastName, fullName, phone, email, dob, gender, ct);
            }

            // 2. Resolve/upsert Patient. Legacy claims can contain PersonId,
            // PatientNo and name while Claim.PatientId itself is null.
            patientId = await ResolveOrCreatePatientAsync(
                conn,
                tenantId,
                orgId,
                patientId,
                patientNo,
                personId,
                fullName,
                patientOfficeId,
                ct);

            // 3. Upsert Appointment
            if (apptId.HasValue && apptId.Value > 0)
            {
                await UpsertAppointmentAsync(conn, tenantId, orgId, apptId.Value, patientId, dos, appStatusId, patientOfficeId, ptTypeId, providerName, insuranceName, ct);
            }

            // Policy number lookup from procedure codes
            string? policyNumber = null;
            var hasCodes = rec.TryGetProperty("procedureCodes", out var pcProp) || rec.TryGetProperty("ProcedureCodes", out pcProp);
            if (hasCodes && pcProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in pcProp.EnumerateArray())
                {
                    policyNumber = GetStringProp(c, "PolicyNumber", "policyNumber", "PolicyNo", "policyNo");
                    if (!string.IsNullOrWhiteSpace(policyNumber)) break;
                }
            }

            // 4. Upsert Claim
            await UpsertClaimAsync(conn, tenantId, orgId, claimId, apptId, patientId, eobCheckNo, attachment, ptResAmt, allowedAmount, billedAmount, paidAmount, claimStatusId, eobStatusId, submittedDate, remarks, carrierId, policyNumber, ct);

            // 5. Upsert Procedure Codes
            if (hasCodes && pcProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var code in pcProp.EnumerateArray())
                {
                    var apptCodeId = GetIntProp(code, "ApptCodeId", "apptCodeId", "Id", "id");
                    var codeClaimId = claimId;
                    var codeApptId = GetIntProp(code, "ApptId", "apptId") ?? apptId;
                    var codeId = GetIntProp(code, "CodeId", "codeId");
                    var codeStr = GetStringProp(code, "Code", "code", "Cpt_Codes", "cpt_Codes");
                    var claimAmt = GetDecimalProp(code, "ClaimAmount", "claimAmount", "Claim_Amt", "claim_Amt");
                    var totClaimAmt = GetDecimalProp(code, "TotalClaimAmount", "totalClaimAmount", "Tot_ClaimAmt", "tot_ClaimAmt");
                    var insRate = GetDecimalProp(code, "InsuranceRate", "insuranceRate", "Ins_Rate", "ins_Rate");
                    var insAmt = GetDecimalProp(code, "InsuranceAmount", "insuranceAmount", "Ins_Amt", "ins_Amt");
                    var paidAmt = GetDecimalProp(code, "PaidAmount", "paidAmount", "Paid_Amt", "paid_Amt");
                    var ptResp = GetDecimalProp(code, "PatientResponsibility", "patientResponsibility", "Pt_R", "pt_R");
                    var modifiers = GetStringProp(code, "Modifiers", "modifiers");
                    var polNo = GetStringProp(code, "PolicyNumber", "policyNumber", "PolicyNo", "policyNo") ?? policyNumber;
                    var dosStr = dos?.ToString("yyyy-MM-dd");

                    codeId = await ResolveOrCreateProcedureCodeIdAsync(
                        conn, tenantId, codeId, codeStr, claimAmt, ct);

                    await UpsertApptCodeAsync(conn, tenantId, orgId, apptCodeId, codeApptId, codeClaimId, patientId, codeId, codeStr, dosStr, polNo, claimAmt, totClaimAmt, insRate, insAmt, paidAmt, ptResp, modifiers, ct);
                }
            }

            await UpsertGroupCodesAsync(conn, tenantId, orgId, claimId, groupCodes, ct);

            if (!existingClaimId.HasValue)
            {
                inserted++;
            }
            else if (!string.IsNullOrWhiteSpace(fullName) || !string.IsNullOrWhiteSpace(patientNo) || patientId.HasValue)
            {
                // Existing claims are never inserted again. Re-import only
                // repairs missing patient/appointment/master references.
                repaired++;
            }
            else
            {
                skipped++;
            }
        }

        return new ClaimsImportResult(inserted, repaired, skipped);
    }

    private static async Task<int?> ResolveExistingClaimIdAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int sourceClaimId,
        int? appointmentId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) ClaimId
FROM dbo.tblInsPTClaims WITH (UPDLOCK, HOLDLOCK)
WHERE TenantId=@TenantId AND OrgId=@OrgId
  AND (ClaimId=@SourceClaimId OR (@AppointmentId IS NOT NULL AND AppointmentId=@AppointmentId))
ORDER BY CASE WHEN ClaimId=@SourceClaimId THEN 0 ELSE 1 END,ClaimId;";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@SourceClaimId", SqlDbType.Int) { Value = sourceClaimId });
        cmd.Parameters.Add(new SqlParameter("@AppointmentId", SqlDbType.Int) { Value = (object?)appointmentId ?? DBNull.Value });
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task UpsertPersonAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int personId,
        string? firstName,
        string? lastName,
        string? fullName,
        string? phone,
        string? email,
        DateTime? dob,
        bool? gender,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.tblPerson WHERE PersonId = @PersonId)
BEGIN
    UPDATE dbo.tblPerson
    SET FirstName = COALESCE(@FirstName, FirstName),
        LastName = COALESCE(@LastName, LastName),
        FullName = COALESCE(@FullName, FullName),
        PhoneNumber = COALESCE(@PhoneNumber, PhoneNumber),
        Email = COALESCE(@Email, Email),
        DOB = COALESCE(@DOB, DOB),
        Gender = COALESCE(@Gender, Gender),
        TenantId = @TenantId,
        OrgId = @OrgId
    WHERE PersonId = @PersonId;
END
ELSE
BEGIN
    SET IDENTITY_INSERT dbo.tblPerson ON;
    INSERT INTO dbo.tblPerson (PersonId, TenantId, OrgId, FirstName, LastName, FullName, PhoneNumber, Email, DOB, Gender, CreatedDate)
    VALUES (@PersonId, @TenantId, @OrgId, @FirstName, @LastName, @FullName, @PhoneNumber, @Email, @DOB, @Gender, GETUTCDATE());
    SET IDENTITY_INSERT dbo.tblPerson OFF;
END";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@PersonId", SqlDbType.Int) { Value = personId });
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 200) { Value = (object?)firstName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 200) { Value = (object?)lastName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@FullName", SqlDbType.NVarChar, 400) { Value = (object?)fullName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.NVarChar, 100) { Value = (object?)phone ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object?)email ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@DOB", SqlDbType.DateTime) { Value = (object?)dob ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Gender", SqlDbType.Bit) { Value = (object?)gender ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertPatientAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int patientId,
        string? patientNo,
        int? personId,
        string? patientName,
        int? officeId,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.tblPatient WHERE PatientId = @PatientId)
BEGIN
    UPDATE dbo.tblPatient
    SET PatientNo = COALESCE(@PatientNo, PatientNo),
        PersonId = COALESCE(@PersonId, PersonId),
        PatientName = COALESCE(@PatientName, PatientName),
        OfficeId = COALESCE(@OfficeId, OfficeId),
        OrgId = @OrgId,
        TenantId = @TenantId
    WHERE PatientId = @PatientId;
END
ELSE
BEGIN
    SET IDENTITY_INSERT dbo.tblPatient ON;
    INSERT INTO dbo.tblPatient (PatientId, TenantId, OrgId, OfficeId, PatientNo, PersonId, PatientName, CreatedDate)
    VALUES (@PatientId, @TenantId, @OrgId, @OfficeId, @PatientNo, @PersonId, @PatientName, GETUTCDATE());
    SET IDENTITY_INSERT dbo.tblPatient OFF;
END";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@PatientId", SqlDbType.Int) { Value = patientId });
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@PatientNo", SqlDbType.NVarChar, 200) { Value = (object?)patientNo ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PersonId", SqlDbType.Int) { Value = (object?)personId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PatientName", SqlDbType.NVarChar, 200) { Value = (object?)patientName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@OfficeId", SqlDbType.Int) { Value = (object?)officeId ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int?> ResolveOrCreatePatientAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int? sourcePatientId,
        string? patientNo,
        int? personId,
        string? patientName,
        int? officeId,
        CancellationToken ct)
    {
        if (sourcePatientId is > 0)
        {
            await UpsertPatientAsync(
                conn,
                tenantId,
                orgId,
                sourcePatientId.Value,
                patientNo,
                personId,
                patientName,
                officeId,
                ct);
            return sourcePatientId;
        }

        if (personId is not > 0 && string.IsNullOrWhiteSpace(patientNo))
        {
            return null;
        }

        const string sql = @"
DECLARE @ResolvedPatientId int;

SELECT TOP (1) @ResolvedPatientId = PatientId
FROM dbo.tblPatient
WHERE TenantId = @TenantId
  AND OrgId = @OrgId
  AND ((@PersonId IS NOT NULL AND PersonId = @PersonId)
       OR (@PatientNo IS NOT NULL AND PatientNo = @PatientNo))
ORDER BY CASE WHEN @PersonId IS NOT NULL AND PersonId = @PersonId THEN 0 ELSE 1 END,
         PatientId;

IF @ResolvedPatientId IS NULL
BEGIN
    INSERT INTO dbo.tblPatient
        (TenantId, OrgId, OfficeId, PatientNo, PersonId, PatientName, CreatedDate)
    VALUES
        (@TenantId, @OrgId, @OfficeId, @PatientNo, @PersonId, @PatientName, GETUTCDATE());

    SET @ResolvedPatientId = CONVERT(int, SCOPE_IDENTITY());
END
ELSE
BEGIN
    UPDATE dbo.tblPatient
    SET PatientNo = COALESCE(@PatientNo, PatientNo),
        PersonId = COALESCE(@PersonId, PersonId),
        PatientName = COALESCE(@PatientName, PatientName),
        OfficeId = COALESCE(@OfficeId, OfficeId)
    WHERE PatientId = @ResolvedPatientId;
END

SELECT @ResolvedPatientId;";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@PatientNo", SqlDbType.NVarChar, 200)
        {
            Value = string.IsNullOrWhiteSpace(patientNo) ? DBNull.Value : patientNo
        });
        cmd.Parameters.Add(new SqlParameter("@PersonId", SqlDbType.Int) { Value = (object?)personId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PatientName", SqlDbType.NVarChar, 200) { Value = (object?)patientName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@OfficeId", SqlDbType.Int) { Value = (object?)officeId ?? DBNull.Value });

        var resolved = await cmd.ExecuteScalarAsync(ct);
        return resolved is null or DBNull ? null : Convert.ToInt32(resolved, CultureInfo.InvariantCulture);
    }

    private static async Task<int?> ResolveCarrierIdAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        string carrierName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) CarrierId
FROM dbo.tblCarrier
WHERE UPPER(LTRIM(RTRIM(CarrierName))) = UPPER(LTRIM(RTRIM(@CarrierName)))
  AND (TenantId = @TenantId OR TenantId IS NULL)
  AND (OrgId = @OrgId OR OrgId IS NULL)
ORDER BY CarrierId;";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@CarrierName", SqlDbType.NVarChar, 300) { Value = carrierName });
        var resolved = await cmd.ExecuteScalarAsync(ct);
        return resolved is null or DBNull ? null : Convert.ToInt32(resolved, CultureInfo.InvariantCulture);
    }

    private static async Task<int?> ResolveOfficeIdAsync(
        DbConnection conn, int tenantId, string officeName, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) OfficeId FROM dbo.tblOffices
WHERE (TenantId=@TenantId OR TenantId IS NULL)
  AND UPPER(LTRIM(RTRIM(OfficeName)))=UPPER(LTRIM(RTRIM(@Name)))
ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END,OfficeId;";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = officeName });
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<int?> ResolvePatientTypeIdAsync(
        DbConnection conn, int tenantId, string patientTypeName, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) PtTypeId FROM dbo.tblPatientType
WHERE (TenantId=@TenantId OR TenantId IS NULL)
  AND UPPER(LTRIM(RTRIM(PtType)))=UPPER(LTRIM(RTRIM(@Name)))
ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END,PtTypeId;";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = patientTypeName });
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<int?> ResolveOrCreateProcedureCodeIdAsync(
        DbConnection conn,
        int tenantId,
        int? sourceProcedureCodeId,
        string? procedureCode,
        decimal? standardRate,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @ResolvedId int;

IF NULLIF(LTRIM(RTRIM(@ProcedureCode)), N'') IS NOT NULL
BEGIN
    SELECT TOP (1) @ResolvedId=ProcCodeId
    FROM dbo.tblProcedureCodes
    WHERE (TenantId=@TenantId OR TenantId IS NULL)
      AND UPPER(LTRIM(RTRIM(Codes)))=UPPER(LTRIM(RTRIM(@ProcedureCode)))
    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END,ProcCodeId;

    IF @ResolvedId IS NULL
    BEGIN
        INSERT INTO dbo.tblProcedureCodes(TenantId,Codes,StandardRate)
        VALUES(@TenantId,LTRIM(RTRIM(@ProcedureCode)),@StandardRate);
        SET @ResolvedId=CONVERT(int,SCOPE_IDENTITY());
    END
END
ELSE IF @SourceProcedureCodeId IS NOT NULL
     AND EXISTS(SELECT 1 FROM dbo.tblProcedureCodes WHERE ProcCodeId=@SourceProcedureCodeId AND (TenantId=@TenantId OR TenantId IS NULL))
BEGIN
    SET @ResolvedId=@SourceProcedureCodeId;
END

SELECT @ResolvedId;";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@SourceProcedureCodeId", SqlDbType.Int) { Value = (object?)sourceProcedureCodeId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ProcedureCode", SqlDbType.NVarChar, 50) { Value = (object?)procedureCode ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@StandardRate", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)standardRate ?? DBNull.Value });
        var resolved = await cmd.ExecuteScalarAsync(ct);
        return resolved is null or DBNull ? null : Convert.ToInt32(resolved, CultureInfo.InvariantCulture);
    }

    private static async Task UpsertGroupCodesAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int claimId,
        string? groupCodes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(groupCodes))
        {
            return;
        }

        const string sql = @"
;WITH Parsed AS
(
    SELECT DISTINCT LEFT(LTRIM(RTRIM(value)), 50) AS GroupCode
    FROM STRING_SPLIT(@GroupCodes, ',')
    WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL
)
INSERT INTO dbo.tblClaimGrpCodeDetails
    (TenantId, OrgId, AppClaimId, Grp_Code, CreatedDate)
SELECT @TenantId, @OrgId, @ClaimId, parsed.GroupCode, GETUTCDATE()
FROM Parsed parsed
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.tblClaimGrpCodeDetails existing
    WHERE existing.TenantId = @TenantId
      AND existing.AppClaimId = @ClaimId
      AND UPPER(LTRIM(RTRIM(existing.Grp_Code))) = UPPER(parsed.GroupCode)
);";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@ClaimId", SqlDbType.Int) { Value = claimId });
        cmd.Parameters.Add(new SqlParameter("@GroupCodes", SqlDbType.NVarChar, -1) { Value = groupCodes });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertAppointmentAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int appointmentId,
        int? patientId,
        DateTime? dos,
        int? appStatusId,
        int? officeId,
        int? ptTypeId,
        string? providerName,
        string? insuranceName,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.tblAppointment WHERE AppointmentId = @AppointmentId)
BEGIN
    UPDATE dbo.tblAppointment
    SET PatientId = COALESCE(@PatientId, PatientId),
        DOS = COALESCE(@DOS, DOS),
        AppStatusId = COALESCE(@AppStatusId, AppStatusId),
        OfficeId = COALESCE(@OfficeId, OfficeId),
        PtTypeId = COALESCE(@PtTypeId, PtTypeId),
        ProviderName = COALESCE(@ProviderName, ProviderName),
        InsuranceName = COALESCE(@InsuranceName, InsuranceName),
        IsClaimed = 1,
        OrgId = @OrgId,
        TenantId = @TenantId
    WHERE AppointmentId = @AppointmentId;
END
ELSE
BEGIN
    SET IDENTITY_INSERT dbo.tblAppointment ON;
    INSERT INTO dbo.tblAppointment (AppointmentId, TenantId, PatientId, OrgId, DOS, AppStatusId, OfficeId, PtTypeId, ProviderName, InsuranceName, IsClaimed, CreatedDate)
    VALUES (@AppointmentId, @TenantId, @PatientId, @OrgId, @DOS, @AppStatusId, @OfficeId, @PtTypeId, @ProviderName, @InsuranceName, 1, GETUTCDATE());
    SET IDENTITY_INSERT dbo.tblAppointment OFF;
END";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@AppointmentId", SqlDbType.Int) { Value = appointmentId });
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@PatientId", SqlDbType.Int) { Value = (object?)patientId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@DOS", SqlDbType.DateTime) { Value = (object?)dos ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@AppStatusId", SqlDbType.Int) { Value = (object?)appStatusId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@OfficeId", SqlDbType.Int) { Value = (object?)officeId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PtTypeId", SqlDbType.Int) { Value = (object?)ptTypeId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ProviderName", SqlDbType.NVarChar, 200) { Value = (object?)providerName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@InsuranceName", SqlDbType.NVarChar, 200) { Value = (object?)insuranceName ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertClaimAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int claimId,
        int? appointmentId,
        int? patientId,
        string? eobCheckNo,
        string? attachment,
        decimal? patientResponsibility,
        decimal? totalAllowed,
        decimal? totalBilled,
        decimal? totalPaid,
        int? statusId,
        int? eobStatusId,
        DateTime? submittedDate,
        string? remarks,
        int? carrierId,
        string? policyNumber,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.tblInsPTClaims WHERE ClaimId = @ClaimId AND TenantId=@TenantId AND OrgId=@OrgId)
BEGIN
    UPDATE dbo.tblInsPTClaims
    SET AppointmentId = COALESCE(@AppointmentId, AppointmentId),
        PatientId = COALESCE(@PatientId, PatientId),
        OrgId = @OrgId,
        TenantId = @TenantId,
        EobCheckNo = COALESCE(@EobCheckNo, EobCheckNo),
        Attachment = COALESCE(@Attachment, Attachment),
        PatientResponsibility = COALESCE(@PatientResponsibility, PatientResponsibility),
        TotalAllowed = COALESCE(@TotalAllowed, TotalAllowed),
        TotalBilled = COALESCE(@TotalBilled, TotalBilled),
        TotalPaid = COALESCE(@TotalPaid, TotalPaid),
        StatusId = COALESCE(@StatusId, StatusId),
        EobStatusId = COALESCE(@EobStatusId, EobStatusId),
        SubmittedDate = COALESCE(@SubmittedDate, SubmittedDate),
        Remarks = COALESCE(@Remarks, Remarks),
        CarrierId = COALESCE(@CarrierId, CarrierId),
        PolicyNumber = COALESCE(@PolicyNumber, PolicyNumber)
    WHERE ClaimId = @ClaimId AND TenantId=@TenantId AND OrgId=@OrgId;
END
ELSE
BEGIN
    SET IDENTITY_INSERT dbo.tblInsPTClaims ON;
    INSERT INTO dbo.tblInsPTClaims (ClaimId, TenantId, AppointmentId, PatientId, OrgId, EobCheckNo, Attachment, PatientResponsibility, TotalAllowed, TotalBilled, TotalPaid, StatusId, EobStatusId, SubmittedDate, Remarks, CarrierId, PolicyNumber, CreatedDate)
    VALUES (@ClaimId, @TenantId, @AppointmentId, @PatientId, @OrgId, @EobCheckNo, @Attachment, @PatientResponsibility, @TotalAllowed, @TotalBilled, @TotalPaid, @StatusId, @EobStatusId, @SubmittedDate, @Remarks, @CarrierId, @PolicyNumber, GETUTCDATE());
    SET IDENTITY_INSERT dbo.tblInsPTClaims OFF;
END";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@ClaimId", SqlDbType.Int) { Value = claimId });
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@AppointmentId", SqlDbType.Int) { Value = (object?)appointmentId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PatientId", SqlDbType.Int) { Value = (object?)patientId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@EobCheckNo", SqlDbType.NVarChar, 200) { Value = (object?)eobCheckNo ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Attachment", SqlDbType.NVarChar, 500) { Value = (object?)attachment ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PatientResponsibility", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)patientResponsibility ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@TotalAllowed", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)totalAllowed ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@TotalBilled", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)totalBilled ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@TotalPaid", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)totalPaid ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@StatusId", SqlDbType.Int) { Value = (object?)statusId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@EobStatusId", SqlDbType.Int) { Value = (object?)eobStatusId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@SubmittedDate", SqlDbType.DateTime) { Value = (object?)submittedDate ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Remarks", SqlDbType.NVarChar, 1000) { Value = (object?)remarks ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@CarrierId", SqlDbType.Int) { Value = (object?)carrierId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PolicyNumber", SqlDbType.NVarChar, 200) { Value = (object?)policyNumber ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertApptCodeAsync(
        DbConnection conn,
        int tenantId,
        int orgId,
        int? apptCodeId,
        int? appointmentId,
        int claimId,
        int? patientId,
        int? procedureCodeId,
        string? procedureCode,
        string? dateOfService,
        string? policyNo,
        decimal? claimAmount,
        decimal? totalClaimAmount,
        decimal? insuranceRate,
        decimal? insuranceAmount,
        decimal? paid,
        decimal? patientResponsibility,
        string? modifiers,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @TargetApptCodeId int;

SELECT TOP (1) @TargetApptCodeId=ApptCodeId
FROM dbo.tblApptCodes WITH (UPDLOCK,HOLDLOCK)
WHERE TenantId=@TenantId AND OrgId=@OrgId
  AND ((@ApptCodeId IS NOT NULL AND @ApptCodeId>0 AND ApptCodeId=@ApptCodeId)
       OR (ClaimId=@ClaimId
           AND ISNULL(AppointmentId,0)=ISNULL(@AppointmentId,0)
           AND UPPER(LTRIM(RTRIM(ISNULL(ProcedureCode,N''))))=UPPER(LTRIM(RTRIM(ISNULL(@ProcedureCode,N''))))
           AND UPPER(LTRIM(RTRIM(ISNULL(Modifiers,N''))))=UPPER(LTRIM(RTRIM(ISNULL(@Modifiers,N''))))
           AND UPPER(LTRIM(RTRIM(ISNULL(PolicyNumber,N''))))=UPPER(LTRIM(RTRIM(ISNULL(@PolicyNo,N''))))))
ORDER BY CASE WHEN ApptCodeId=@ApptCodeId THEN 0 ELSE 1 END,ApptCodeId;

IF @TargetApptCodeId IS NOT NULL
BEGIN
    UPDATE dbo.tblApptCodes
    SET AppointmentId = COALESCE(@AppointmentId, AppointmentId),
        ClaimId = COALESCE(@ClaimId, ClaimId),
        PatientId = COALESCE(@PatientId, PatientId),
        OrgId = @OrgId,
        TenantId = @TenantId,
        ProcedureCodeId = COALESCE(@ProcedureCodeId, ProcedureCodeId),
        ProcedureCode = COALESCE(@ProcedureCode, ProcedureCode),
        DateOfService = COALESCE(@DateOfService, DateOfService),
        PolicyNo = COALESCE(@PolicyNo, PolicyNo),
        ClaimAmount = COALESCE(@ClaimAmount, ClaimAmount),
        TotalClaimAmount = COALESCE(@TotalClaimAmount, TotalClaimAmount),
        InsuranceRate = COALESCE(@InsuranceRate, InsuranceRate),
        InsuranceAmount = COALESCE(@InsuranceAmount, InsuranceAmount),
        Paid = COALESCE(@Paid, Paid),
        PatientResponsibility = COALESCE(@PatientResponsibility, PatientResponsibility),
        Modifiers = COALESCE(@Modifiers, Modifiers),
        PolicyNumber = COALESCE(@PolicyNo, PolicyNumber)
    WHERE ApptCodeId = @TargetApptCodeId AND TenantId=@TenantId AND OrgId=@OrgId;
END
ELSE IF @ApptCodeId IS NOT NULL AND @ApptCodeId > 0
        AND NOT EXISTS(SELECT 1 FROM dbo.tblApptCodes WHERE ApptCodeId=@ApptCodeId)
BEGIN
    SET IDENTITY_INSERT dbo.tblApptCodes ON;
    INSERT INTO dbo.tblApptCodes (ApptCodeId, TenantId, AppointmentId, ClaimId, PatientId, OrgId, ProcedureCodeId, ProcedureCode, DateOfService, PolicyNo, ClaimAmount, TotalClaimAmount, InsuranceRate, InsuranceAmount, Paid, PatientResponsibility, Modifiers, PolicyNumber, CreatedDate)
    VALUES (@ApptCodeId, @TenantId, @AppointmentId, @ClaimId, @PatientId, @OrgId, @ProcedureCodeId, @ProcedureCode, @DateOfService, @PolicyNo, @ClaimAmount, @TotalClaimAmount, @InsuranceRate, @InsuranceAmount, @Paid, @PatientResponsibility, @Modifiers, @PolicyNo, GETUTCDATE());
    SET IDENTITY_INSERT dbo.tblApptCodes OFF;
END
ELSE
BEGIN
    INSERT INTO dbo.tblApptCodes (TenantId, AppointmentId, ClaimId, PatientId, OrgId, ProcedureCodeId, ProcedureCode, DateOfService, PolicyNo, ClaimAmount, TotalClaimAmount, InsuranceRate, InsuranceAmount, Paid, PatientResponsibility, Modifiers, PolicyNumber, CreatedDate)
    VALUES (@TenantId, @AppointmentId, @ClaimId, @PatientId, @OrgId, @ProcedureCodeId, @ProcedureCode, @DateOfService, @PolicyNo, @ClaimAmount, @TotalClaimAmount, @InsuranceRate, @InsuranceAmount, @Paid, @PatientResponsibility, @Modifiers, @PolicyNo, GETUTCDATE());
END";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@ApptCodeId", SqlDbType.Int) { Value = (object?)apptCodeId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@OrgId", SqlDbType.Int) { Value = orgId });
        cmd.Parameters.Add(new SqlParameter("@AppointmentId", SqlDbType.Int) { Value = (object?)appointmentId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ClaimId", SqlDbType.Int) { Value = claimId });
        cmd.Parameters.Add(new SqlParameter("@PatientId", SqlDbType.Int) { Value = (object?)patientId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ProcedureCodeId", SqlDbType.Int) { Value = (object?)procedureCodeId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ProcedureCode", SqlDbType.NVarChar, 50) { Value = (object?)procedureCode ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@DateOfService", SqlDbType.NVarChar, 50) { Value = (object?)dateOfService ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PolicyNo", SqlDbType.NVarChar, 200) { Value = (object?)policyNo ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ClaimAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)claimAmount ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@TotalClaimAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)totalClaimAmount ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@InsuranceRate", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)insuranceRate ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@InsuranceAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)insuranceAmount ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Paid", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)paid ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PatientResponsibility", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)patientResponsibility ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Modifiers", SqlDbType.NVarChar, 100) { Value = (object?)modifiers ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<int> SaveRoznamchaBatchAsync(JsonElement recordsElement, int tenantId, CancellationToken ct)
    {
        var billingCategoryId = await ResolveBillingCategoryIdAsync(tenantId, ct);
        var receiptRozTypeId = await ResolveReceiptRozTypeIdAsync(ct);
        var savedCount = 0;

        foreach (var rec in recordsElement.EnumerateArray())
        {
            var refCode = GetStringProp(rec, "Ref", "ref");
            var oldRef = GetStringProp(rec, "Old_Ref", "old_Ref", "oldRef") ?? refCode;
            var sNoStr = GetStringProp(rec, "SNo", "sNo");
            int? sNo = int.TryParse(sNoStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSNo) ? parsedSNo : null;

            var transDateStr = GetStringProp(rec, "TransDate", "transDate");
            var transDate = ParseDateOnly(transDateStr) ?? DateOnly.FromDateTime(DateTime.UtcNow);

            var insDateStr = GetStringProp(rec, "InsDate", "insDate");
            var instrumentDate = ParseDateOnly(insDateStr);

            var amount = GetDecimalProp(rec, "Cr_DbAmt", "cr_DbAmt", "Amount", "amount");
            var balanceAmount = GetDecimalProp(rec, "Bal_Amt", "bal_Amt", "BalanceAmount", "balanceAmount");
            var adjustment = GetDecimalProp(rec, "Adjustment", "adjustment");
            var usdAmount = GetDecimalProp(rec, "UsdAmt", "usdAmt", "UsdAmount", "usdAmount");
            var taxRate = GetDecimalProp(rec, "TaxRate", "taxRate");
            var taxAmt = GetDecimalProp(rec, "TaxAmt", "taxAmt");
            var qty = GetDecimalProp(rec, "Qty", "qty");
            var rate = GetDecimalProp(rec, "rate", "Rate");

            var fromAccId = GetIntProp(rec, "from_AccId", "FromAccountId");
            var toAcctId = GetIntProp(rec, "To_AcctId", "ToAccountId");
            var customerId = GetIntProp(rec, "CustomerId", "customerId");
            var transTypeId = GetIntProp(rec, "TransTypeId", "transTypeId");
            var transModeId = GetIntProp(rec, "TransModeId", "transModeId");
            var currencyId = GetIntProp(rec, "CurrencyId", "currencyId");
            var taxTypeId = GetIntProp(rec, "TaxTypeId", "taxTypeId");
            var enterStatusId = GetIntProp(rec, "EnterStatusId", "enterStatusId");

            var instrumentNo = GetStringProp(rec, "InstrumentNo", "instrumentNo");
            var descriptions = GetStringProp(rec, "RDiscriptions", "rDiscriptions", "Descriptions", "descriptions");
            var bankLtRef = GetStringProp(rec, "BankLT_Ref", "bankLT_Ref", "BankLtRef", "bankLtRef");
            var bankRef = GetStringProp(rec, "Bank_Ref", "bank_Ref", "BankRef", "bankRef");
            var attachment = GetStringProp(rec, "Attachment", "attachment");
            var remarks = GetStringProp(rec, "Remarks", "remarks");
            var isDeleted = GetBoolProp(rec, "isDeleted", "IsDeleted") ?? false;

            RoznamchaEntry? existing = null;
            if (!string.IsNullOrWhiteSpace(refCode))
            {
                existing = await _db.RoznamchaEntries
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Ref == refCode, ct);
            }

            if (existing == null && !string.IsNullOrWhiteSpace(bankLtRef))
            {
                existing = await _db.RoznamchaEntries
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.BankLtRef == bankLtRef, ct);
            }

            if (existing != null)
            {
                existing.SNo = sNo ?? existing.SNo;
                existing.OldRef = oldRef ?? existing.OldRef;
                existing.TransDate = transDate;
                existing.InstrumentDate = instrumentDate ?? existing.InstrumentDate;
                existing.InstrumentNo = instrumentNo ?? existing.InstrumentNo;
                existing.Descriptions = descriptions ?? existing.Descriptions;
                existing.Amount = amount ?? existing.Amount;
                existing.BalanceAmount = balanceAmount ?? existing.BalanceAmount;
                existing.Adjustment = adjustment ?? existing.Adjustment;
                existing.UsdAmount = usdAmount ?? existing.UsdAmount;
                existing.TaxRate = taxRate ?? existing.TaxRate;
                existing.TaxAmt = taxAmt ?? existing.TaxAmt;
                existing.Qty = qty ?? existing.Qty;
                existing.Rate = rate ?? existing.Rate;
                existing.FromAccountId = fromAccId ?? existing.FromAccountId;
                existing.ToAccountId = toAcctId ?? existing.ToAccountId;
                existing.CustomerId = customerId ?? existing.CustomerId;
                existing.TransTypeId = transTypeId ?? existing.TransTypeId;
                existing.TransModeId = transModeId ?? existing.TransModeId;
                existing.CurrencyId = currencyId ?? existing.CurrencyId;
                existing.TaxTypeId = taxTypeId ?? existing.TaxTypeId;
                existing.EnterStatusId = enterStatusId ?? existing.EnterStatusId;
                existing.Attachment = attachment ?? existing.Attachment;
                existing.Remarks = remarks ?? existing.Remarks;
                existing.BankLtRef = bankLtRef ?? existing.BankLtRef;
                existing.BankRef = bankRef ?? existing.BankRef;
                existing.IsDeleted = isDeleted;
                existing.UpdatedOnUtc = DateTime.UtcNow;
            }
            else
            {
                var entry = new RoznamchaEntry
                {
                    TenantId = tenantId,
                    SNo = sNo,
                    Ref = refCode,
                    OldRef = oldRef,
                    RoznamchaTypeId = receiptRozTypeId,
                    CategoryId = billingCategoryId,
                    FromAccountId = fromAccId,
                    ToAccountId = toAcctId,
                    CustomerId = customerId,
                    TransTypeId = transTypeId,
                    TransModeId = transModeId,
                    InstrumentDate = instrumentDate,
                    InstrumentNo = instrumentNo,
                    Descriptions = descriptions,
                    CurrencyId = currencyId,
                    TaxTypeId = taxTypeId,
                    TaxRate = taxRate,
                    TaxAmt = taxAmt,
                    Adjustment = adjustment,
                    Amount = amount,
                    BalanceAmount = balanceAmount,
                    Qty = qty,
                    Rate = rate,
                    EnterStatusId = enterStatusId,
                    Attachment = attachment,
                    Remarks = remarks,
                    BankLtRef = bankLtRef,
                    TransDate = transDate,
                    IsDeleted = isDeleted,
                    BankRef = bankRef,
                    UsdAmount = usdAmount,
                    IsShow = true,
                    IsSettled = false,
                    CreatedOnUtc = DateTime.UtcNow
                };
                _db.RoznamchaEntries.Add(entry);
            }

            savedCount++;
        }

        if (savedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return savedCount;
    }

    private async Task<int?> ResolveBillingCategoryIdAsync(int tenantId, CancellationToken ct)
    {
        var categoryId = await _db.AccountsCategories.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive &&
                (x.Name.ToLower() == "billing" || (x.Code != null && x.Code.ToLower() == "billing")))
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);

        if (!categoryId.HasValue)
        {
            categoryId = await _db.AccountsCategories.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderBy(x => x.Id)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
        }

        return categoryId;
    }

    private async Task<int?> ResolveReceiptRozTypeIdAsync(CancellationToken ct)
    {
        var typeId = await _db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => x.Code.ToUpper() == "RECEIPT")
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);

        return typeId ?? 2;
    }

    private static string? GetStringProp(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    // NEW: Import all batches for a date range (re‑fetch everything)
    [HttpPost]
    public async Task<IActionResult> ImportAll([FromQuery] string fromDate, [FromQuery] string toDate, CancellationToken ct = default)
    {
        try
        {
            long afterId = 0;
            long maxBillingId = 0; // let old API decide the max
            const int pageSize = 1000;
            int totalReturned = 0, totalSaved = 0, totalRepaired = 0, totalSkipped = 0;
            bool hasMore = true;

            while (hasMore && !ct.IsCancellationRequested)
            {
                var result = await FetchBillingBatch(fromDate, toDate, afterId, maxBillingId, pageSize, ct);
                if (result is ObjectResult obj && obj.Value is JsonElement json)
                {
                    if (!json.GetProperty("success").GetBoolean())
                        return BadRequest(json);

                    totalReturned += json.GetProperty("returnedCount").GetInt32();
                    totalSaved    += json.GetProperty("savedCount").GetInt32();
                    totalRepaired += json.GetProperty("repairedCount").GetInt32();
                    totalSkipped  += json.GetProperty("skippedCount").GetInt32();
                    afterId        = json.GetProperty("nextId").GetInt64();
                    hasMore        = json.GetProperty("hasMore").GetBoolean();
                }
                else
                {
                    return StatusCode(500, new { success = false, message = "Unexpected response from fetch" });
                }
            }

            return Ok(new
            {
                success = true,
                totalReturned,
                totalSaved,
                totalRepaired,
                totalSkipped,
                message = "Full import completed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImportAll error");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static int? GetIntProp(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)) return val;
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static decimal? GetDecimalProp(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val)) return val;
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static bool? GetBoolProp(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
                if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static DateOnly? ParseDateOnly(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        if (DateOnly.TryParse(str, CultureInfo.InvariantCulture, out var d1)) return d1;
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, out var dt)) return DateOnly.FromDateTime(dt);
        if (DateTime.TryParseExact(str.Trim(), new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy", "yyyy/MM/dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return DateOnly.FromDateTime(dt2);
        return null;
    }

    private static DateTime? ParseDateTime(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return dt2;
        if (DateTime.TryParse(str, out var dt3))
            return dt3;
        if (DateTime.TryParseExact(str.Trim(), new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt4))
            return dt4;
        return null;
    }
}
