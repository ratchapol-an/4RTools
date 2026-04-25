using System;
using System.Collections.ObjectModel;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using RagnarokAutomation.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace RagnarokReconnectWinUI.Pages;

public sealed partial class HomePage : Page
{
    public ObservableCollection<MonitoringIncident> Incidents => App.Services.RecentIncidents;
    public ObservableCollection<ProcessMonitoringState> ProcessStates => App.Services.ProcessStates;

    public HomePage()
    {
        InitializeComponent();
        StatusText.Text = App.Services.MonitoringEngine.IsRunning ? "Status: running" : "Status: stopped";
        _ = App.Services.RefreshMonitoringStatesAsync();
    }

    private async void StartMonitoring_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Services.MonitoringEngine.StartAsync();
        await App.Services.RefreshMonitoringStatesAsync();
        StatusText.Text = "Status: running";
    }

    private void StopMonitoring_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        App.Services.MonitoringEngine.Stop();
        StatusText.Text = "Status: stopped";
    }

    private async void PollNow_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Services.MonitoringEngine.PollAndNotifyAsync(CancellationToken.None);
        await App.Services.RefreshMonitoringStatesAsync();
        StatusText.Text = $"Last poll: {DateTimeOffset.Now:HH:mm:ss}";
    }

    private async void ToggleChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not ProcessMonitoringState state)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(state.CharacterName) || state.CharacterName == "(unbound)")
        {
            return;
        }

        await App.Services.UpdateProcessTogglesAsync(state.CharacterName, state.AlertEnabled, state.ReloginEnabled);
    }
}
