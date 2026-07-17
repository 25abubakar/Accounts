using Accounts.DTOs;
using Accounts.Models;
using AutoMapper;

namespace Accounts.Mapping;

public sealed class AttendanceStatusProfile : Profile
{
    public AttendanceStatusProfile()
    {
        CreateMap<ProcessStatusStyle, AttendanceStatusDto>()
            .ForMember(d => d.ProcessName, o => o.MapFrom(s => s.Process.ProcessName))
            .ForMember(d => d.StatusName, o => o.MapFrom(s => s.Status.StatusName))
            .ForMember(d => d.ColorName, o => o.MapFrom(s => s.ColorStyle.ColorName))
            .ForMember(d => d.ColorCode, o => o.MapFrom(s => s.ColorStyle.ColorCode))
            .ForMember(d => d.FontColor, o => o.MapFrom(s => s.ColorStyle.FontColor))
            .ForMember(d => d.FontSize, o => o.MapFrom(s => s.ColorStyle.FontSize));
        CreateMap<CreateAttendanceStatusDto, ProcessStatusStyle>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.ProcessId, o => o.Ignore()).ForMember(d => d.StatusId, o => o.Ignore()).ForMember(d => d.ColorStyleId, o => o.Ignore())
            .ForMember(d => d.Process, o => o.Ignore()).ForMember(d => d.Status, o => o.Ignore()).ForMember(d => d.ColorStyle, o => o.Ignore())
            .ForMember(d => d.CreatedDate, o => o.Ignore())
            .ForMember(d => d.ModifiedDate, o => o.Ignore());
        CreateMap<UpdateAttendanceStatusDto, ProcessStatusStyle>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.ProcessId, o => o.Ignore()).ForMember(d => d.StatusId, o => o.Ignore()).ForMember(d => d.ColorStyleId, o => o.Ignore())
            .ForMember(d => d.Process, o => o.Ignore()).ForMember(d => d.Status, o => o.Ignore()).ForMember(d => d.ColorStyle, o => o.Ignore())
            .ForMember(d => d.CreatedDate, o => o.Ignore())
            .ForMember(d => d.ModifiedDate, o => o.Ignore());
    }
}
