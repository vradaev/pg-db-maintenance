namespace DatabaseMaintenance.Models;

public class TelegramSettings
{
    public string BotToken { get; set; } = string.Empty;
    public long ChatId { get; set; }
} 