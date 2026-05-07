using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PartyWingBuffTools.Core.Models;

namespace PartyWingBuffTools;

public sealed partial class MainWindow
{
    private readonly List<ScheduledActionDefinition> _scheduledActions = new();
    private readonly List<ScheduledJobDefinition> _scheduledJobs = new();
    private readonly Dictionary<string, DateTimeOffset> _nextRunByRunId = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scheduledExecutionLock = new(1, 1);
    private readonly SemaphoreSlim _dispatchExecutionGate = new(1, 1);
    private readonly Dictionary<string, TextBlock> _scheduledRunIndicatorByRunId = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _scheduledJobsCts;
    private Task? _scheduledJobsTask;
    private bool _scheduledJobsEnabled = false;
    private TextBlock _scheduledJobsStatusText = null!;
    private Button _scheduledJobsStartButton = null!;
    private Button _scheduledJobsStopButton = null!;
    private StackPanel _scheduledActionsHost = null!;
    private StackPanel _scheduledJobsHost = null!;
    private Grid _scheduledActionsPanel = null!;
    private Grid _scheduledJobsPanelBody = null!;

    private FrameworkElement BuildScheduledJobsPanel()
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock { Text = "⏰ Scheduled Jobs", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 16 };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = "Define action templates, then create jobs that run the selected action at one or more daily times.",
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(subtitle, 1);
        root.Children.Add(subtitle);

        var scheduledControlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _scheduledJobsStartButton = new Button { Content = "▶ Start Jobs" };
        _scheduledJobsStartButton.Click += (_, _) => SetScheduledJobsEnabled(true);
        _scheduledJobsStopButton = new Button { Content = "■ Stop Jobs" };
        _scheduledJobsStopButton.Click += (_, _) => SetScheduledJobsEnabled(false);
        _scheduledJobsStatusText = new TextBlock { Text = "Status: Idle", VerticalAlignment = VerticalAlignment.Center };
        scheduledControlRow.Children.Add(new TextBlock { Text = "Schedule Jobs:", VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        scheduledControlRow.Children.Add(_scheduledJobsStartButton);
        scheduledControlRow.Children.Add(_scheduledJobsStopButton);
        scheduledControlRow.Children.Add(_scheduledJobsStatusText);
        Grid.SetRow(scheduledControlRow, 2);
        root.Children.Add(scheduledControlRow);

        var tabButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var actionsTabButton = new Button { Content = "Actions" };
        var jobsTabButton = new Button { Content = "Schedule Jobs" };
        tabButtons.Children.Add(actionsTabButton);
        tabButtons.Children.Add(jobsTabButton);
        Grid.SetRow(tabButtons, 3);
        root.Children.Add(tabButtons);

        var panelHost = new Grid();
        Grid.SetRow(panelHost, 4);
        root.Children.Add(panelHost);

        _scheduledActionsPanel = new Grid { RowSpacing = 10 };
        _scheduledActionsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _scheduledActionsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _scheduledJobsPanelBody = new Grid { RowSpacing = 10, Visibility = Visibility.Collapsed };
        _scheduledJobsPanelBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _scheduledJobsPanelBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panelHost.Children.Add(_scheduledActionsPanel);
        panelHost.Children.Add(_scheduledJobsPanelBody);

        actionsTabButton.Click += (_, _) =>
        {
            _scheduledActionsPanel.Visibility = Visibility.Visible;
            _scheduledJobsPanelBody.Visibility = Visibility.Collapsed;
        };
        jobsTabButton.Click += (_, _) =>
        {
            _scheduledActionsPanel.Visibility = Visibility.Collapsed;
            _scheduledJobsPanelBody.Visibility = Visibility.Visible;
        };

        var actionsHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionsHeader.Children.Add(new TextBlock { Text = "Actions", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        var addActionButton = new Button { Content = "+ Add Action" };
        addActionButton.Click += (_, _) =>
        {
            _scheduledActions.Add(new ScheduledActionDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"Action{_scheduledActions.Count + 1}",
                Steps = new List<ScheduledActionStep> { new() { StepType = "KEY", KeyText = string.Empty, DelayText = "250" } },
            });
            SaveScheduledJobsToDisk();
            RenderScheduledActions();
            RenderScheduledJobs();
        };
        actionsHeader.Children.Add(addActionButton);
        Grid.SetRow(actionsHeader, 0);
        _scheduledActionsPanel.Children.Add(actionsHeader);

        _scheduledActionsHost = new StackPanel { Spacing = 8 };
        var actionsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _scheduledActionsHost,
        };
        Grid.SetRow(actionsScroll, 1);
        _scheduledActionsPanel.Children.Add(actionsScroll);

        var jobsHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        jobsHeader.Children.Add(new TextBlock { Text = "Jobs", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        var addJobButton = new Button { Content = "+ Add Job" };
        addJobButton.Click += (_, _) =>
        {
            _scheduledJobs.Add(new ScheduledJobDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"Job{_scheduledJobs.Count + 1}",
                ActionId = _scheduledActions.FirstOrDefault()?.Id ?? string.Empty,
                Runs = new List<ScheduledRunTimeDefinition> { CreateDefaultRunTime() },
            });
            SaveScheduledJobsToDisk();
            RecomputeNextScheduledRuns();
            RenderScheduledJobs();
        };
        jobsHeader.Children.Add(addJobButton);
        Grid.SetRow(jobsHeader, 0);
        _scheduledJobsPanelBody.Children.Add(jobsHeader);

        _scheduledJobsHost = new StackPanel { Spacing = 10 };
        var jobsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _scheduledJobsHost,
        };
        Grid.SetRow(jobsScroll, 1);
        _scheduledJobsPanelBody.Children.Add(jobsScroll);

        return root;
    }

    private static ScheduledRunTimeDefinition CreateDefaultRunTime()
    {
        return new ScheduledRunTimeDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            TimeText = "00:00",
            IsEnabled = false,
        };
    }

