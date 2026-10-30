using Accounts.Authorization;
using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Accounts.Services.Scheduler.Jobs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public static class SchedulerModule
{
    // Integrated in-process scheduler: runs directly inside the Accounts application host.
    public static IServiceCollection AddAccountsScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSchedulerStorage(configuration);
        services.AddScoped<SchedulerAccessFilter>();
        services.AddHostedService<SchedulerRealtimeRelay>();
        services.AddHostedService<SchedulerExecutionService>();
        services.AddAuthorization(options => options.AddPolicy("SchedulerAdmin", policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasClaim("is_tenant_admin", "true") || context.User.IsInRole("TenantAdmin"))));
        return services;
    }

    public static IServiceCollection AddSchedulerStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var business = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Accounts connection is required.");
        var scheduler = configuration.GetConnectionString("SchedulerConnection") ?? business;
        services.AddSingleton(new SchedulerStorage(scheduler, business));
        services.AddHttpContextAccessor();
        services.AddScoped<SchedulerTenant>();
        services.AddDbContext<SchedulerDbContext>(options => options.UseSqlServer(scheduler));
        services.AddSingleton<SchedulerSqlStore>();
        services.AddScoped<ISchedulerService,SchedulerApplicationService>();
        services.AddScoped<IModuleJob, SchedulerHealthJob>();
        services.AddScoped<IModuleJob, AttendanceFinalizationJob>();
        services.AddScoped<IModuleJob, AssessmentGenerationJob>();
        services.AddScoped<IModuleJob, AssessmentRemindersJob>();
        services.AddScoped<IModuleJob, ProcessAutoTransferJob>();
        services.AddScoped<IModuleJob, ChatExpiryJob>();
        services.AddScoped<IModuleJob, ConversionCleanupJob>();
        services.AddScoped<IModuleJob, IdempotencyExpiryJob>();
        services.AddScoped<IModuleJob, FaxReconciliationJob>();
        services.AddScoped<ScheduledTenantContext>();
        services.AddScoped<SchedulerDefinitionPlan>();
        services.AddSingleton<ScheduleSynchronizer>();
        services.AddSingleton<SchedulerCalendar>();
        services.AddScoped<ModuleJobDispatcher>();
        services.AddSingleton<SchedulerQueue>();

        // Production Job Services executed by the scheduler dispatcher
        services.AddScoped<AttendanceFinalizationJobService>();
        services.AddScoped<AssessmentCycleJobService>();
        services.AddScoped<ProcessAutoTransferJobService>();
        services.AddScoped<ChatViewOnceExpiryJobService>();
        services.AddScoped<ConversionFileCleanupJobService>();
        services.AddScoped<IdempotencyExpiryJobService>();
        return services;
    }
}
