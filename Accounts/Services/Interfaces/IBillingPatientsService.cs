using Accounts.DTOs;
using Accounts.Models;
using Microsoft.AspNetCore.Http;

namespace Accounts.Services.Interfaces;

public interface IBillingPatientsService
{
    Task<IReadOnlyList<BillingPatientRowDto>> ListAppointmentsAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        int? officeId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingPatientRowDto>> SearchPatientsAsync(
        int tenantId,
        string search,
        CancellationToken ct = default);

    Task<int> CreatePatientAndAppointmentAsync(
        int tenantId,
        CreatePatientAppointmentRequest request,
        string? userId,
        CancellationToken ct = default);

    Task<int> UploadPatientSheetAsync(
        int tenantId,
        IFormFile file,
        string? userId,
        CancellationToken ct = default);

    Task<int> SyncEmrAsync(
        int tenantId,
        DateOnly date,
        string? userId,
        CancellationToken ct = default);

    Task<BillingPatientLookupsDto> GetPatientLookupsAsync(
        int tenantId,
        CancellationToken ct = default);
}

public sealed class BillingPatientLookupsDto
{
    public IReadOnlyList<object> Offices { get; set; } = [];
    public IReadOnlyList<object> PatientTypes { get; set; } = [];
    public IReadOnlyList<object> Statuses { get; set; } = [];
}

