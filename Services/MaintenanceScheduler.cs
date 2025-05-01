using Quartz;
using Quartz.Impl;
using Microsoft.Extensions.Hosting;
using DatabaseMaintenance.Jobs;
using Microsoft.Extensions.Options;
using DatabaseMaintenance.Models;

namespace DatabaseMaintenance.Services;

public class MaintenanceScheduler : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IOptions<MaintenanceSettings> _maintenanceSettings;
    private readonly ILogger<MaintenanceScheduler> _logger;
    private IScheduler? _scheduler;

    public MaintenanceScheduler(
        ISchedulerFactory schedulerFactory,
        IOptions<MaintenanceSettings> maintenanceSettings,
        ILogger<MaintenanceScheduler> logger)
    {
        _schedulerFactory = schedulerFactory;
        _maintenanceSettings = maintenanceSettings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scheduler...");
        _logger.LogInformation("Vacuum schedule: {Schedule}", _maintenanceSettings.Value.VacuumTables.Schedule);
        _logger.LogInformation("Reindex schedule: {Schedule}", _maintenanceSettings.Value.Reindex.Schedule);

        _scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        await _scheduler.Start(cancellationToken);

        // Настройка задачи для вакуума
        var vacuumJob = JobBuilder.Create<VacuumJob>()
            .WithIdentity("vacuumJob", "maintenance")
            .Build();

        var vacuumTrigger = TriggerBuilder.Create()
            .WithIdentity("vacuumTrigger", "maintenance")
            .WithCronSchedule(_maintenanceSettings.Value.VacuumTables.Schedule)
            .Build();

        // Настройка задачи для реиндексации
        var reindexJob = JobBuilder.Create<ReindexJob>()
            .WithIdentity("reindexJob", "maintenance")
            .Build();

        var reindexTrigger = TriggerBuilder.Create()
            .WithIdentity("reindexTrigger", "maintenance")
            .WithCronSchedule(_maintenanceSettings.Value.Reindex.Schedule)
            .Build();

        // Добавляем обработчики ошибок
        _scheduler.ListenerManager.AddJobListener(new JobErrorHandler(_logger));
        _scheduler.ListenerManager.AddTriggerListener(new TriggerErrorHandler(_logger));

        await _scheduler.ScheduleJob(vacuumJob, vacuumTrigger, cancellationToken);
        await _scheduler.ScheduleJob(reindexJob, reindexTrigger, cancellationToken);

        _logger.LogInformation("Jobs scheduled successfully");
        _logger.LogInformation("Next vacuum run: {NextRun}", await _scheduler.GetTrigger(vacuumTrigger.Key, cancellationToken));
        _logger.LogInformation("Next reindex run: {NextRun}", await _scheduler.GetTrigger(reindexTrigger.Key, cancellationToken));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler != null)
        {
            await _scheduler.Shutdown(cancellationToken);
            _logger.LogInformation("Maintenance scheduler stopped");
        }
    }
}

public class JobErrorHandler : IJobListener
{
    private readonly ILogger<MaintenanceScheduler> _logger;

    public JobErrorHandler(ILogger<MaintenanceScheduler> logger)
    {
        _logger = logger;
    }

    public string Name => "JobErrorHandler";

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        if (jobException != null)
        {
            _logger.LogError(jobException, "Ошибка при выполнении джоба {JobName}", context.JobDetail.Key.Name);
        }
        return Task.CompletedTask;
    }
}

public class TriggerErrorHandler : ITriggerListener
{
    private readonly ILogger<MaintenanceScheduler> _logger;

    public TriggerErrorHandler(ILogger<MaintenanceScheduler> logger)
    {
        _logger = logger;
    }

    public string Name => "TriggerErrorHandler";

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Триггер {TriggerName} пропущен", trigger.Key.Name);
        return Task.CompletedTask;
    }

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
} 