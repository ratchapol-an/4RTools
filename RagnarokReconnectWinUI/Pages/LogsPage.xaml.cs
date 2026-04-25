using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Controls;
using RagnarokAutomation.Core;

namespace RagnarokReconnectWinUI.Pages;

public sealed partial class LogsPage : Page
{
    public ObservableCollection<MonitoringIncident> Incidents => App.Services.RecentIncidents;

    public LogsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshDiagnosticDump();
    }

    private static string BuildDiagnosticDump()
    {
        StringBuilder sb = new();
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("== Process Diagnostics ==");
        int processCount = 0;
        foreach (ProcessMonitoringState state in App.Services.ProcessStates.OrderBy(s => s.ProcessId))
        {
            processCount++;
            sb.AppendLine($"pid={state.ProcessId} process={state.ProcessName} char={state.CharacterName} user={state.Username} status={state.State} cause={state.RootCause} ports={state.RemotePorts}");
            if (!string.IsNullOrWhiteSpace(state.Diagnostic))
            {
                sb.AppendLine($"diag={state.Diagnostic}");
            }
            sb.AppendLine("---");
        }
        if (processCount == 0)
        {
            sb.AppendLine("(no process diagnostics yet - run monitoring poll)");
        }

        sb.AppendLine("== Recent Incidents ==");
        List<MonitoringIncident> recent = App.Services.RecentIncidents
            .GroupBy(i => new
            {
                i.Timestamp,
                i.ProcessId,
                i.CharacterName,
                i.State,
                i.RootCause,
                i.DeliveredToDiscord,
                i.Evidence
            })
            .Select(g => g.First())
            .Take(50)
            .ToList();
        int incidentCount = 0;
        foreach (MonitoringIncident incident in recent)
        {
            incidentCount++;
            string recovered = incident.RecoveredAt.HasValue ? $" recovered={incident.RecoveredAt.Value:yyyy-MM-dd HH:mm}" : string.Empty;
            sb.AppendLine($"time={incident.Timestamp:yyyy-MM-dd HH:mm} pid={incident.ProcessId} character={incident.CharacterName} status={incident.State} cause={incident.RootCause} sent={incident.DeliveredToDiscord}{recovered}");
            sb.AppendLine($"info={incident.Evidence}");
            sb.AppendLine("---");
        }
        if (incidentCount == 0)
        {
            sb.AppendLine("(no incidents yet)");
        }

        sb.AppendLine("== UI/Test Send Logs ==");
        IReadOnlyList<string> logLines = App.Services.ReadRecentLogLines(200);
        int uiLogCount = 0;
        Regex primaryLogLine = new(@"^\d{4}-\d{2}-\d{2}\s", RegexOptions.Compiled);
        string currentDateGroup = string.Empty;
        foreach (string uiLog in logLines)
        {
            if (!primaryLogLine.IsMatch(uiLog))
            {
                continue;
            }

            string dateGroup = uiLog.Length >= 10 ? uiLog[..10] : "unknown-date";
            if (!string.Equals(dateGroup, currentDateGroup, StringComparison.Ordinal))
            {
                currentDateGroup = dateGroup;
                sb.AppendLine($"-- {currentDateGroup} --");
            }

            uiLogCount++;
            sb.AppendLine(uiLog);
        }
        if (uiLogCount == 0)
        {
            sb.AppendLine("(no ui/test-send logs yet)");
        }

        return sb.ToString();
    }

    private void RefreshDiagnosticDump()
    {
        DiagnosticDumpTextBox.Text = BuildDiagnosticDump();
    }

    private void RefreshDump_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        RefreshDiagnosticDump();
    }

    private void CopyDump_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        DataPackage dataPackage = new();
        dataPackage.SetText(DiagnosticDumpTextBox.Text ?? string.Empty);
        Clipboard.SetContent(dataPackage);
    }
}
