// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using RagnarokAutomation.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace RagnarokReconnectWinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        MonitoringSettings settings = App.Services.Configuration.Monitoring;
        DiscordTokenInput.Password = settings.DiscordBotToken;
        PollIntervalInput.Value = settings.PollIntervalSeconds;
        DedupWindowInput.Value = settings.DedupWindowMinutes;
        HeartbeatWindowInput.Value = settings.IncidentHeartbeatMinutes;
        SimulateInternetDownToggle.IsOn = settings.SimulateInternetDown;
    }

    private async void SaveSettings_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        MonitoringSettings settings = App.Services.Configuration.Monitoring;
        settings.DiscordBotToken = DiscordTokenInput.Password.Trim();
        settings.PollIntervalSeconds = (int)Math.Clamp(PollIntervalInput.Value, 1, 60);
        settings.DedupWindowMinutes = (int)Math.Clamp(DedupWindowInput.Value, 1, 120);
        settings.IncidentHeartbeatMinutes = (int)Math.Clamp(HeartbeatWindowInput.Value, 5, 240);
        settings.SimulateInternetDown = SimulateInternetDownToggle.IsOn;
        await App.Services.SaveConfigurationAsync();
        SavedText.Text = $"Saved at {DateTimeOffset.Now:HH:mm:ss}";
    }

    private async void OpenConfigFile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string configPath = App.Services.ConfigurationFilePath;
        if (!File.Exists(configPath))
        {
            await App.Services.SaveConfigurationAsync();
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{configPath}\"",
            UseShellExecute = true
        });
    }

    private void OpenConfigFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string folderPath = App.Services.StorageDirectoryPath;
        Directory.CreateDirectory(folderPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }
}
