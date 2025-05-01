using Quartz;
using DatabaseMaintenance.Services;
using Npgsql;

namespace DatabaseMaintenance.Jobs;

public class ReindexJob : IJob
{
    private readonly DatabaseService _databaseService;
    private readonly TelegramService _telegramService;
    private readonly ILogger<ReindexJob> _logger;

    public ReindexJob(
        DatabaseService databaseService,
        TelegramService telegramService,
        ILogger<ReindexJob> logger)
    {
        _databaseService = databaseService;
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("Starting REINDEX");
            await _telegramService.SendMessageAsync("🛠️ <b>Starting REINDEX of database tables</b>");

            var stats = await _databaseService.ReindexTablesAsync();
            
            if (stats.TablesProcessed == 0)
            {
                await _telegramService.EditMessageAsync("🟢 <b>No tables require reindexing</b>");
                _logger.LogInformation("No tables require reindexing");
                return;
            }

            var message = new System.Text.StringBuilder();
            message.AppendLine("📊 <b>REINDEX Completed</b>");
            message.AppendLine();
            message.AppendLine("📈 <b>Summary</b>");
            message.AppendLine($"• Tables: <code>{stats.TablesProcessed}</code>");
            message.AppendLine($"• Time: <code>{stats.TotalTime.TotalSeconds:F2}</code> sec");
            message.AppendLine();
            message.AppendLine("📋 <b>Table Details</b>");
            message.AppendLine();
            message.AppendLine("<pre>");
            message.AppendLine("Table        Time");
            message.AppendLine("------------------");

            foreach (var table in stats.TableStats)
            {
                message.AppendLine($"{table.TableName.PadRight(12)} {table.Duration.TotalSeconds:F2} sec");
            }
            message.AppendLine("</pre>");

            await _telegramService.EditMessageAsync(message.ToString());
            _logger.LogInformation("REINDEX completed successfully");
        }
        catch (Exception ex)
        {
            var errorMessage = $"❌ <b>REINDEX Error:</b>\n<code>{ex.Message}</code>";
            await _telegramService.EditMessageAsync(errorMessage);
            _logger.LogError(ex, "Error during REINDEX execution");
            throw;
        }
    }

    private string EscapeMarkdown(string text)
    {
        return text
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("~", "\\~")
            .Replace("`", "\\`")
            .Replace(">", "\\>")
            .Replace("#", "\\#")
            .Replace("+", "\\+")
            .Replace("-", "\\-")
            .Replace("=", "\\=")
            .Replace("|", "\\|")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace(".", "\\.")
            .Replace("!", "\\!");
    }
}

public class TableReindexInfo
{
    public string TableName { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
} 