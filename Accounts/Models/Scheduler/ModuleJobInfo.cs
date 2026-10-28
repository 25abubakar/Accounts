

namespace Accounts.Models.Scheduler;

public sealed record ModuleJobInfo(string Key,string Module,string Name,bool ReadOnly,string Description,
    bool SystemOnly=false,string? ConcurrencyKey=null);