    private void InitializeScheduledJobs()
    {
        try
        {
            LoadScheduledJobsFromDisk();
            RenderScheduledActions();
            RenderScheduledJobs();
            SetScheduledJobsEnabled(_scheduledJobsEnabled);
        }
        catch (Exception ex)
        {
            AppendLog($"Scheduled Jobs disabled due to initialization error: {ex.Message}");
        }
    }

    private void SetScheduledJobsEnabled(bool enabled)
    {
        _scheduledJobsEnabled = enabled;
        if (_scheduledJobsEnabled)
        {
            RecomputeNextScheduledRuns();
            StartScheduledJobsLoop();
            int enabledRuns = _scheduledJobs
                .SelectMany(j => j.Runs)
                .Count(r => r.IsEnabled);
            if (enabledRuns == 0)
            {
                AppendLog("Scheduled jobs started, but no enabled run times were found.");
            }
            else
            {
                AppendLog($"Scheduled jobs started with {enabledRuns} enabled run time(s).");
                foreach (ScheduledJobDefinition job in _scheduledJobs)
                {
                    foreach (ScheduledRunTimeDefinition run in job.Runs.Where(r => r.IsEnabled))
                    {
                        if (_nextRunByRunId.TryGetValue(run.Id, out DateTimeOffset next))
                        {
                            string dayLabel = next.Date == DateTimeOffset.Now.Date
                                ? "today"
                                : next.Date == DateTimeOffset.Now.Date.AddDays(1)
                                    ? "tomorrow"
                                    : next.ToString("yyyy-MM-dd");
                            AppendLog($"Next run: {job.Name} at {next:yyyy-MM-dd HH:mm:ss} ({dayLabel})");
                        }
                    }
                }
            }
        }
        else
        {
            StopScheduledJobsLoop();
            AppendLog("Scheduled jobs stopped.");
        }

        if (_scheduledJobsStartButton is not null && _scheduledJobsStopButton is not null && _scheduledJobsStatusText is not null)
        {
            _scheduledJobsStartButton.IsEnabled = !_scheduledJobsEnabled;
            _scheduledJobsStopButton.IsEnabled = _scheduledJobsEnabled;
            _scheduledJobsStatusText.Text = _scheduledJobsEnabled ? "Status: Running" : "Status: Idle";
        }
    }

    private void StartScheduledJobsLoop()
    {
        if (_scheduledJobsTask is not null && !_scheduledJobsTask.IsCompleted)
        {
            return;
        }

        _scheduledJobsCts = new CancellationTokenSource();
        _scheduledJobsTask = Task.Run(() => RunScheduledJobsLoopAsync(_scheduledJobsCts.Token));
    }

    private void StopScheduledJobsLoop()
    {
        try
        {
            _scheduledJobsCts?.Cancel();
            _scheduledJobsTask?.Wait(1000);
            _scheduledJobsTask = null;
            _scheduledJobsCts = null;
        }
        catch
        {
            // shutdown no-op
        }
    }

    private async Task RunScheduledJobsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<(ScheduledJobDefinition job, ScheduledRunTimeDefinition run)> due = new();
            DateTimeOffset now = DateTimeOffset.Now;

