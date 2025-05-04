using Quartz;
using DatabaseMaintenance.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DatabaseMaintenance.Models;

namespace DatabaseMaintenance.Jobs;

public class WaypointCleanupJob : IJob
{
    private readonly DatabaseService _databaseService;
    private readonly TelegramService _telegramService;
    private readonly ILogger<WaypointCleanupJob> _logger;
    private readonly IOptions<MaintenanceSettings> _maintenanceSettings;

    public WaypointCleanupJob(
        DatabaseService databaseService,
        TelegramService telegramService,
        ILogger<WaypointCleanupJob> logger,
        IOptions<MaintenanceSettings> maintenanceSettings)
    {
        _databaseService = databaseService;
        _telegramService = telegramService;
        _logger = logger;
        _maintenanceSettings = maintenanceSettings;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var settings = _maintenanceSettings.Value.WaypointCleanup;
            var startTime = TimeSpan.Parse(settings.StartTime);
            var endTime = TimeSpan.Parse(settings.EndTime);

            _logger.LogInformation("Starting waypoint cleanup (older than {Months} months)", 
                settings.RetentionMonths);
            await _telegramService.SendMessageAsync(
                $"🧹 <b>Starting waypoint cleanup (older than {settings.RetentionMonths} months)</b>");

            // Получаем количество строк до удаления
            var rowsBefore = await _databaseService.GetWaypointTableRowCountAsync();
            var totalDeleted = 0;
            var startDateTime = DateTime.UtcNow;

            // Удаляем батчи, пока есть что удалять и не вышли за временной интервал
            int deleted;
            do
            {
                // Проверяем, не вышли ли за временной интервал
                var currentTime = DateTime.UtcNow.TimeOfDay;
                if (currentTime >= endTime)
                {
                    _logger.LogInformation("Stopping cleanup: current time {Time} is outside allowed interval {Start}-{End}", 
                        currentTime, startTime, endTime);
                    break;
                }

                deleted = await _databaseService.DeleteOldWaypointsBatchAsync();
                totalDeleted += deleted;
                _logger.LogInformation("Deleted {Count} rows in current batch", deleted);
            } while (deleted > 0);

            // Получаем количество строк после удаления
            var rowsAfter = await _databaseService.GetWaypointTableRowCountAsync();
            var duration = DateTime.UtcNow - startDateTime;

            var message = new System.Text.StringBuilder();
            message.AppendLine("🧹 <b>Waypoint Cleanup Completed</b>");
            message.AppendLine();
            message.AppendLine("📈 <b>Summary</b>");
            message.AppendLine("<pre>");
            message.AppendLine("Rows        Before     After      Deleted");
            message.AppendLine("--------------------------------------------");
            message.AppendLine($"{FormatNumber(rowsBefore).PadLeft(12)} {FormatNumber(rowsAfter).PadLeft(10)} {FormatNumber(totalDeleted).PadLeft(10)}");
            message.AppendLine("</pre>");
            message.AppendLine("\n#waypoint_cleanup");

            await _telegramService.EditMessageAsync(message.ToString());
            _logger.LogInformation("Cleanup completed: deleted {Count}, rows before {Before}, after {After}, duration {Duration}", 
                totalDeleted, rowsBefore, rowsAfter, duration);
        }
        catch (Exception ex)
        {
            var errorMessage = $"❌ <b>Waypoint cleanup error:</b>\n<code>{ex.Message}</code>\n#waypoint_cleanup";
            await _telegramService.EditMessageAsync(errorMessage);
            _logger.LogError(ex, "Error during waypoint cleanup");
            throw;
        }
    }

    private string FormatNumber(long number)
    {
        return number.ToString("N0");
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F2}{sizes[order]}";
    }
} 