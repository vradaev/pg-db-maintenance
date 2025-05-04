using DatabaseMaintenance.Services;
using DatabaseMaintenance.Jobs;
using DatabaseMaintenance.Models;
using NLog;
using NLog.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();

try
{
    var builder = Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            logging.AddNLog();
        })
        .ConfigureServices((hostContext, services) =>
        {
            // Конфигурация
            services.Configure<DatabaseSettings>(hostContext.Configuration.GetSection("Database"));
            services.Configure<TelegramSettings>(hostContext.Configuration.GetSection("Telegram"));
            services.Configure<MaintenanceSettings>(hostContext.Configuration.GetSection("Maintenance"));

            // Сервисы
            services.AddSingleton<DatabaseService>();
            services.AddSingleton<TelegramService>();
            services.AddSingleton<MaintenanceScheduler>();
            services.AddHostedService<MaintenanceScheduler>();

            // Quartz
            services.AddQuartz(q =>
            {
                q.UseMicrosoftDependencyInjectionJobFactory();
                q.UseSimpleTypeLoader();
                q.UseInMemoryStore();
                q.UseDefaultThreadPool(tp =>
                {
                    tp.MaxConcurrency = 10;
                });
            });

            services.AddQuartzHostedService(q => 
            {
                q.WaitForJobsToComplete = true;
                q.StartDelay = TimeSpan.FromSeconds(5);
            });

            // Регистрация заданий
            services.AddTransient<VacuumJob>();
            services.AddTransient<ReindexJob>();
            services.AddTransient<WaypointCleanupJob>();
        })
        .Build();

    await builder.RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Stopped program because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}