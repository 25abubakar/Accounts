using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IAttendanceStatusService
{
    Task<IReadOnlyList<AttendanceStatusDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AttendanceStatusDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AttendanceStatusDto> CreateAsync(CreateAttendanceStatusDto dto, CancellationToken cancellationToken = default);
    Task<AttendanceStatusDto?> UpdateAsync(int id, UpdateAttendanceStatusDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class DuplicateAttendanceStatusException(string message) : InvalidOperationException(message);