            lock (_nextRunByRunId)
            {
                foreach (ScheduledJobDefinition job in _scheduledJobs)
                {
                    foreach (ScheduledRunTimeDefinition run in job.Runs)
                    {
                        if (!run.IsEnabled)
                        {
                            continue;
                        }

                        if (_nextRunByRunId.TryGetValue(run.Id, out DateTimeOffset nextRun) && now >= nextRun)
                        {
                            due.Add((job, run));
                            _nextRunByRunId[run.Id] = nextRun.AddDays(1);
                        }
                    }
                }
            }

            foreach ((ScheduledJobDefinition job, ScheduledRunTimeDefinition run) in due)
            {
                _ = Task.Run(() => ExecuteScheduledRunAsync(job, run, ct), ct);
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ExecuteScheduledRunAsync(ScheduledJobDefinition job, ScheduledRunTimeDefinition run, CancellationToken ct)
    {
        string execId = Guid.NewGuid().ToString("N")[..8];
        await SetScheduledRunExecutingVisualAsync(run.Id, isExecuting: true);
        await _scheduledExecutionLock.WaitAsync(ct);
        try
        {
            ScheduledActionDefinition? action = _scheduledActions.FirstOrDefault(a => a.Id == job.ActionId);
            if (action is null)
            {
                await AppendScheduledLogAsync($"[{execId}] Scheduled job skipped: action not found for job '{job.Name}'.");
                return;
            }

            await AppendScheduledLogAsync($"[{execId}] Scheduled job started: {job.Name} ({action.Name}) at {run.TimeText}. Steps={action.Steps.Count}");

            if (job.StopPartyTriggerAtStart)
            {
                await RequestStopPartyTriggerAsync();
            }

            // Validate selected target process IDs against currently detected clients.
            var currentlyDetected = new HashSet<int>();
            await RunOnUiThreadAsync(() =>
            {
                currentlyDetected.Clear();
                foreach (int pid in _availableProcesses.Select(p => p.ProcessId))
                {
                    currentlyDetected.Add(pid);
                }
            });

            List<int> invalidPids = job.TargetProcessIds
                .Distinct()
                .Where(pid => !currentlyDetected.Contains(pid))
                .ToList();
            foreach (int invalidPid in invalidPids)
            {
                await AppendScheduledLogAsync($"[{execId}] Scheduled job skipped stale process id: {invalidPid} (reselect process in schedule job).");
            }

            List<int> validPids = job.TargetProcessIds
                .Distinct()
                .Where(pid => currentlyDetected.Contains(pid))
                .ToList();
            if (validPids.Count == 0)
            {
                await AppendScheduledLogAsync($"[{execId}] Scheduled job '{job.Name}' has no valid target process IDs at execution time.");
                return;
            }
            await AppendScheduledLogAsync($"[{execId}] Scheduled dispatch targets: {string.Join(", ", validPids)}");

            await _dispatchExecutionGate.WaitAsync(ct);
            try
            {
                List<Task> processTasks = validPids
                    .Select(pid => RunScheduledActionForProcessAsync(pid, action, execId, ct))
                    .ToList();
                await Task.WhenAll(processTasks);
            }
            finally
            {
                _dispatchExecutionGate.Release();
            }

            if (job.StartPartyTriggerAtEnd)
            {
                await RunOnUiThreadAsync(() =>
                {
                    if (_runCts is null)
                    {
                        _ = StartSchedulerAsync();
                    }
                });
            }

            await AppendScheduledLogAsync($"[{execId}] Scheduled job completed: {job.Name} ({action.Name}).");
        }
        catch (OperationCanceledException)
        {
            // ignore shutdown cancel
        }
        catch (Exception ex)
        {
            await AppendScheduledLogAsync($"[{execId}] Scheduled job failed: {ex.Message}");
        }
        finally
        {
            _scheduledExecutionLock.Release();
            await SetScheduledRunExecutingVisualAsync(run.Id, isExecuting: false);
        }
    }

    private Task SetScheduledRunExecutingVisualAsync(string runId, bool isExecuting)
    {
        return RunOnUiThreadAsync(() =>
        {
            if (_scheduledRunIndicatorByRunId.TryGetValue(runId, out TextBlock? indicator))
            {
                indicator.Visibility = isExecuting ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    private async Task RequestStopPartyTriggerAsync()
    {
        await RunOnUiThreadAsync(() => _runCts?.Cancel());
        while (true)
        {
            bool isStopped = false;
            await RunOnUiThreadAsync(() => isStopped = _runCts is null);
            if (isStopped)
            {
                break;
            }

            await Task.Delay(120);
        }
    }

    private async Task RunScheduledActionForProcessAsync(int processId, ScheduledActionDefinition action, string execId, CancellationToken ct)
    {
        for (int i = 0; i < action.Steps.Count; i++)
        {
            ScheduledActionStep step = action.Steps[i];
            string stepType = NormalizeStepType(step.StepType);
            bool sent;
            string logAction;
            if (stepType == "MOUSE")
            {
                if (!TryResolveMousePoint(step.MousePointId, out double x, out double y))
                {
                    await AppendScheduledLogAsync($"[{execId}] Scheduled mouse point missing -> {processId} ({action.Name}) step {i + 1}/{action.Steps.Count}");
                    continue;
                }

                sent = _keyDispatchService.SendMouseClickNormalized(processId, x, y);
                logAction = $"mouse({x:0.###},{y:0.###})";
            }
            else
            {
                string key = (step.KeyText ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                sent = _keyDispatchService.SendKey(processId, key);
                logAction = $"key {key}";
            }

            await AppendScheduledLogAsync(sent
                ? $"[{execId}] Scheduled {logAction} -> {processId} ({action.Name}) step {i + 1}/{action.Steps.Count}"
                : $"[{execId}] Scheduled {logAction} failed -> {processId} ({action.Name}) step {i + 1}/{action.Steps.Count}");

            int delay = 250;
            if (int.TryParse(step.DelayText, out int parsed))
            {
                delay = Math.Max(0, parsed);
            }

            if (delay > 0)
            {
                await Task.Delay(delay, ct);
            }
        }
    }

    private Task AppendScheduledLogAsync(string message)
    {
        return RunOnUiThreadAsync(() => AppendLog(message));
    }

    private void RefreshScheduledJobsProcessOptions()
    {
        if (_scheduledJobsHost is not null)
        {
            RenderScheduledJobs();
        }
    }

    private void RenderScheduledActions()
    {
        _scheduledActionsHost.Children.Clear();
        foreach (ScheduledActionDefinition action in _scheduledActions.ToList())
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(4),
                BorderBrush = GetOptionalResourceBrush("CardStrokeColorDefaultBrush"),
            };
            var host = new StackPanel { Spacing = 6 };
            border.Child = host;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var nameBox = new TextBox { Text = action.Name, PlaceholderText = "Action name", Width = 250 };
            nameBox.TextChanged += (_, _) =>
            {
                action.Name = nameBox.Text;
                SaveScheduledJobsToDisk();
                RenderScheduledJobs();
            };
            var addStepButton = new Button { Content = "+ Step" };
            addStepButton.Click += (_, _) =>
            {
                action.Steps.Add(new ScheduledActionStep { StepType = "KEY", KeyText = string.Empty, DelayText = "250" });
                SaveScheduledJobsToDisk();
                RenderScheduledActions();
            };
            var duplicateButton = new Button { Content = "Duplicate" };
            duplicateButton.Click += (_, _) =>
            {
                _scheduledActions.Add(new ScheduledActionDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = string.IsNullOrWhiteSpace(action.Name) ? $"Action{_scheduledActions.Count + 1}" : $"{action.Name} Copy",
                    Steps = action.Steps.Select(s => new ScheduledActionStep { StepType = NormalizeStepType(s.StepType), KeyText = s.KeyText, MousePointId = s.MousePointId, DelayText = s.DelayText }).ToList(),
                });
                SaveScheduledJobsToDisk();
                RenderScheduledActions();
                RenderScheduledJobs();
            };
            var removeButton = new Button { Content = "Delete" };
            removeButton.Click += (_, _) =>
            {
                _scheduledActions.Remove(action);
                foreach (ScheduledJobDefinition job in _scheduledJobs.Where(j => j.ActionId == action.Id))
                {
                    job.ActionId = _scheduledActions.FirstOrDefault()?.Id ?? string.Empty;
                }

                SaveScheduledJobsToDisk();
                RecomputeNextScheduledRuns();
                RenderScheduledActions();
                RenderScheduledJobs();
            };
            header.Children.Add(new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(nameBox);
            header.Children.Add(addStepButton);
            header.Children.Add(duplicateButton);
            header.Children.Add(removeButton);
            host.Children.Add(header);

            if (action.Steps.Count == 0)
            {
                action.Steps.Add(new ScheduledActionStep { StepType = "KEY", KeyText = string.Empty, DelayText = "250" });
            }

            foreach (ScheduledActionStep step in action.Steps.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var stepTypeCombo = new ComboBox { Width = 90, ItemsSource = new[] { "KEY", "MOUSE" }, SelectedItem = NormalizeStepType(step.StepType) };
                var keyBox = new TextBox { Text = step.KeyText, PlaceholderText = "Key", Width = 110, IsReadOnly = true };
                var pointCombo = new ComboBox { Width = 170, DisplayMemberPath = "Name", SelectedValuePath = "Id", ItemsSource = _mousePoints, SelectedValue = step.MousePointId };
                void applyStepUiState()
                {
                    bool isKey = NormalizeStepType(step.StepType) == "KEY";
                    keyBox.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
                    pointCombo.Visibility = isKey ? Visibility.Collapsed : Visibility.Visible;
                }
                stepTypeCombo.SelectionChanged += (_, _) =>
                {
                    step.StepType = NormalizeStepType(stepTypeCombo.SelectedItem?.ToString());
                    SaveScheduledJobsToDisk();
                    applyStepUiState();
                };
                pointCombo.SelectionChanged += (_, _) =>
                {
                    step.MousePointId = pointCombo.SelectedValue?.ToString() ?? string.Empty;
                    SaveScheduledJobsToDisk();
                };
                keyBox.KeyDown += (_, e) =>
                {
                    if (NormalizeStepType(step.StepType) != "KEY")
                    {
                        e.Handled = true;
                        return;
                    }

                    string token = ConvertVirtualKeyToToken(e.Key);
                    if (token == "Esc")
                    {
                        e.Handled = true;
                        return;
                    }

                    if (token == "Back")
                    {
                        keyBox.Text = string.Empty;
                        step.KeyText = string.Empty;
                        SaveScheduledJobsToDisk();
                        e.Handled = true;
                        return;
                    }

                    keyBox.Text = token;
                    step.KeyText = token;
                    SaveScheduledJobsToDisk();
                    e.Handled = true;
                };
                var delayBox = new TextBox { Text = step.DelayText, PlaceholderText = "Delay ms", Width = 100 };
                delayBox.TextChanged += (_, _) =>
                {
                    step.DelayText = delayBox.Text;
                    SaveScheduledJobsToDisk();
                };
                var removeStepButton = new Button { Content = "-", MinWidth = 32 };
                removeStepButton.Click += (_, _) =>
                {
                    action.Steps.Remove(step);
                    SaveScheduledJobsToDisk();
                    RenderScheduledActions();
                };
                row.Children.Add(new TextBlock { Text = "Step:", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(stepTypeCombo);
                row.Children.Add(keyBox);
                row.Children.Add(pointCombo);
                row.Children.Add(delayBox);
                row.Children.Add(removeStepButton);
                applyStepUiState();
                host.Children.Add(row);
            }

            _scheduledActionsHost.Children.Add(border);
        }
    }

    private void RenderScheduledJobs()
    {
        _scheduledJobsHost.Children.Clear();
        _scheduledRunIndicatorByRunId.Clear();
        foreach (ScheduledJobDefinition job in _scheduledJobs.ToList())
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(4),
                BorderBrush = GetOptionalResourceBrush("CardStrokeColorDefaultBrush"),
            };
            var host = new StackPanel { Spacing = 8 };
            border.Child = host;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var jobNameBox = new TextBox { Text = job.Name, PlaceholderText = "Job name", Width = 200 };
            jobNameBox.TextChanged += (_, _) =>
            {
                job.Name = jobNameBox.Text;
                SaveScheduledJobsToDisk();
            };
            var actionCombo = new ComboBox { Width = 220, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
            actionCombo.ItemsSource = _scheduledActions;
            actionCombo.SelectedValue = job.ActionId;
            actionCombo.SelectionChanged += (_, _) =>
            {
                job.ActionId = actionCombo.SelectedValue?.ToString() ?? string.Empty;
                SaveScheduledJobsToDisk();
            };
            var addRunButton = new Button { Content = "+ Time" };
            addRunButton.Click += (_, _) =>
            {
                job.Runs.Add(CreateDefaultRunTime());
                SaveScheduledJobsToDisk();
                RecomputeNextScheduledRuns();
                RenderScheduledJobs();
            };
            var duplicateJobButton = new Button { Content = "Duplicate Job" };
            duplicateJobButton.Click += (_, _) =>
            {
                _scheduledJobs.Add(new ScheduledJobDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = string.IsNullOrWhiteSpace(job.Name) ? $"Job{_scheduledJobs.Count + 1}" : $"{job.Name} Copy",
                    ActionId = job.ActionId,
                    TargetProcessIds = job.TargetProcessIds.Distinct().ToList(),
                    StopPartyTriggerAtStart = job.StopPartyTriggerAtStart,
                    StartPartyTriggerAtEnd = job.StartPartyTriggerAtEnd,
                    Runs = job.Runs.Select(r => new ScheduledRunTimeDefinition
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        TimeText = r.TimeText,
                        IsEnabled = r.IsEnabled,
                    }).ToList(),
                });
                SaveScheduledJobsToDisk();
                RecomputeNextScheduledRuns();
                RenderScheduledJobs();
            };
            var removeJobButton = new Button { Content = "Delete Job" };
            removeJobButton.Click += (_, _) =>
            {
                _scheduledJobs.Remove(job);
                SaveScheduledJobsToDisk();
                RecomputeNextScheduledRuns();
                RenderScheduledJobs();
            };
            header.Children.Add(new TextBlock { Text = "Job:", VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(jobNameBox);
            header.Children.Add(new TextBlock { Text = "Action:", VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(actionCombo);
            header.Children.Add(addRunButton);
            header.Children.Add(duplicateJobButton);
            header.Children.Add(removeJobButton);
            host.Children.Add(header);

            host.Children.Add(new TextBlock { Text = "Target Processes:", Opacity = 0.9 });
            var processChecks = new StackPanel { Spacing = 4 };
            foreach (RagnarokProcessInfo process in _availableProcesses)
            {
                var processCheck = new CheckBox
                {
                    Content = $"{process.DisplayName} ({process.ProcessId})",
                    IsChecked = job.TargetProcessIds.Contains(process.ProcessId),
                };
                processCheck.Checked += (_, _) =>
                {
                    if (!job.TargetProcessIds.Contains(process.ProcessId))
                    {
                        job.TargetProcessIds.Add(process.ProcessId);
                    }
                    SaveScheduledJobsToDisk();
                };
                processCheck.Unchecked += (_, _) =>
                {
                    job.TargetProcessIds.Remove(process.ProcessId);
                    SaveScheduledJobsToDisk();
                };
                processChecks.Children.Add(processCheck);
            }
            host.Children.Add(processChecks);

            var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var stopCheck = new CheckBox { Content = "Stop Party Trigger at start", IsChecked = job.StopPartyTriggerAtStart };
            stopCheck.Checked += (_, _) => { job.StopPartyTriggerAtStart = true; SaveScheduledJobsToDisk(); };
            stopCheck.Unchecked += (_, _) => { job.StopPartyTriggerAtStart = false; SaveScheduledJobsToDisk(); };
            var startCheck = new CheckBox { Content = "Start Party Trigger at end", IsChecked = job.StartPartyTriggerAtEnd };
            startCheck.Checked += (_, _) => { job.StartPartyTriggerAtEnd = true; SaveScheduledJobsToDisk(); };
            startCheck.Unchecked += (_, _) => { job.StartPartyTriggerAtEnd = false; SaveScheduledJobsToDisk(); };
            flags.Children.Add(stopCheck);
            flags.Children.Add(startCheck);
            host.Children.Add(flags);

            foreach (ScheduledRunTimeDefinition run in job.Runs.ToList())
            {
                var runBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = GetOptionalResourceBrush("CardStrokeColorSecondaryBrush"),
                };
                var runHost = new StackPanel { Spacing = 6 };
                runBorder.Child = runHost;

                var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var runIndicator = new TextBlock
                {
                    Text = "●",
                    Visibility = Visibility.Collapsed,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = _executingForegroundBrush,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                };
                var enabledCheck = new CheckBox { Content = "Enabled", IsChecked = run.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
                enabledCheck.Checked += (_, _) =>
                {
                    run.IsEnabled = true;
                    SaveScheduledJobsToDisk();
                    RecomputeNextScheduledRuns();
                };
                enabledCheck.Unchecked += (_, _) =>
                {
                    run.IsEnabled = false;
                    SaveScheduledJobsToDisk();
                    RecomputeNextScheduledRuns();
                };
                if (!TryParseDailyTime(run.TimeText, out TimeSpan initialTime))
                {
                    initialTime = TimeSpan.Zero;
                }

                List<string> hourOptions = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
                List<string> minuteOptions = Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();
                var hourCombo = new ComboBox { Width = 80, ItemsSource = hourOptions, SelectedItem = initialTime.Hours.ToString("D2") };
                var minuteCombo = new ComboBox { Width = 80, ItemsSource = minuteOptions, SelectedItem = initialTime.Minutes.ToString("D2") };
                void updateTimeFromCombo()
                {
                    string hourText = hourCombo.SelectedItem?.ToString() ?? "00";
                    string minuteText = minuteCombo.SelectedItem?.ToString() ?? "00";
                    run.TimeText = $"{hourText}:{minuteText}";
                    SaveScheduledJobsToDisk();
                    RecomputeNextScheduledRuns();
                }
                hourCombo.SelectionChanged += (_, _) => updateTimeFromCombo();
                minuteCombo.SelectionChanged += (_, _) => updateTimeFromCombo();

                var duplicateRunButton = new Button { Content = "Duplicate Time" };
                duplicateRunButton.Click += (_, _) =>
                {
                    job.Runs.Add(new ScheduledRunTimeDefinition
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        TimeText = run.TimeText,
                        IsEnabled = run.IsEnabled,
                    });
                    SaveScheduledJobsToDisk();
                    RecomputeNextScheduledRuns();
                    RenderScheduledJobs();
                };
                var runNowButton = new Button { Content = "Run Now" };
                runNowButton.Click += (_, _) =>
                {
                    AppendLog($"Run Now clicked: {job.Name} at {run.TimeText}");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ExecuteScheduledRunAsync(job, run, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            await AppendScheduledLogAsync($"Run Now failed: {ex.Message}");
                        }
                    });
                };
                var removeRunButton = new Button { Content = "Delete Time" };
                removeRunButton.Click += (_, _) =>
                {
                    job.Runs.Remove(run);
                    SaveScheduledJobsToDisk();
                    RecomputeNextScheduledRuns();
                    RenderScheduledJobs();
                };
                top.Children.Add(runIndicator);
                top.Children.Add(enabledCheck);
                top.Children.Add(new TextBlock { Text = "Time:", VerticalAlignment = VerticalAlignment.Center });
                top.Children.Add(hourCombo);
                top.Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center });
                top.Children.Add(minuteCombo);
                top.Children.Add(runNowButton);
                top.Children.Add(duplicateRunButton);
                top.Children.Add(removeRunButton);
                runHost.Children.Add(top);
                _scheduledRunIndicatorByRunId[run.Id] = runIndicator;

                host.Children.Add(runBorder);
            }

            _scheduledJobsHost.Children.Add(border);
        }
    }

    private void LoadScheduledJobsFromDisk()
    {
        _scheduledActions.Clear();
        _scheduledJobs.Clear();
        try
        {
            Directory.CreateDirectory(GetActionsDirectoryPath());
            string actionsPath = GetActionsFilePath();
            string schedulesPath = GetSchedulesFilePath();
            if (File.Exists(actionsPath))
            {
                string raw = File.ReadAllText(actionsPath);
                List<ScheduledActionDefinition>? loaded = JsonSerializer.Deserialize<List<ScheduledActionDefinition>>(raw);
                _scheduledActions.AddRange(loaded ?? new List<ScheduledActionDefinition>());
            }

            if (File.Exists(schedulesPath))
            {
                string raw = File.ReadAllText(schedulesPath);
                List<ScheduledJobDefinition>? loadedJobs = JsonSerializer.Deserialize<List<ScheduledJobDefinition>>(raw);
                if (loadedJobs is not null && loadedJobs.Count > 0 && loadedJobs.Any(j => j.Runs is not null))
                {
                    _scheduledJobs.Clear();
                    _scheduledJobs.AddRange(loadedJobs);
                }
                else
                {
                    List<LegacyScheduledRunDefinition>? legacyRuns = JsonSerializer.Deserialize<List<LegacyScheduledRunDefinition>>(raw);
                    foreach (IGrouping<string, LegacyScheduledRunDefinition> group in (legacyRuns ?? new List<LegacyScheduledRunDefinition>())
                                 .Where(r => !string.IsNullOrWhiteSpace(r.ActionId))
                                 .GroupBy(r => r.ActionId ?? string.Empty))
                    {
                        string actionId = group.Key;
                        string actionName = _scheduledActions.FirstOrDefault(a => a.Id == actionId)?.Name ?? "Action";
                        _scheduledJobs.Add(new ScheduledJobDefinition
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = $"{actionName} Job",
                            ActionId = actionId,
                            TargetProcessIds = group.SelectMany(r => r.TargetProcessIds ?? new List<int>()).Distinct().ToList(),
                            StopPartyTriggerAtStart = group.Any(r => r.StopPartyTriggerAtStart),
                            StartPartyTriggerAtEnd = group.Any(r => r.StartPartyTriggerAtEnd),
                            Runs = group.Select(r => new ScheduledRunTimeDefinition
                            {
                                Id = string.IsNullOrWhiteSpace(r.Id) ? Guid.NewGuid().ToString("N") : r.Id!,
                                TimeText = string.IsNullOrWhiteSpace(r.TimeText) ? "00:00" : r.TimeText!,
                                IsEnabled = r.IsEnabled,
                            }).ToList(),
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to load scheduled jobs: {ex.Message}");
            TryBackupInvalidScheduledFiles();
            _scheduledActions.Clear();
            _scheduledJobs.Clear();
        }

        foreach (ScheduledJobDefinition job in _scheduledJobs)
        {
            job.Id = string.IsNullOrWhiteSpace(job.Id) ? Guid.NewGuid().ToString("N") : job.Id;
            job.TargetProcessIds ??= new List<int>();
            job.Runs ??= new List<ScheduledRunTimeDefinition>();
            foreach (ScheduledRunTimeDefinition run in job.Runs)
            {
                run.Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id;
                if (string.IsNullOrWhiteSpace(run.TimeText))
                {
                    run.TimeText = "00:00";
                }
            }
        }

        foreach (ScheduledActionDefinition action in _scheduledActions)
        {
            action.Id = string.IsNullOrWhiteSpace(action.Id) ? Guid.NewGuid().ToString("N") : action.Id;
            action.Steps ??= new List<ScheduledActionStep>();
            foreach (ScheduledActionStep step in action.Steps)
            {
                step.StepType = NormalizeStepType(step.StepType);
                step.KeyText ??= string.Empty;
                step.MousePointId ??= string.Empty;
                step.DelayText = string.IsNullOrWhiteSpace(step.DelayText) ? "250" : step.DelayText;
            }
        }

        RecomputeNextScheduledRuns();
    }

    private void TryBackupInvalidScheduledFiles()
    {
        try
        {
            string actionsPath = GetActionsFilePath();
            string schedulesPath = GetSchedulesFilePath();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            if (File.Exists(actionsPath))
            {
                File.Copy(actionsPath, actionsPath + $".invalid-{stamp}.bak", overwrite: true);
            }

            if (File.Exists(schedulesPath))
            {
                File.Copy(schedulesPath, schedulesPath + $".invalid-{stamp}.bak", overwrite: true);
            }
        }
        catch
        {
            // best effort only
        }
    }

    private void SaveScheduledJobsToDisk()
    {
        try
        {
            Directory.CreateDirectory(GetActionsDirectoryPath());
            string actionJson = JsonSerializer.Serialize(_scheduledActions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetActionsFilePath(), actionJson);
            string jobsJson = JsonSerializer.Serialize(_scheduledJobs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetSchedulesFilePath(), jobsJson);
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to save scheduled jobs: {ex.Message}");
        }
    }

    private void RecomputeNextScheduledRuns()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        lock (_nextRunByRunId)
        {
            _nextRunByRunId.Clear();
            foreach (ScheduledJobDefinition job in _scheduledJobs)
            {
                foreach (ScheduledRunTimeDefinition run in job.Runs)
                {
                    if (!TryParseDailyTime(run.TimeText, out TimeSpan time))
                    {
                        continue;
                    }

                    DateTimeOffset candidate = new(
                        now.Year,
                        now.Month,
                        now.Day,
                        time.Hours,
                        time.Minutes,
                        0,
                        now.Offset);
                    if (candidate <= now)
                    {
                        candidate = candidate.AddDays(1);
                    }

                    _nextRunByRunId[run.Id] = candidate;
                }
            }
        }
    }

    private static bool TryParseDailyTime(string text, out TimeSpan time)
    {
        return TimeSpan.TryParseExact(text?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time);
    }

    private string GetActionsDirectoryPath()
    {
        return Path.Combine(GetAppDataRootPath(), "actions");
    }

    private string GetActionsFilePath()
    {
        return Path.Combine(GetActionsDirectoryPath(), "actions.json");
    }

    private string GetSchedulesFilePath()
    {
        return Path.Combine(GetActionsDirectoryPath(), "schedules.json");
    }

    private static Microsoft.UI.Xaml.Media.Brush GetOptionalResourceBrush(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out object? value) &&
            value is Microsoft.UI.Xaml.Media.Brush brush)
        {
            return brush;
        }

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private sealed class ScheduledActionDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public List<ScheduledActionStep> Steps { get; set; } = new();
    }

    private sealed class ScheduledActionStep
    {
        public string StepType { get; set; } = "KEY";
        public string KeyText { get; set; } = string.Empty;
        public string MousePointId { get; set; } = string.Empty;
        public string DelayText { get; set; } = "250";
    }

    private sealed class ScheduledJobDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string ActionId { get; set; } = string.Empty;
        public List<int> TargetProcessIds { get; set; } = new();
        public bool StopPartyTriggerAtStart { get; set; }
        public bool StartPartyTriggerAtEnd { get; set; }
        public List<ScheduledRunTimeDefinition> Runs { get; set; } = new();
    }

    private sealed class ScheduledRunTimeDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TimeText { get; set; } = "00:00";
        public bool IsEnabled { get; set; } = true;
    }

    private sealed class LegacyScheduledRunDefinition
    {
        public string? Id { get; set; }
        public string? TimeText { get; set; }
        public string? ActionId { get; set; }
        public List<int>? TargetProcessIds { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool StopPartyTriggerAtStart { get; set; }
        public bool StartPartyTriggerAtEnd { get; set; }
    }
}
