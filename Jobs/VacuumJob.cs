using Quartz;
using DatabaseMaintenance.Services;
using Npgsql;

namespace DatabaseMaintenance.Jobs;

public class VacuumJob : IJob
{
    private readonly DatabaseService _databaseService;
    private readonly TelegramService _telegramService;
    private readonly ILogger<VacuumJob> _logger;

    public VacuumJob(
        DatabaseService databaseService,
        TelegramService telegramService,
        ILogger<VacuumJob> logger)
    {
        _databaseService = databaseService;
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("Starting VACUUM");
            await _telegramService.SendMessageAsync("⚒️ <b>Starting database maintenance</b>\n#dbmaintenance");

            var stats = await _databaseService.VacuumTablesAsync();
            
            if (stats.TablesProcessed == 0)
            {
                await _telegramService.EditMessageAsync("🟢 <b>No tables require maintenance</b>\n#dbmaintenance");
                _logger.LogInformation("No tables require maintenance");
                return;
            }

            var message = new System.Text.StringBuilder();
            message.AppendLine("📊 <b>Maintenance Report</b>");
            message.AppendLine();
            message.AppendLine("📈 <b>Summary</b>");
            message.AppendLine($"• Tables: <code>{stats.TablesProcessed}</code>");
            message.AppendLine($"• Time: <code>{stats.TotalTime.TotalSeconds:F2}</code> sec");
            message.AppendLine($"• Space freed: <code>{FormatSize(stats.TotalSpaceFreed)}</code>");
            message.AppendLine();
            message.AppendLine("📋 <b>Table Details</b>");
            message.AppendLine();
            message.AppendLine("<pre>");
            message.AppendLine("Table        Before     After      Freed");
            message.AppendLine("--------------------------------------------");

            foreach (var table in stats.TableStats)
            {
                message.AppendLine($"{table.TableName.PadRight(12)} {FormatSize(table.SizeBefore).PadLeft(8)} {FormatSize(table.SizeAfter).PadLeft(8)} {FormatSize(table.SpaceFreed).PadLeft(10)}");
            }
            message.AppendLine("</pre>");
            message.AppendLine("\n#dbmaintenance");

            await _telegramService.EditMessageAsync(message.ToString());
            _logger.LogInformation("VACUUM completed successfully");
        }
        catch (Exception ex)
        {
            var errorMessage = $"❌ <b>Maintenance Error:</b>\n<code>{ex.Message}</code>\n#dbmaintenance";
            await _telegramService.EditMessageAsync(errorMessage);
            _logger.LogError(ex, "Error during VACUUM execution");
            throw;
        }
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
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