using Accounts.Services.Scheduler.Jobs;
using Accounts.Data;
using Accounts.Idempotency;
using Accounts.Models.Scheduler;
using Accounts.Services;
using Accounts.Services.Interfaces;
using Accounts.Services.Scheduler;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace Accounts.Scheduler.Worker;

public static class WorkerBusinessServices
{
    public static IServiceCollection AddBusinessJobs(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddScoped<ITenantService>(provider=>provider.GetRequiredService<ScheduledTenantContext>());
        services.AddDbContext<ApplicationDbContext>((provider,options)=>options.UseSqlServer(provider.GetRequiredService<SchedulerStorage>().BusinessConnection));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RbacService>();
        services.AddScoped<AssessmentCycleJobService>();
        services.AddScoped<AttendanceFinalizationService>();
        services.AddScoped<AttendanceFinalizationJobService>();
        services.AddScoped<IRealtimePublisher,SchedulerOutboxPublisher>();
        services.AddScoped<ProcessAutoTransferJobService>();
        services.AddScoped<ChatViewOnceExpiryJobService>();
        services.AddScoped<ConversionFileCleanupJobService>();
        services.AddScoped<IIdempotencyStore,EfIdempotencyStore>();
        services.AddScoped<IdempotencyExpiryJobService>();
        services.AddOptions<FileConversionOptions>().Bind(configuration.GetSection(FileConversionOptions.SectionName));
        services.AddOptions<IdempotencyOptions>().Bind(configuration.GetSection(IdempotencyOptions.SectionName));
        services.AddSingleton<IWebHostEnvironment>(new BusinessFileEnvironment(configuration));
        return services;
    }
    private sealed class BusinessFileEnvironment : IWebHostEnvironment
    {
        public BusinessFileEnvironment(IConfiguration configuration)
        {
            var configured=configuration["Scheduler:ApplicationContentRoot"]??throw new InvalidOperationException("Set Scheduler:ApplicationContentRoot to the existing Accounts backend directory.");
            ContentRootPath=Path.GetFullPath(configured);
            if(!Directory.Exists(ContentRootPath))throw new InvalidOperationException("Accounts application content root does not exist.");
            WebRootPath=Path.Combine(ContentRootPath,"wwwroot");
        }
        public string EnvironmentName {get;set;}="Production";
        public string ApplicationName {get;set;}="Accounts";
        public string ContentRootPath {get;set;}
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public string WebRootPath {get;set;}
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
}
