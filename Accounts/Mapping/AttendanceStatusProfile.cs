using Accounts.DTOs;
using Accounts.Models;
using AutoMapper;

namespace Accounts.Mapping;

public sealed class AttendanceStatusProfile : Profile
{
    public AttendanceStatusProfile()
    {
        CreateMap<AttendanceStatusMaster, AttendanceStatusDto>();
        CreateMap<CreateAttendanceStatusDto, AttendanceStatusMaster>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.CreatedDate, o => o.Ignore())
            .ForMember(d => d.ModifiedDate, o => o.Ignore());
        CreateMap<UpdateAttendanceStatusDto, AttendanceStatusMaster>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.CreatedDate, o => o.Ignore())
            .ForMember(d => d.ModifiedDate, o => o.Ignore());
    }
}
