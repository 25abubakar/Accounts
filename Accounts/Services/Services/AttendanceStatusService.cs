using Accounts.DTOs;
using Accounts.Models;
using Accounts.Repositories.Interfaces;
using Accounts.Services.Interfaces;
using AutoMapper;

namespace Accounts.Services.Services;

public sealed class AttendanceStatusService : IAttendanceStatusService
{
    private readonly IAttendanceStatusRepository _repository;
    private readonly IMapper _mapper;

    public AttendanceStatusService(IAttendanceStatusRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<AttendanceStatusDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _mapper.Map<IReadOnlyList<AttendanceStatusDto>>(await _repository.GetAllAsync(cancellationToken));

    public async Task<AttendanceStatusDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _mapper.Map<AttendanceStatusDto?>(await _repository.GetByIdAsync(id, cancellationToken));

    public async Task<AttendanceStatusDto> CreateAsync(CreateAttendanceStatusDto dto, CancellationToken cancellationToken = default)
    {
        Normalize(dto);
        await EnsureUniqueAsync(dto.Code, dto.StatusName, null, cancellationToken);
        var entity = _mapper.Map<StatusMaster>(dto);
        entity.StatusType = "Attendance";
        entity.CreatedDate = DateTime.UtcNow;
        await _repository.AddAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return _mapper.Map<AttendanceStatusDto>(entity);
    }

    public async Task<AttendanceStatusDto?> UpdateAsync(int id, UpdateAttendanceStatusDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null) return null;
        Normalize(dto);
        await EnsureUniqueAsync(dto.Code, dto.StatusName, id, cancellationToken);
        _mapper.Map(dto, entity);
        entity.ModifiedDate = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return _mapper.Map<AttendanceStatusDto>(entity);
    }

    public async Task<bool> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null) return false;
        entity.IsActive = false;
        entity.ModifiedDate = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task EnsureUniqueAsync(string code, string name, int? excludingId, CancellationToken cancellationToken)
    {
        if (await _repository.CodeExistsAsync(code, excludingId, cancellationToken))
            throw new DuplicateAttendanceStatusException($"Attendance status code '{code}' already exists.");
        if (await _repository.NameExistsAsync(name, excludingId, cancellationToken))
            throw new DuplicateAttendanceStatusException($"Attendance status name '{name}' already exists.");
    }

    private static void Normalize(AttendanceStatusWriteDto dto)
    {
        dto.Code = dto.Code.Trim().ToUpperInvariant();
        dto.StatusName = dto.StatusName.Trim();
        dto.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        dto.ColorCode = string.IsNullOrWhiteSpace(dto.ColorCode) ? null : dto.ColorCode.Trim().ToUpperInvariant();
    }
}
