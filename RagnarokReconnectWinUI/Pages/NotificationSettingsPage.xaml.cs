using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using RagnarokAutomation.Core;

namespace RagnarokReconnectWinUI.Pages;

public sealed partial class NotificationSettingsPage : Page
{
    public sealed class UserListItem : INotifyPropertyChanged
    {
        public required UserDiscordMap Map { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Username => Map.Username;
        public string DiscordUserIdDisplay => string.IsNullOrWhiteSpace(Map.DiscordUserId) ? "(not set)" : Map.DiscordUserId;
        public string EditDiscordUserId { get; set; } = string.Empty;
        public bool IsEditing { get; private set; }
        public Visibility DisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

        public void StartEdit()
        {
            EditDiscordUserId = Map.DiscordUserId;
            IsEditing = true;
            RaiseEditStateChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditDiscordUserId)));
        }

        public void DiscardEdit()
        {
            EditDiscordUserId = Map.DiscordUserId;
            IsEditing = false;
            RaiseEditStateChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditDiscordUserId)));
        }

        public void ApplyEdit(string normalizedDiscordUserId)
        {
            Map.DiscordUserId = normalizedDiscordUserId;
            EditDiscordUserId = normalizedDiscordUserId;
            IsEditing = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscordUserIdDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditDiscordUserId)));
            RaiseEditStateChanged();
        }

        private void RaiseEditStateChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditVisibility)));
        }
    }

    public sealed class UsernameSelectionItem
    {
        public string Username { get; set; } = string.Empty;
        public string DiscordUserId { get; set; } = string.Empty;
        public string DisplayText =>
            string.IsNullOrWhiteSpace(DiscordUserId) ? $"{Username} (Discord ID not set)" : $"{Username} ({DiscordUserId})";
    }

    private readonly DiscordBotClient _discordBotClient = new(new HttpClient());
    public ObservableCollection<UserListItem> Users { get; } = [];
    public ObservableCollection<UsernameSelectionItem> AvailableUsers { get; } = [];

    public NotificationSettingsPage()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        Dictionary<string, string> byUsername = App.Services.Configuration.Users
            .Where(u => !string.IsNullOrWhiteSpace(u.Username))
            .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.DiscordUserId))?.DiscordUserId ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        HashSet<string> usernames = App.Services.Configuration.Characters
            .Select(c => c.Username)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string mappedUsername in App.Services.Configuration.Users
                     .Select(u => u.Username)
                     .Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            usernames.Add(mappedUsername);
        }

        AvailableUsers.Clear();
        foreach (string username in usernames.OrderBy(u => u, StringComparer.OrdinalIgnoreCase))
        {
            AvailableUsers.Add(new UsernameSelectionItem
            {
                Username = username,
                DiscordUserId = byUsername.TryGetValue(username, out string? discordUserId) ? discordUserId ?? string.Empty : string.Empty
            });
        }

        Users.Clear();
        foreach (UserDiscordMap userMap in App.Services.Configuration.Users.OrderBy(a => a.Username))
        {
            Users.Add(new UserListItem { Map = userMap });
        }
    }

    private void SaveUser_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string username = (UsernameDropdown.SelectedItem as UsernameSelectionItem)?.Username ?? string.Empty;
        string rawDiscordUserId = DiscordUserIdInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        if (!TryNormalizeDiscordUserId(rawDiscordUserId, out string discordUserId))
        {
            StatusText.Text = $"Invalid Discord User ID for '{username}'. Use numeric user ID (snowflake) or <@id>.";
            App.Services.AddUiLog($"save mapping failed: username='{username}' invalid discordUserId='{rawDiscordUserId}'");
            return;
        }

        UserDiscordMap? existing = App.Services.Configuration.Users.FirstOrDefault(a =>
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            App.Services.Configuration.Users.Add(new UserDiscordMap
            {
                Username = username,
                DiscordUserId = discordUserId
            });
        }
        else
        {
            existing.DiscordUserId = discordUserId;
        }

        Reload();
    }

    private void UsernameDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string username = (UsernameDropdown.SelectedItem as UsernameSelectionItem)?.Username ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
        {
            DiscordUserIdInput.Text = string.Empty;
            return;
        }

        UserDiscordMap? existing = App.Services.Configuration.Users.FirstOrDefault(a =>
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        DiscordUserIdInput.Text = existing?.DiscordUserId ?? string.Empty;
    }

    private async void SaveAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Services.SaveConfigurationAsync();
    }

    private void EditUser_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not UserListItem item)
        {
            return;
        }

        foreach (UserListItem other in Users.Where(u => u != item && u.IsEditing))
        {
            other.DiscardEdit();
        }

        item.StartEdit();
        UsernameSelectionItem? selected = AvailableUsers.FirstOrDefault(u =>
            string.Equals(u.Username, item.Username, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            UsernameDropdown.SelectedItem = selected;
        }
        StatusText.Text = $"Editing mapping for '{item.Username}'.";
    }

    private async void SaveUserRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not UserListItem item)
        {
            return;
        }

        if (!TryNormalizeDiscordUserId(item.EditDiscordUserId.Trim(), out string normalizedDiscordUserId))
        {
            StatusText.Text = $"Invalid Discord User ID for '{item.Username}'. Use numeric user ID (snowflake) or <@id>.";
            App.Services.AddUiLog($"save row mapping failed: username='{item.Username}' invalid discordUserId='{item.EditDiscordUserId}'");
            return;
        }

        item.ApplyEdit(normalizedDiscordUserId);
        Reload();
        await App.Services.SaveConfigurationAsync();
        StatusText.Text = $"Saved mapping for '{item.Username}'.";
    }

    private void DiscardUserRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not UserListItem item)
        {
            return;
        }

        item.DiscardEdit();
        StatusText.Text = $"Discarded edits for '{item.Username}'.";
    }

    private async void TestSend_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        UserDiscordMap? userMap = button.DataContext switch
        {
            UserListItem listItem => listItem.Map,
            UserDiscordMap directMap => directMap,
            _ => null
        };
        if (userMap is null)
        {
            return;
        }

        string botToken = App.Services.Configuration.Monitoring.DiscordBotToken.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            StatusText.Text = "Discord bot token is empty. Save it in Settings first.";
            App.Services.AddUiLog("test-send failed: Discord bot token is empty");
            return;
        }

        if (string.IsNullOrWhiteSpace(userMap.DiscordUserId))
        {
            StatusText.Text = $"Discord User ID is empty for username '{userMap.Username}'.";
            App.Services.AddUiLog($"test-send failed: username='{userMap.Username}' has empty Discord user id");
            return;
        }

        if (!TryNormalizeDiscordUserId(userMap.DiscordUserId, out string normalizedDiscordUserId))
        {
            StatusText.Text = $"Discord User ID for '{userMap.Username}' is invalid. Use numeric user ID (snowflake).";
            App.Services.AddUiLog($"test-send failed: username='{userMap.Username}' invalid discordUserId='{userMap.DiscordUserId}'");
            return;
        }

        button.IsEnabled = false;
        string previousLabel = button.Content?.ToString() ?? "Test Send";
        button.Content = "Sending...";
        try
        {
            string simulatedAlert = string.Join(
                "\n",
                $"Character: (test) {userMap.Username}",
                "Status: Disconnected",
                "Cause: LikelyServerDown");

            DiscordSendResult sendResult = await _discordBotClient.SendDirectMessageWithDiagnosticsAsync(
                botToken,
                normalizedDiscordUserId,
                simulatedAlert,
                CancellationToken.None);

            StatusText.Text = sendResult.Success
                ? $"Test message sent to '{userMap.Username}' ({userMap.DiscordUserId})."
                : $"Failed to send test message to '{userMap.Username}' ({userMap.DiscordUserId}). Check bot permissions and ID.";
            App.Services.AddUiLog(sendResult.Success
                ? $"test-send success: username='{userMap.Username}' discordUserId='{userMap.DiscordUserId}'"
                : $"test-send failed: username='{userMap.Username}' discordUserId='{userMap.DiscordUserId}' error={sendResult.ErrorMessage}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Test send error: {ex.Message}";
            App.Services.AddUiLog($"test-send exception: username='{userMap.Username}' discordUserId='{userMap.DiscordUserId}' error={ex}");
        }
        finally
        {
            button.Content = previousLabel;
            button.IsEnabled = true;
        }
    }

    private static bool TryNormalizeDiscordUserId(string input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate = input.Trim();
        if (candidate.StartsWith("<@") && candidate.EndsWith(">"))
        {
            candidate = candidate.Replace("<@", string.Empty).Replace(">", string.Empty).Replace("!", string.Empty);
        }

        if (!ulong.TryParse(candidate, out _))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
