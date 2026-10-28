using Accounts.Models.Scheduler;

namespace Accounts.Services.Interfaces;

public interface IModuleJob
{
    ModuleJobInfo Info { get; }
    Task<string> ExecuteAsync(int tenantId,CancellationToken cancellationToken);
}
