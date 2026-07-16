using Accounts.DTOs;
using Accounts.Models;
using AutoMapper;

namespace Accounts.Mapping;

public sealed class AttendanceStatusProfile : Profile
{
    public AttendanceStatusProfile()
    {
        CreateMap<StatusMaster, AttendanceStatusDto>();
        CreateMap<CreateAttendanceStatusDto, StatusMaster>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.CreatedDate, o => o.Ignore())
            .ForMember(d => d.ModifiedDate, o => o.Ignore());
        CreateMap<UpdateAttendanceStatusDto, StatusMaster>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.CreatedDate, o => o.Ignore())
            .ForMember(d => d.ModifiedDate, o => o.Ignore());
    }
}
