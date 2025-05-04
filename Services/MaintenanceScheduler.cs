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
        if (_maintenanceSettings.Value.VacuumTables.Enabled)
        {
            var vacuumJob = JobBuilder.Create<VacuumJob>()
                .WithIdentity("vacuumJob", "maintenance")
                .Build();

            var vacuumTrigger = TriggerBuilder.Create()
                .WithIdentity("vacuumTrigger", "maintenance")
                .WithCronSchedule(_maintenanceSettings.Value.VacuumTables.Schedule)
                .Build();

            await _scheduler.ScheduleJob(vacuumJob, vacuumTrigger, cancellationToken);
            _logger.LogInformation("Vacuum job scheduled");
            _logger.LogInformation("Next vacuum run: {NextRun}", await _scheduler.GetTrigger(vacuumTrigger.Key, cancellationToken));
        }
        else
        {
            _logger.LogInformation("Vacuum job is disabled");
        }

        // Настройка задачи для реиндексации
        if (_maintenanceSettings.Value.Reindex.Enabled)
        {
            var reindexJob = JobBuilder.Create<ReindexJob>()
                .WithIdentity("reindexJob", "maintenance")
                .Build();

            var reindexTrigger = TriggerBuilder.Create()
                .WithIdentity("reindexTrigger", "maintenance")
                .WithCronSchedule(_maintenanceSettings.Value.Reindex.Schedule)
                .Build();

            await _scheduler.ScheduleJob(reindexJob, reindexTrigger, cancellationToken);
            _logger.LogInformation("Reindex job scheduled");
            _logger.LogInformation("Next reindex run: {NextRun}", await _scheduler.GetTrigger(reindexTrigger.Key, cancellationToken));
        }
        else
        {
            _logger.LogInformation("Reindex job is disabled");
        }

        // Настройка задачи для очистки waypoint
        if (_maintenanceSettings.Value.WaypointCleanup.Enabled)
        {
            var cleanupJob = JobBuilder.Create<WaypointCleanupJob>()
                .WithIdentity("cleanupJob", "maintenance")
                .Build();

            var cleanupTrigger = TriggerBuilder.Create()
                .WithIdentity("cleanupTrigger", "maintenance")
                .WithCronSchedule(_maintenanceSettings.Value.WaypointCleanup.Schedule)
                .UsingJobData("startTime", _maintenanceSettings.Value.WaypointCleanup.StartTime)
                .UsingJobData("endTime", _maintenanceSettings.Value.WaypointCleanup.EndTime)
                .Build();

            await _scheduler.ScheduleJob(cleanupJob, cleanupTrigger, cancellationToken);
            _logger.LogInformation("Cleanup job scheduled");
            _logger.LogInformation("Next cleanup run: {NextRun}", await _scheduler.GetTrigger(cleanupTrigger.Key, cancellationToken));
        }
        else
        {
            _logger.LogInformation("Cleanup job is disabled");
        }

        // Добавляем обработчики ошибок
        _scheduler.ListenerManager.AddJobListener(new JobErrorHandler(_logger));
        _scheduler.ListenerManager.AddTriggerListener(new TriggerErrorHandler(_logger));
        _scheduler.ListenerManager.AddTriggerListener(new TimeCheckTriggerListener(_logger));
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

public class TimeCheckTriggerListener : ITriggerListener
{
    private readonly ILogger<MaintenanceScheduler> _logger;

    public TimeCheckTriggerListener(ILogger<MaintenanceScheduler> logger)
    {
        _logger = logger;
    }

    public string Name => "TimeCheckTriggerListener";

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (trigger.JobKey.Name == "cleanupJob")
        {
            var startTime = TimeSpan.Parse(trigger.JobDataMap.GetString("startTime") ?? "01:00");
            var endTime = TimeSpan.Parse(trigger.JobDataMap.GetString("endTime") ?? "05:00");
            var currentTime = DateTime.UtcNow.TimeOfDay;

            if (currentTime < startTime || currentTime >= endTime)
            {
                _logger.LogInformation("Skipping cleanup job: current time {CurrentTime} is outside allowed interval {StartTime}-{EndTime}", 
                    currentTime, startTime, endTime);
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
} 