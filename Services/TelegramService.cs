using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using DatabaseMaintenance.Models;

namespace DatabaseMaintenance.Services;

public class TelegramService
{
    private readonly string _botToken;
    private readonly long _chatId;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramService> _logger;
    private long? _lastMessageId;

    public TelegramService(
        IOptions<TelegramSettings> settings,
        ILogger<TelegramService> logger)
    {
        _botToken = settings.Value.BotToken;
        _chatId = settings.Value.ChatId;
        _httpClient = new HttpClient();
        _logger = logger;
    }

    public async Task SendMessageAsync(string message, bool disableNotification = true)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new
            {
                chat_id = _chatId,
                text = message,
                parse_mode = "HTML",
                disable_notification = disableNotification
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error sending message to Telegram. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseContent);
                throw new Exception($"Error sending message to Telegram: {responseContent}");
            }

            // Save message ID for future editing
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            if (responseData.TryGetProperty("result", out var result) && 
                result.TryGetProperty("message_id", out var messageId))
            {
                _lastMessageId = messageId.GetInt64();
            }

            _logger.LogInformation("Message sent to Telegram successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Telegram");
            throw;
        }
    }

    public async Task EditMessageAsync(string message)
    {
        if (!_lastMessageId.HasValue)
        {
            _logger.LogWarning("No message ID available for editing");
            return;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{_botToken}/editMessageText";
            var payload = new
            {
                chat_id = _chatId,
                message_id = _lastMessageId,
                text = message,
                parse_mode = "HTML"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error editing message in Telegram. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseContent);
                throw new Exception($"Error editing message in Telegram: {responseContent}");
            }

            _logger.LogInformation("Message edited in Telegram successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing message in Telegram");
            throw;
        }
    }
} 