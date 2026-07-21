using Accounts.DTOs;
using Accounts.Models;
using Accounts.Repositories.Interfaces;
using Accounts.Services.Interfaces;
using AutoMapper;
using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AttendanceStatusService : IAttendanceStatusService
{
    private readonly IAttendanceStatusRepository _repository;
    private readonly IMapper _mapper;
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;

    public AttendanceStatusService(IAttendanceStatusRepository repository, IMapper mapper, ApplicationDbContext db, ITenantService tenant)
    {
        _repository = repository;
        _mapper = mapper;
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<AttendanceStatusDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _mapper.Map<IReadOnlyList<AttendanceStatusDto>>(await _repository.GetAllAsync(cancellationToken));

    public async Task<AttendanceStatusDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _mapper.Map<AttendanceStatusDto?>(await _repository.GetByIdAsync(id, cancellationToken));

    public async Task<AttendanceStatusDto> CreateAsync(CreateAttendanceStatusDto dto, CancellationToken cancellationToken = default)
    {
        Normalize(dto);
        await EnsureUniqueAsync(dto.Code, dto.StatusName, null, cancellationToken);
        var entity = _mapper.Map<ProcessStatusStyle>(dto);
        entity.TenantId = _tenant.IsSuperAdmin ? null : _tenant.TenantId;
        entity.IsSystem = _tenant.IsSuperAdmin;
        await SetRelationsAsync(entity, dto, cancellationToken);
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
        await SetRelationsAsync(entity, dto, cancellationToken);
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
        dto.ProcessName = dto.ProcessName.Trim();
        dto.StatusName = dto.StatusName.Trim();
        dto.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        dto.ColorCode = string.IsNullOrWhiteSpace(dto.ColorCode) ? null : dto.ColorCode.Trim().ToUpperInvariant();
        dto.ColorName = dto.ColorName.Trim();
        dto.FontColor = dto.FontColor.Trim().ToUpperInvariant();
        dto.FontSize = dto.FontSize.Trim();
    }

    private async Task SetRelationsAsync(ProcessStatusStyle entity, AttendanceStatusWriteDto dto, CancellationToken ct)
    {
        var process = await _db.Processes.FirstOrDefaultAsync(x => x.ProcessName == dto.ProcessName, ct);
        if (process == null) { process = new ProcessMaster { ProcessName = dto.ProcessName }; _db.Processes.Add(process); }

        var status = await _db.Statuses.FirstOrDefaultAsync(x => x.StatusName == dto.StatusName, ct);
        if (status == null) { status = new StatusDefinition { StatusName = dto.StatusName }; _db.Statuses.Add(status); }

        var colorCode = dto.ColorCode ?? "#64748B";
        var style = await _db.ColorStyles.FirstOrDefaultAsync(x => x.ColorName == dto.ColorName && x.ColorCode == colorCode && x.FontColor == dto.FontColor && x.FontSize == dto.FontSize, ct);
        if (style == null)
        {
            style = new ColorStyle { ColorName = dto.ColorName, ColorCode = colorCode, FontColor = dto.FontColor, FontSize = dto.FontSize };
            _db.ColorStyles.Add(style);
        }

        entity.Process = process;
        entity.Status = status;
        entity.ColorStyle = style;
    }
}
