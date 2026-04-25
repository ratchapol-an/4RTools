using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RagnarokAutomation.Core;

public sealed record DiscordSendResult(bool Success, string ErrorMessage = "");

public sealed class DiscordBotClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;

    public async Task<bool> SendDirectMessageAsync(string botToken, string discordUserId, string message, CancellationToken cancellationToken)
    {
        DiscordSendResult result = await SendDirectMessageWithDiagnosticsAsync(
            botToken,
            discordUserId,
            message,
            cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<DiscordSendResult> SendDirectMessageWithDiagnosticsAsync(
        string botToken,
        string discordUserId,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(discordUserId))
        {
            return new DiscordSendResult(false, "bot token or discord user id is empty");
        }

        (string? channelId, string? channelError) = await EnsureDmChannelAsync(botToken, discordUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return new DiscordSendResult(false, $"failed to create DM channel: {channelError}");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, $"https://discord.com/api/v10/channels/{channelId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { content = message }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return new DiscordSendResult(true);
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new DiscordSendResult(
            false,
            $"send message failed: status={(int)response.StatusCode} {response.StatusCode}; body={responseBody}");
    }

    private async Task<(string? ChannelId, string? Error)> EnsureDmChannelAsync(
        string botToken,
        string discordUserId,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "https://discord.com/api/v10/users/@me/channels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { recipient_id = discordUserId }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (null, $"status={(int)response.StatusCode} {response.StatusCode}; body={errorBody}");
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(content);
        return doc.RootElement.TryGetProperty("id", out JsonElement value)
            ? (value.GetString(), null)
            : (null, $"missing channel id in response body={content}");
    }
}
