using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PartyWingBuffTools.Core.Models;
using PartyWingBuffTools.Core.Services;
using PartyWingBuffTools.Native;
using PartyWingBuffTools.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace PartyWingBuffTools;

public sealed partial class MainWindow : Window
{
    private const string DefaultExecutableName = "Ragexe";
    private const string DefaultHpAddressHex = "0146F28C";
    private const string DefaultNameAddressHex = "01471CD8";
    private const string DefaultProfileName = "Default";
    private const int MaxDisplayedLogLines = 50;
    private const int StartupWindowWidth = 1180;
    private const int StartupWindowHeight = 760;

    private readonly RagnarokProcessService _processService = new();
    private readonly KeyDispatchService _keyDispatchService = new();
    private readonly GlobalHotkeyService _globalHotkeyService = new();
    private readonly PartyBuffScheduler _scheduler = new();
    private readonly List<RagnarokProcessInfo> _availableProcesses = new();
    private readonly List<TriggerDefinition> _triggers = new();
    private readonly Queue<DispatchPlan> _triggerQueue = new();
    private readonly object _triggerQueueLock = new();
    private readonly List<SupportedServerEntry> _supportedServers = new();
    private readonly Dictionary<TriggerDefinition, IReadOnlyList<Control>> _triggerVisuals = new();
    private readonly Dictionary<TriggerDefinition, TextBlock> _triggerIndicatorByTrigger = new();
    private readonly Dictionary<string, IReadOnlyList<Control>> _stepVisualsByReason = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _stepIndicatorByReason = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MousePointDefinition> _mousePoints = new();
    private TriggerDefinition? _selectedTrigger;
    private CancellationTokenSource? _runCts;
    private int? _pendingArchbishopProcessId;
    private string _currentProfileName = DefaultProfileName;
    private readonly Queue<string> _recentLogLines = new();
    private readonly object _logFileLock = new();
    private readonly Queue<string> _pendingFileLogLines = new();
    private readonly object _pendingFileLogLock = new();
    private readonly SemaphoreSlim _logSignal = new(0);
    private readonly CancellationTokenSource _logWriterCts = new();
    private Task? _logWriterTask;
    private readonly string _appVersion;
    private string _toggleHotkey = "PGDN";
    private DateTimeOffset _ignoreGlobalHotkeyUntilUtc = DateTimeOffset.MinValue;
    private bool _scheduledJobsInitialized;

    private ComboBox _archbishopProcessComboBox = null!;
    private TextBlock _archbishopCharacterText = null!;
    private TextBox _logTextBox = null!;
    private TextBlock _statusText = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private TextBox _toggleHotkeyTextBox = null!;
    private CheckBox _audioCueCheckBox = null!;
    private StackPanel _triggerRowsHost = null!;
    private StackPanel _memberRowsHost = null!;
    private TextBlock _memberEditorTitle = null!;
    private Grid _runPanel = null!;
    private StackPanel _settingsPanel = null!;
    private StackPanel _logsPanel = null!;
    private StackPanel _profilesPanel = null!;
    private FrameworkElement _scheduledActionsPanel = null!;
    private FrameworkElement _scheduledJobsPanel = null!;
    private StackPanel _mousePointsPanel = null!;
    private StackPanel _mousePointsHost = null!;
    private ComboBox _mousePointTestProcessComboBox = null!;
    private TextBox _serverProcessNameTextBox = null!;
    private TextBox _hpAddressTextBox = null!;
    private TextBox _nameAddressTextBox = null!;
    private TextBox _mouseFocusSettleDelayTextBox = null!;
    private ComboBox _profileComboBox = null!;
    private TextBox _profileNameTextBox = null!;
    private Microsoft.UI.Xaml.Media.Brush _normalForegroundBrush = null!;
    private Microsoft.UI.Xaml.Media.Brush _executingForegroundBrush = null!;

    public MainWindow()
    {
        _appVersion = ReadAppVersion();
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        BuildUi();
        AppWindow.Resize(new SizeInt32(StartupWindowWidth, StartupWindowHeight));
        InitializeExecutionIndicatorBrushes();
        EnsureDefaultMousePoints();
        EnsureDefaultProfileExists();
        RefreshProfileList(selectProfileName: DefaultProfileName);
        LoadProfile(_currentProfileName, logResult: false);
        LoadSupportedProcessNames();
        RefreshProcesses();
        _logWriterTask = Task.Run(() => RunLogWriterAsync(_logWriterCts.Token));
        InitializeGlobalHotkey();
        Closed += (_, _) =>
        {
            SaveProfile(_currentProfileName, logResult: false);
            _globalHotkeyService.Dispose();
            if (_scheduledJobsInitialized)
            {
                StopScheduledJobsLoop();
            }
            StopLogWriter();
        };
    }

    private void BuildUi()
    {
        var rightRoot = new Grid { RowSpacing = 12, Margin = new Thickness(14, 10, 14, 14) };
        rightRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var topActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 2, 0, 10) };
        var refreshButton = new Button { Content = "Refresh Processes" };
        refreshButton.Click += (_, _) => { RefreshProcesses(); AppendLog("Processes refreshed."); };
        _profileComboBox = new ComboBox { MinWidth = 150 };
        _profileComboBox.SelectionChanged += (_, _) =>
        {
            if (_profileComboBox.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                _currentProfileName = selected;
                LoadProfile(_currentProfileName, logResult: true);
                LoadSupportedProcessNames();
                RefreshProcesses();
            }
        };
        var saveProfileButton = new Button { Content = "💾 Save" };
        saveProfileButton.Click += (_, _) => SaveProfile(_currentProfileName, logResult: true);
        topActions.Children.Add(refreshButton);
        topActions.Children.Add(new TextBlock { Text = "Profile:", VerticalAlignment = VerticalAlignment.Center });
        topActions.Children.Add(_profileComboBox);
        topActions.Children.Add(saveProfileButton);
        Grid.SetRow(topActions, 0);
        rightRoot.Children.Add(topActions);

        var contentHost = new Grid();
        Grid.SetRow(contentHost, 1);
        rightRoot.Children.Add(contentHost);

        _runPanel = BuildRunPanel();
        contentHost.Children.Add(_runPanel);

        _settingsPanel = BuildSettingsPanel();
        _settingsPanel.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_settingsPanel);

        _logsPanel = BuildLogsPanel();
        _logsPanel.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_logsPanel);

        _profilesPanel = BuildProfilesPanel();
        _profilesPanel.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_profilesPanel);

        _scheduledActionsPanel = BuildScheduledActionsPanel();
        _scheduledActionsPanel.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_scheduledActionsPanel);

        _scheduledJobsPanel = BuildScheduledJobsPanel();
        _scheduledJobsPanel.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_scheduledJobsPanel);

        _mousePointsPanel = BuildMousePointsPanel();
        _mousePointsPanel.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_mousePointsPanel);

        var navView = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = true,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            Content = rightRoot,
        };
        navView.MenuItems.Add(CreateNavItem("Party Trigger", "run", "\uE7FC", true));
        navView.MenuItems.Add(CreateNavItem("Address Settings", "settings", "\uE713"));
        navView.MenuItems.Add(CreateNavItem("Profiles", "profiles", "\uE716"));
        navView.MenuItems.Add(CreateNavItem("Logs", "logs", "\uE8A5"));
        navView.MenuItems.Add(CreateNavItem("Scheduled Actions", "scheduled-actions", "\uE8AB"));
        navView.MenuItems.Add(CreateNavItem("Scheduled Jobs", "scheduled", "\uE823"));
        navView.MenuItems.Add(CreateNavItem("Mouse Points", "mouse-points", "\uE7C9"));
        navView.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItemContainer?.Tag is string panelTag)
            {
                ShowPanel(panelTag);
            }
        };

        ContentHost.Children.Add(navView);

        AddTrigger("Buff60", "60", "F1", "300");
        AddTrigger("Buff180", "180", "F1", "300");
        SelectTrigger(_triggers.FirstOrDefault());
        ShowPanel("run");

        _archbishopProcessComboBox.SelectionChanged += (_, _) => UpdateArchbishopCharacterPreview();
    }

    private static NavigationViewItem CreateNavItem(string text, string tag, string glyph, bool isSelected = false)
    {
        return new NavigationViewItem
        {
            Content = text,
            Tag = tag,
            IsSelected = isSelected,
            Icon = new FontIcon { Glyph = glyph },
        };
    }

    private void InitializeGlobalHotkey()
    {
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        if (!ApplyGlobalToggleHotkey(_toggleHotkey, logResult: false))
        {
            _toggleHotkey = "PGDN";
            _globalHotkeyService.TrySetHotkeyToken(_toggleHotkey);
        }

        _globalHotkeyService.Enable();
        AppendLog($"Global toggle key ready: {_toggleHotkey}.");
    }

    private void OnGlobalHotkeyPressed()
    {
        if (DateTimeOffset.UtcNow < _ignoreGlobalHotkeyUntilUtc)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            if (_runCts is null)
            {
                await StartSchedulerAsync();
                return;
            }

            _runCts.Cancel();
        });
    }

    private bool ApplyGlobalToggleHotkey(string token, bool logResult)
    {
        string normalized = token.Trim().ToUpperInvariant();
        if (!_globalHotkeyService.TrySetHotkeyToken(normalized))
        {
            return false;
        }

        _toggleHotkey = normalized;
        if (logResult)
        {
            AppendLog($"Global toggle key set to {_toggleHotkey}.");
        }

        return true;
    }

    private void PlayToggleSound(bool isOn)
    {
        if (_audioCueCheckBox.IsChecked != true)
        {
            return;
        }

        try
        {
            Console.Beep(isOn ? 1200 : 700, 140);
        }
        catch
        {
            // no-op on environments that do not support beep
        }
    }

    private Grid BuildRunPanel()
    {
        var runGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        runGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        runGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        runGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        runGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        runGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        runGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var partyControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _startButton = new Button { Content = "▶ Start" };
        _startButton.Click += async (_, _) => await StartSchedulerAsync();
        _stopButton = new Button { Content = "■ Stop", IsEnabled = false };
        _stopButton.Click += (_, _) => _runCts?.Cancel();
        _toggleHotkeyTextBox = new TextBox { Text = _toggleHotkey, IsReadOnly = true, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
        _toggleHotkeyTextBox.KeyDown += (_, e) =>
        {
            string token = ConvertVirtualKeyToToken(e.Key);
            if (token is "Back" or "Esc")
            {
                e.Handled = true;
                return;
            }

            if (ApplyGlobalToggleHotkey(token, logResult: true))
            {
                _toggleHotkeyTextBox.Text = _toggleHotkey;
                _ignoreGlobalHotkeyUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(500);
            }
            else
            {
                AppendLog($"Unsupported toggle key '{token}'.");
            }

            e.Handled = true;
        };
        _audioCueCheckBox = new CheckBox { Content = "Audio cue", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        _statusText = new TextBlock { Text = "Status: Idle", VerticalAlignment = VerticalAlignment.Center };
        partyControls.Children.Add(new TextBlock { Text = "Party Trigger:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        partyControls.Children.Add(_startButton);
        partyControls.Children.Add(_stopButton);
        partyControls.Children.Add(new TextBlock { Text = "Toggle key:", VerticalAlignment = VerticalAlignment.Center });
        partyControls.Children.Add(_toggleHotkeyTextBox);
        partyControls.Children.Add(_audioCueCheckBox);
        partyControls.Children.Add(_statusText);
        Grid.SetRow(partyControls, 0);
        Grid.SetColumn(partyControls, 0);
        Grid.SetColumnSpan(partyControls, 2);
        runGrid.Children.Add(partyControls);

        var leftTop = new StackPanel { Spacing = 8 };
        leftTop.Children.Add(new TextBlock { Text = "👑 Archbishop Process", FontWeight = FontWeights.Bold, FontSize = 16 });
        _archbishopProcessComboBox = new ComboBox { DisplayMemberPath = "DisplayName", SelectedValuePath = "ProcessId" };
        _archbishopCharacterText = new TextBlock { Text = "Character: -" };
        leftTop.Children.Add(_archbishopProcessComboBox);
        Grid.SetRow(leftTop, 1);
        Grid.SetColumn(leftTop, 0);
        runGrid.Children.Add(leftTop);

        var rightTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        rightTop.Children.Add(new TextBlock
        {
            Text = "Set key press and delay in separate member fields.",
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(rightTop, 1);
        Grid.SetColumn(rightTop, 1);
        runGrid.Children.Add(rightTop);

        FrameworkElement triggerEditor = BuildTriggerEditor();
        Grid.SetRow(triggerEditor, 2);
        Grid.SetColumn(triggerEditor, 0);
        Grid.SetColumnSpan(triggerEditor, 2);
        runGrid.Children.Add(triggerEditor);

        FrameworkElement memberEditor = BuildMemberEditor();
        Grid.SetRow(memberEditor, 3);
        Grid.SetColumn(memberEditor, 0);
        Grid.SetColumnSpan(memberEditor, 2);
        runGrid.Children.Add(memberEditor);

        return runGrid;
    }

    private StackPanel BuildSettingsPanel()
    {
        var settings = new StackPanel { Spacing = 10, MaxWidth = 500 };
        _serverProcessNameTextBox = new TextBox { Text = DefaultExecutableName };
        _hpAddressTextBox = new TextBox { Text = DefaultHpAddressHex };
        _nameAddressTextBox = new TextBox { Text = DefaultNameAddressHex };
        _mouseFocusSettleDelayTextBox = new TextBox { Text = _keyDispatchService.FocusSettleDelayMs.ToString(), Width = 120 };
        _mouseFocusSettleDelayTextBox.TextChanged += (_, _) => ApplyMouseFocusSettleDelayFromSettings(logResult: false);
        settings.Children.Add(new TextBlock { Text = "🎯 Ragnarok Executable Name", FontWeight = FontWeights.Bold, FontSize = 15 });
        settings.Children.Add(_serverProcessNameTextBox);
        settings.Children.Add(new TextBlock { Text = "❤ HP Address (hex)", FontWeight = FontWeights.SemiBold });
        settings.Children.Add(_hpAddressTextBox);
        settings.Children.Add(new TextBlock { Text = "🏷 Name Address (hex)", FontWeight = FontWeights.SemiBold });
        settings.Children.Add(_nameAddressTextBox);
        settings.Children.Add(new TextBlock { Text = "🖱 Mouse Focus Settle Delay (ms)", FontWeight = FontWeights.SemiBold });
        settings.Children.Add(_mouseFocusSettleDelayTextBox);
        var debugButton = new Button { Content = "Debug Selected Process Name Read" };
        debugButton.Click += (_, _) => RunDebugForSelectedProcess();
        settings.Children.Add(debugButton);
        settings.Children.Add(new TextBlock { Text = "Defaults from supported_servers.json: hp=0146F28C name=01471CD8", TextWrapping = TextWrapping.Wrap });
        return settings;
    }

    private StackPanel BuildLogsPanel()
    {
        var logs = new StackPanel { Spacing = 8 };
        logs.Children.Add(new TextBlock { Text = "📝 Runtime Logs", FontWeight = FontWeights.Bold, FontSize = 16 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var clearButton = new Button { Content = "Clear Log" };
        clearButton.Click += (_, _) => ClearLogs();
        var openFolderButton = new Button { Content = "Open Logs Folder" };
        openFolderButton.Click += (_, _) => OpenLogsFolder();
        actions.Children.Add(clearButton);
        actions.Children.Add(openFolderButton);
        logs.Children.Add(actions);
        _logTextBox = new TextBox { AcceptsReturn = true, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 420 };
        ScrollViewer.SetVerticalScrollBarVisibility(_logTextBox, ScrollBarVisibility.Auto);
        logs.Children.Add(_logTextBox);
        return logs;
    }

    private StackPanel BuildProfilesPanel()
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 700 };
        panel.Children.Add(new TextBlock { Text = "👤 Profile Management", FontWeight = FontWeights.Bold, FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = "Create, load, copy, rename, or delete profiles.", Opacity = 0.85 });

        var selectRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        selectRow.Children.Add(new TextBlock { Text = "Current Profile:", VerticalAlignment = VerticalAlignment.Center });
        var loadSelectedButton = new Button { Content = "📂 Load Selected" };
        loadSelectedButton.Click += (_, _) =>
        {
            if (LoadProfile(_currentProfileName, logResult: true))
            {
                LoadSupportedProcessNames();
                RefreshProcesses();
            }
        };
        selectRow.Children.Add(loadSelectedButton);
        panel.Children.Add(selectRow);

        _profileNameTextBox = new TextBox { PlaceholderText = "Profile name", MinWidth = 180 };
        panel.Children.Add(_profileNameTextBox);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var createProfileButton = new Button { Content = "New" };
        createProfileButton.Click += (_, _) => CreateProfile();
        var copyProfileButton = new Button { Content = "Copy" };
        copyProfileButton.Click += (_, _) => CopyProfile();
        var renameProfileButton = new Button { Content = "Rename" };
        renameProfileButton.Click += (_, _) => RenameProfile();
        var deleteProfileButton = new Button { Content = "Delete" };
        deleteProfileButton.Click += (_, _) => DeleteProfile();

        actions.Children.Add(createProfileButton);
        actions.Children.Add(copyProfileButton);
        actions.Children.Add(renameProfileButton);
        actions.Children.Add(deleteProfileButton);
        panel.Children.Add(actions);

        var importExportRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var openProfileFolderButton = new Button { Content = "📁 Open profile folder" };
        openProfileFolderButton.Click += (_, _) => OpenProfileFolderInExplorer();
        var importProfilesButton = new Button { Content = "⬇ Import profiles…" };
        importProfilesButton.Click += (_, _) => ImportProfilesFromDisk();
        importExportRow.Children.Add(openProfileFolderButton);
        importExportRow.Children.Add(importProfilesButton);
        panel.Children.Add(importExportRow);

        panel.Children.Add(new TextBlock
        {
            Text = "Profiles live under %LocalAppData%\\4RTools\\PartyWingBuffTools\\profiles. Use Open folder to copy files to another PC, or Import to add .json profiles from disk (existing names are overwritten). Default cannot be renamed/deleted. Save on the top bar saves the current profile.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        return panel;
    }

    private FrameworkElement BuildTriggerEditor()
    {
        var wrapper = new Grid();
        wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = "🔁 Trigger Sequences", FontWeight = FontWeights.SemiBold, FontSize = 16 });
        var addButton = new Button { Content = "+ Add Trigger", MinWidth = 110 };
        addButton.Click += (_, _) => AddTrigger();
        title.Children.Add(addButton);
        wrapper.Children.Add(title);

        var header = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(HeaderText(string.Empty, 0));
        header.Children.Add(HeaderText("Active", 1));
        header.Children.Add(HeaderText("Name", 2));
        header.Children.Add(HeaderText("Interval(s)", 3));
        header.Children.Add(HeaderText("Teleport", 4));
        header.Children.Add(HeaderText("Post Delay(ms)", 5));
        header.Children.Add(HeaderText("Members", 6));
        header.Children.Add(HeaderText("Remove", 7));
        Grid.SetRow(header, 1);
        wrapper.Children.Add(header);

        _triggerRowsHost = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = new ScrollViewer { Content = _triggerRowsHost, Height = 120 };
        Grid.SetRow(scroll, 2);
        wrapper.Children.Add(scroll);
        return wrapper;
    }

    private FrameworkElement BuildMemberEditor()
    {
        var wrapper = new Grid();
        wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _memberEditorTitle = new TextBlock { Text = "👥 Members for trigger: -", FontWeight = FontWeights.SemiBold, FontSize = 16 };
        title.Children.Add(_memberEditorTitle);
        var addButton = new Button { Content = "+ Add Member", MinWidth = 110 };
        addButton.Click += (_, _) => AddMemberToSelectedTrigger();
        title.Children.Add(addButton);
        title.Children.Add(new TextBlock
        {
            Text = "Add key/mouse rows per member. Each row has a key or mouse point and delay (ms).",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
        wrapper.Children.Add(title);

        var header = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(HeaderText("Active", 0));
        header.Children.Add(HeaderText("Role/Label", 1));
        header.Children.Add(HeaderText("Process", 2));
        header.Children.Add(HeaderText("Steps", 3));
        header.Children.Add(HeaderText("Remove", 4));
        Grid.SetRow(header, 1);
        wrapper.Children.Add(header);

        _memberRowsHost = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = new ScrollViewer { Content = _memberRowsHost };
        Grid.SetRow(scroll, 2);
        wrapper.Children.Add(scroll);
        return wrapper;
    }

    private void AddTrigger(string name = "", string interval = "", string teleport = "F1", string postDelay = "300", bool isActive = true)
    {
        var trigger = new TriggerDefinition
        {
            IsActive = isActive,
            Name = name,
            IntervalSecText = interval,
            TeleportKey = teleport,
            PostDelayText = postDelay,
            Members = new List<MemberDefinition>(),
        };
        _triggers.Add(trigger);

        var rowGrid = new Grid { ColumnSpacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = new TextBlock
        {
            Text = "●",
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = _executingForegroundBrush,
            FontWeight = FontWeights.Bold,
        };
        var activeCheck = new CheckBox { IsChecked = isActive, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var nameBox = new TextBox { Text = name, PlaceholderText = "Buff60", Width = 170, HorizontalAlignment = HorizontalAlignment.Left };
        var intervalBox = new TextBox { Text = interval, PlaceholderText = "60", Width = 88, HorizontalAlignment = HorizontalAlignment.Left };
        var teleportBox = new TextBox { Text = teleport, PlaceholderText = "F1", IsReadOnly = true, Width = 88, HorizontalAlignment = HorizontalAlignment.Left };
        var delayBox = new TextBox { Text = postDelay, PlaceholderText = "300", Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        var membersButton = new Button { Content = "Edit", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 52 };
        var removeButton = new Button { Content = "X", MinWidth = 34 };

        activeCheck.Checked += (_, _) => trigger.IsActive = true;
        activeCheck.Unchecked += (_, _) => trigger.IsActive = false;
        nameBox.TextChanged += (_, _) => trigger.Name = nameBox.Text;
        intervalBox.TextChanged += (_, _) => trigger.IntervalSecText = intervalBox.Text;
        teleportBox.KeyDown += (_, e) =>
        {
            string token = ConvertVirtualKeyToToken(e.Key);
            if (token == "Back")
            {
                teleportBox.Text = string.Empty;
                trigger.TeleportKey = string.Empty;
                e.Handled = true;
                return;
            }

            if (token == "Esc")
            {
                e.Handled = true;
                return;
            }

            teleportBox.Text = token;
            trigger.TeleportKey = token;
            e.Handled = true;
        };
        delayBox.TextChanged += (_, _) => trigger.PostDelayText = delayBox.Text;
        membersButton.Click += (_, _) => SelectTrigger(trigger);
        removeButton.Click += (_, _) =>
        {
            _triggers.Remove(trigger);
            _triggerVisuals.Remove(trigger);
            _triggerIndicatorByTrigger.Remove(trigger);
            _triggerRowsHost.Children.Remove(rowGrid);
            if (_selectedTrigger == trigger)
            {
                SelectTrigger(_triggers.FirstOrDefault());
            }
        };

        Grid.SetColumn(indicator, 0);
        Grid.SetColumn(activeCheck, 1);
        Grid.SetColumn(nameBox, 2);
        Grid.SetColumn(intervalBox, 3);
        Grid.SetColumn(teleportBox, 4);
        Grid.SetColumn(delayBox, 5);
        Grid.SetColumn(membersButton, 6);
        Grid.SetColumn(removeButton, 7);
        rowGrid.Children.Add(indicator);
        rowGrid.Children.Add(activeCheck);
        rowGrid.Children.Add(nameBox);
        rowGrid.Children.Add(intervalBox);
        rowGrid.Children.Add(teleportBox);
        rowGrid.Children.Add(delayBox);
        rowGrid.Children.Add(membersButton);
        rowGrid.Children.Add(removeButton);

        _triggerVisuals[trigger] = new Control[] { nameBox, intervalBox, teleportBox, delayBox, membersButton };
        _triggerIndicatorByTrigger[trigger] = indicator;
        _triggerRowsHost.Children.Add(rowGrid);
    }

    private void SelectTrigger(TriggerDefinition? trigger)
    {
        _selectedTrigger = trigger;
        RenderMembers();
    }

    private void AddMemberToSelectedTrigger()
    {
        if (_selectedTrigger is null)
        {
            AppendLog("Select a trigger first.");
            return;
        }

        _selectedTrigger.Members.Add(new MemberDefinition
        {
            IsActive = true,
            Label = string.Empty,
            ProcessIdText = string.Empty,
            Steps = new List<ActionStepDefinition>
            {
                new() { StepType = "KEY", KeyText = string.Empty, DelayText = "250" },
            },
        });
        RenderMembers();
    }

    private void RenderMembers()
    {
        _memberRowsHost.Children.Clear();
        _stepVisualsByReason.Clear();
        _stepIndicatorByReason.Clear();
        if (_selectedTrigger is null)
        {
            _memberEditorTitle.Text = "Members for trigger: -";
            return;
        }

        _memberEditorTitle.Text = $"Members for trigger: {_selectedTrigger.Name}";
        foreach (MemberDefinition member in _selectedTrigger.Members)
        {
            var rowGrid = new Grid { ColumnSpacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var activeCheck = new CheckBox { IsChecked = member.IsActive, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var labelBox = new TextBox { Text = member.Label, PlaceholderText = "Bard", Width = 170, HorizontalAlignment = HorizontalAlignment.Left };
            var processCombo = new ComboBox { ItemsSource = _availableProcesses, DisplayMemberPath = "DisplayName", SelectedValuePath = "ProcessId", HorizontalAlignment = HorizontalAlignment.Left, Width = 250 };
            if (int.TryParse(member.ProcessIdText, out int pid))
            {
                processCombo.SelectedValue = pid;
            }
            var removeButton = new Button { Content = "X", MinWidth = 34 };
            var stepsHost = new StackPanel { Spacing = 4 };
            var addStepButton = new Button { Content = "+ Step", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 72 };

            if (member.Steps.Count == 0)
            {
                member.Steps.Add(new ActionStepDefinition { StepType = "KEY", KeyText = string.Empty, DelayText = "250" });
            }

            int executionStepNumber = 0;
            for (int stepIndex = 0; stepIndex < member.Steps.Count; stepIndex++)
            {
                ActionStepDefinition step = member.Steps[stepIndex];
                var stepGrid = new Grid { ColumnSpacing = 4 };
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var stepIndicator = new TextBlock
                {
                    Text = "●",
                    Visibility = Visibility.Collapsed,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    Foreground = _executingForegroundBrush,
                    FontWeight = FontWeights.Bold,
                };
                var typeCombo = new ComboBox
                {
                    Width = 86,
                    ItemsSource = new[] { "KEY", "MOUSE" },
                    SelectedItem = NormalizeStepType(step.StepType),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                var stepKeyBox = new TextBox { Text = step.KeyText, PlaceholderText = "Key", IsReadOnly = true, Width = 76, HorizontalAlignment = HorizontalAlignment.Left };
                var pointCombo = new ComboBox { Width = 146, DisplayMemberPath = "Name", SelectedValuePath = "Id", ItemsSource = _mousePoints, HorizontalAlignment = HorizontalAlignment.Left };
                pointCombo.SelectedValue = step.MousePointId;
                var stepDelayBox = new TextBox { Text = step.DelayText, PlaceholderText = "250", Width = 76, HorizontalAlignment = HorizontalAlignment.Left };
                var removeStepButton = new Button { Content = "-", MinWidth = 34 };

                void applyStepUiState()
                {
                    bool isKey = NormalizeStepType(step.StepType) == "KEY";
                    stepKeyBox.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
                    pointCombo.Visibility = isKey ? Visibility.Collapsed : Visibility.Visible;
                }

                typeCombo.SelectionChanged += (_, _) =>
                {
                    step.StepType = NormalizeStepType(typeCombo.SelectedItem?.ToString());
                    applyStepUiState();
                };
                stepKeyBox.TextChanged += (_, _) => step.KeyText = stepKeyBox.Text;
                pointCombo.SelectionChanged += (_, _) => step.MousePointId = pointCombo.SelectedValue?.ToString() ?? string.Empty;
                stepDelayBox.TextChanged += (_, _) => step.DelayText = stepDelayBox.Text;
                stepKeyBox.KeyDown += (_, e) =>
                {
                    if (NormalizeStepType(step.StepType) != "KEY")
                    {
                        e.Handled = true;
                        return;
                    }

                    string token = ConvertVirtualKeyToToken(e.Key);
                    if (token == "Back")
                    {
                        stepKeyBox.Text = string.Empty;
                        e.Handled = true;
                        return;
                    }

                    if (token == "Esc")
                    {
                        e.Handled = true;
                        return;
                    }

                    stepKeyBox.Text = token;
                    e.Handled = true;
                };
                removeStepButton.Click += (_, _) =>
                {
                    member.Steps.Remove(step);
                    RenderMembers();
                };

                Grid.SetColumn(stepIndicator, 0);
                Grid.SetColumn(typeCombo, 1);
                Grid.SetColumn(stepKeyBox, 2);
                Grid.SetColumn(pointCombo, 3);
                Grid.SetColumn(stepDelayBox, 4);
                Grid.SetColumn(removeStepButton, 5);
                stepGrid.Children.Add(stepIndicator);
                stepGrid.Children.Add(typeCombo);
                stepGrid.Children.Add(stepKeyBox);
                stepGrid.Children.Add(pointCombo);
                stepGrid.Children.Add(stepDelayBox);
                stepGrid.Children.Add(removeStepButton);
                applyStepUiState();
                stepsHost.Children.Add(stepGrid);

                if (int.TryParse(member.ProcessIdText, out int processId) && IsMemberStepExecutable(step))
                {
                    executionStepNumber++;
                    string memberLabel = GetMemberReasonLabel(member, processId);
                    string reason = BuildStepReason(_selectedTrigger.Name, memberLabel, processId, executionStepNumber);
                    _stepVisualsByReason[reason] = new Control[] { typeCombo, stepKeyBox, pointCombo, stepDelayBox };
                    _stepIndicatorByReason[reason] = stepIndicator;
                }
            }

            addStepButton.Click += (_, _) =>
            {
                member.Steps.Add(new ActionStepDefinition { StepType = "KEY", KeyText = string.Empty, DelayText = "250" });
                RenderMembers();
            };
            stepsHost.Children.Add(addStepButton);

            activeCheck.Checked += (_, _) => member.IsActive = true;
            activeCheck.Unchecked += (_, _) => member.IsActive = false;
            labelBox.TextChanged += (_, _) => member.Label = labelBox.Text;
            processCombo.SelectionChanged += (_, _) =>
            {
                member.ProcessIdText = processCombo.SelectedValue?.ToString() ?? string.Empty;
            };
            removeButton.Click += (_, _) =>
            {
                _selectedTrigger?.Members.Remove(member);
                RenderMembers();
            };

            Grid.SetColumn(activeCheck, 0);
            Grid.SetColumn(labelBox, 1);
            Grid.SetColumn(processCombo, 2);
            Grid.SetColumn(stepsHost, 3);
            Grid.SetColumn(removeButton, 4);
            rowGrid.Children.Add(activeCheck);
            rowGrid.Children.Add(labelBox);
            rowGrid.Children.Add(processCombo);
            rowGrid.Children.Add(stepsHost);
            rowGrid.Children.Add(removeButton);

            _memberRowsHost.Children.Add(rowGrid);
        }
    }

    private void RefreshProcesses()
    {
        if (!TryGetAddresses(out int hpAddress, out int nameAddress))
        {
            AppendLog("Invalid address setting. Use hex values like 01471CD8.");
            return;
        }

        _availableProcesses.Clear();
        IReadOnlyCollection<SupportedServerEntry> effectiveServers = _supportedServers.Count > 0
            ? _supportedServers
            : new[] { new SupportedServerEntry { ProcessName = _serverProcessNameTextBox.Text, HpAddress = hpAddress, NameAddress = nameAddress } };

        foreach (RagnarokProcessInfo processInfo in _processService.DiscoverProcesses(effectiveServers, hpAddress, nameAddress))
        {
            _availableProcesses.Add(processInfo);
        }

        _archbishopProcessComboBox.ItemsSource = null;
        _archbishopProcessComboBox.ItemsSource = _availableProcesses;
        if (_pendingArchbishopProcessId.HasValue && _availableProcesses.Any(p => p.ProcessId == _pendingArchbishopProcessId.Value))
        {
            _archbishopProcessComboBox.SelectedValue = _pendingArchbishopProcessId.Value;
        }
        else if (_availableProcesses.Count > 0 && _archbishopProcessComboBox.SelectedIndex < 0)
        {
            _archbishopProcessComboBox.SelectedIndex = 0;
        }

        _pendingArchbishopProcessId = null;
        RenderMembers();
        RefreshScheduledJobsProcessOptions();
        if (_mousePointTestProcessComboBox is not null)
        {
            _mousePointTestProcessComboBox.ItemsSource = null;
            _mousePointTestProcessComboBox.ItemsSource = _availableProcesses;
            if (_availableProcesses.Count > 0 && _mousePointTestProcessComboBox.SelectedIndex < 0)
            {
                _mousePointTestProcessComboBox.SelectedIndex = 0;
            }
        }
        UpdateArchbishopCharacterPreview();
        AppendLog($"Detected {_availableProcesses.Count} Ragnarok client(s).");
    }

    private void UpdateArchbishopCharacterPreview()
    {
        if (_archbishopProcessComboBox.SelectedValue is int processId)
        {
            _archbishopCharacterText.Text = _availableProcesses.FirstOrDefault(p => p.ProcessId == processId)?.CharacterName ?? "(Unknown)";
            return;
        }

        _archbishopCharacterText.Text = "Character: -";
    }

    private async Task StartSchedulerAsync()
    {
        if (_runCts is not null)
        {
            return;
        }

        if (_archbishopProcessComboBox.SelectedValue is not int archbishopProcessId)
        {
            AppendLog("Select an Archbishop process first.");
            return;
        }

        PartyConfig? config = BuildConfig(archbishopProcessId, logValidationErrors: true);
        if (config is null)
        {
            return;
        }

        _scheduler.Reset();
        lock (_triggerQueueLock)
        {
            _triggerQueue.Clear();
        }
        _runCts = new CancellationTokenSource();
        _startButton.IsEnabled = false;
        _stopButton.IsEnabled = true;
        _statusText.Text = "Status: Running";
        AppendLog("Scheduler started.");
        PlayToggleSound(isOn: true);
        try
        {
            await RunLoopAsync(_runCts.Token);
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        finally
        {
            await ClearExecutionVisualsAsync();
            _runCts?.Dispose();
            _runCts = null;
            _startButton.IsEnabled = true;
            _stopButton.IsEnabled = false;
            _statusText.Text = "Status: Idle";
            AppendLog("Scheduler stopped.");
            PlayToggleSound(isOn: false);
        }
    }

    private PartyConfig? BuildConfig(int archbishopProcessId, bool logValidationErrors = true)
    {
        var triggerConfigs = new List<TriggerSequenceConfig>();

        foreach (TriggerDefinition trigger in _triggers)
        {
            if (!trigger.IsActive)
            {
                continue;
            }

            string name = trigger.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!int.TryParse(trigger.IntervalSecText.Trim(), out int intervalSec) || intervalSec <= 0)
            {
                if (logValidationErrors)
                {
                    AppendLog($"Invalid interval in trigger '{name}'.");
                }
                continue;
            }

            if (!int.TryParse(trigger.PostDelayText.Trim(), out int postDelayMs))
            {
                postDelayMs = 300;
            }

            var memberConfigs = new List<MemberSequenceConfig>();
            foreach (MemberDefinition member in trigger.Members)
            {
                if (!member.IsActive)
                {
                    continue;
                }

                if (!int.TryParse(member.ProcessIdText.Trim(), out int pid))
                {
                    continue;
                }

                List<ActionStepConfig> steps = new();
                foreach (ActionStepDefinition step in member.Steps)
                {
                    int delayMs = 250;
                    if (int.TryParse(step.DelayText.Trim(), out int parsedDelay))
                    {
                        delayMs = Math.Max(0, parsedDelay);
                    }

                    string stepType = NormalizeStepType(step.StepType);
                    if (stepType == "KEY")
                    {
                        string keyToken = step.KeyText.Trim();
                        if (string.IsNullOrWhiteSpace(keyToken))
                        {
                            continue;
                        }

                        steps.Add(new ActionStepConfig
                        {
                            Type = ActionStepType.Key,
                            Key = keyToken.ToUpperInvariant(),
                            DelayAfterMs = delayMs,
                        });
                        continue;
                    }

                    if (!TryResolveMousePoint(step.MousePointId, out double x, out double y))
                    {
                        continue;
                    }

                    steps.Add(new ActionStepConfig
                    {
                        Type = ActionStepType.MouseClick,
                        NormalizedX = x,
                        NormalizedY = y,
                        DelayAfterMs = delayMs,
                    });
                }

                if (steps.Count == 0)
                {
                    continue;
                }

                memberConfigs.Add(new MemberSequenceConfig
                {
                    ProcessId = pid,
                    CharacterLabel = string.IsNullOrWhiteSpace(member.Label) ? $"Process {pid}" : member.Label,
                    Steps = steps,
                });
            }

            string teleportKey = trigger.TeleportKey.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(teleportKey))
            {
                if (logValidationErrors)
                {
                    AppendLog($"Teleport key is empty in trigger '{name}'.");
                }
                continue;
            }

            triggerConfigs.Add(new TriggerSequenceConfig
            {
                Name = name,
                IntervalSeconds = intervalSec,
                TeleportKey = teleportKey,
                PostTeleportDelayMs = Math.Max(0, postDelayMs),
                Members = memberConfigs,
            });
        }

        if (triggerConfigs.Count == 0)
        {
            if (logValidationErrors)
            {
                AppendLog("No valid trigger sequences configured.");
            }
            return null;
        }

        return new PartyConfig
        {
            ArchbishopProcessId = archbishopProcessId,
            TriggerSequences = triggerConfigs,
        };
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_archbishopProcessComboBox.SelectedValue is not int archbishopProcessId)
            {
                await Task.Delay(120, ct);
                continue;
            }

            PartyConfig? config = BuildConfig(archbishopProcessId, logValidationErrors: false);
            if (config is null)
            {
                await Task.Delay(120, ct);
                continue;
            }

            IReadOnlyList<DispatchPlan> plans = _scheduler.BuildPlans(config, DateTimeOffset.UtcNow);
            EnqueuePlans(plans);
            if (TryDequeuePlan(out DispatchPlan plan))
            {
                await ExecutePlanAsync(plan, ct);
            }

            await Task.Delay(120, ct);
        }
    }

    private void EnqueuePlans(IEnumerable<DispatchPlan> plans)
    {
        lock (_triggerQueueLock)
        {
            foreach (DispatchPlan plan in plans)
            {
                _triggerQueue.Enqueue(plan);
            }
        }
    }

    private bool TryDequeuePlan(out DispatchPlan plan)
    {
        lock (_triggerQueueLock)
        {
            if (_triggerQueue.Count > 0)
            {
                plan = _triggerQueue.Dequeue();
                return true;
            }
        }

        plan = null!;
        return false;
    }

    private async Task ExecutePlanAsync(DispatchPlan plan, CancellationToken ct)
    {
        AppendLog($"Trigger fired: {plan.TriggerName}");
        await SetTriggerExecutingVisualAsync(plan.TriggerName, isExecuting: true);
        await _dispatchExecutionGate.WaitAsync(ct);
        try
        {

        // Execute Archbishop teleport first (if present), then run members in parallel.
        DispatchAction? teleportAction = plan.Actions.FirstOrDefault(a => a.Reason.EndsWith(":Teleport", StringComparison.OrdinalIgnoreCase));
        if (teleportAction is not null)
        {
            bool sent = ExecuteDispatchAction(teleportAction);
            AppendLog(sent
                ? $"{FormatActionForLog(teleportAction)} -> {teleportAction.ProcessId} ({teleportAction.Reason})"
                : $"Failed {FormatActionForLog(teleportAction)} -> {teleportAction.ProcessId} ({teleportAction.Reason})");

            if (teleportAction.DelayAfterMs > 0)
            {
                await Task.Delay(teleportAction.DelayAfterMs, ct);
            }
        }

        var memberSequences = plan.Actions
            .Where(a => !a.Reason.EndsWith(":Teleport", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => (a.ProcessId, GetMemberSequenceGroupKey(a.Reason)))
            .Select(g => g.ToList())
            .ToList();

        if (memberSequences.Count == 0)
        {
            return;
        }

        List<Task> memberTasks = memberSequences
            .Select(sequence => RunMemberSequenceAsync(sequence, ct))
            .ToList();

        await Task.WhenAll(memberTasks);
        }
        finally
        {
            _dispatchExecutionGate.Release();
            await SetTriggerExecutingVisualAsync(plan.TriggerName, isExecuting: false);
        }
    }

    private async Task RunMemberSequenceAsync(IReadOnlyList<DispatchAction> sequence, CancellationToken ct)
    {
        foreach (DispatchAction action in sequence)
        {
            await SetStepExecutingVisualAsync(action.Reason, isExecuting: true);
            try
            {
                bool sent = ExecuteDispatchAction(action);
                AppendLog(sent
                    ? $"{FormatActionForLog(action)} -> {action.ProcessId} ({action.Reason})"
                    : $"Failed {FormatActionForLog(action)} -> {action.ProcessId} ({action.Reason})");

                if (action.DelayAfterMs > 0)
                {
                    await Task.Delay(action.DelayAfterMs, ct);
                }
            }
            finally
            {
                await SetStepExecutingVisualAsync(action.Reason, isExecuting: false);
            }
        }
    }

    private bool ExecuteDispatchAction(DispatchAction action)
    {
        if (action.Type == ActionStepType.MouseClick && action.NormalizedX.HasValue && action.NormalizedY.HasValue)
        {
            return _keyDispatchService.SendMouseClickNormalized(action.ProcessId, action.NormalizedX.Value, action.NormalizedY.Value);
        }

        return !string.IsNullOrWhiteSpace(action.Key) && _keyDispatchService.SendKey(action.ProcessId, action.Key);
    }

    private static string FormatActionForLog(DispatchAction action)
    {
        if (action.Type == ActionStepType.MouseClick)
        {
            return action.NormalizedX.HasValue && action.NormalizedY.HasValue
                ? $"Mouse({action.NormalizedX.Value:0.###},{action.NormalizedY.Value:0.###})"
                : "Mouse(?)";
        }

        return $"Key {action.Key}";
    }

    private string GetMemberReasonLabel(MemberDefinition member, int processId)
    {
        return string.IsNullOrWhiteSpace(member.Label) ? $"Process {processId}" : member.Label;
    }

    private static string BuildStepReason(string triggerName, string memberLabel, int processId, int stepNumber)
    {
        return $"{triggerName}:{memberLabel}:{processId}:Step{stepNumber}";
    }

    private static string GetMemberSequenceGroupKey(string reason)
    {
        int idx = reason.LastIndexOf(":Step", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? reason[..idx] : reason;
    }

    private static string NormalizeStepType(string? stepType)
    {
        return string.Equals(stepType, "MOUSE", StringComparison.OrdinalIgnoreCase) ? "MOUSE" : "KEY";
    }

    private bool IsMemberStepExecutable(ActionStepDefinition step)
    {
        if (NormalizeStepType(step.StepType) == "KEY")
        {
            return !string.IsNullOrWhiteSpace(step.KeyText);
        }

        return TryResolveMousePoint(step.MousePointId, out _, out _);
    }

    private void InitializeExecutionIndicatorBrushes()
    {
        var resources = Application.Current.Resources;
        _normalForegroundBrush = resources.TryGetValue("TextFillColorPrimaryBrush", out object? normal)
            ? (Microsoft.UI.Xaml.Media.Brush)normal
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        _executingForegroundBrush = resources.TryGetValue("SystemFillColorCautionBrush", out object? accent)
            ? (Microsoft.UI.Xaml.Media.Brush)accent
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
    }

    private async Task SetTriggerExecutingVisualAsync(string triggerName, bool isExecuting)
    {
        await RunOnUiThreadAsync(() =>
        {
            TriggerDefinition? trigger = _triggers.FirstOrDefault(t => t.Name.Equals(triggerName, StringComparison.OrdinalIgnoreCase));
            if (trigger is null || !_triggerVisuals.TryGetValue(trigger, out IReadOnlyList<Control>? controls))
            {
                return;
            }

            SetControlsExecutionForeground(controls, isExecuting);
            if (_triggerIndicatorByTrigger.TryGetValue(trigger, out TextBlock? indicator))
            {
                indicator.Visibility = isExecuting ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    private async Task SetStepExecutingVisualAsync(string reason, bool isExecuting)
    {
        await RunOnUiThreadAsync(() =>
        {
            if (_stepVisualsByReason.TryGetValue(reason, out IReadOnlyList<Control>? controls))
            {
                SetControlsExecutionForeground(controls, isExecuting);
            }

            if (_stepIndicatorByReason.TryGetValue(reason, out TextBlock? indicator))
            {
                indicator.Visibility = isExecuting ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.SetException(new InvalidOperationException("Unable to dispatch execution indicator update to UI thread."));
        }

        return tcs.Task;
    }

    private void SetControlsExecutionForeground(IEnumerable<Control> controls, bool isExecuting)
    {
        Microsoft.UI.Xaml.Media.Brush targetBrush = isExecuting ? _executingForegroundBrush : _normalForegroundBrush;
        foreach (Control control in controls)
        {
            control.Foreground = targetBrush;
            control.FontWeight = isExecuting ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    private async Task ClearExecutionVisualsAsync()
    {
        await RunOnUiThreadAsync(() =>
        {
            foreach (IReadOnlyList<Control> controls in _triggerVisuals.Values)
            {
                SetControlsExecutionForeground(controls, isExecuting: false);
            }

            foreach (IReadOnlyList<Control> controls in _stepVisualsByReason.Values)
            {
                SetControlsExecutionForeground(controls, isExecuting: false);
            }

            foreach (TextBlock indicator in _triggerIndicatorByTrigger.Values)
            {
                indicator.Visibility = Visibility.Collapsed;
            }

            foreach (TextBlock indicator in _stepIndicatorByReason.Values)
            {
                indicator.Visibility = Visibility.Collapsed;
            }
        });
    }

    private bool TryGetAddresses(out int hpAddress, out int nameAddress)
    {
        hpAddress = 0;
        nameAddress = 0;
        bool hasHp = int.TryParse(_hpAddressTextBox.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out hpAddress);
        bool hasName = int.TryParse(_nameAddressTextBox.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out nameAddress);
        return hasHp && hasName;
    }

    private bool ApplyMouseFocusSettleDelayFromSettings(bool logResult)
    {
        if (!int.TryParse(_mouseFocusSettleDelayTextBox.Text.Trim(), out int delay))
        {
            if (logResult)
            {
                AppendLog("Invalid mouse focus settle delay. Use integer milliseconds.");
            }

            return false;
        }

        _keyDispatchService.FocusSettleDelayMs = Math.Max(0, delay);
        return true;
    }

    private StackPanel BuildMousePointsPanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "🖱 Mouse Points", FontWeight = FontWeights.Bold, FontSize = 16 });
        panel.Children.Add(new TextBlock
        {
            Text = "Define named click positions with normalized X/Y (0.0-1.0) relative to each Ragnarok client window.",
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
        });

        var addButton = new Button { Content = "+ Add Point", HorizontalAlignment = HorizontalAlignment.Left };
        _mousePointTestProcessComboBox = new ComboBox
        {
            Width = 260,
            ItemsSource = _availableProcesses,
            DisplayMemberPath = "DisplayName",
            SelectedValuePath = "ProcessId",
            PlaceholderText = "Select process for Test Click",
        };
        var controlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controlRow.Children.Add(addButton);
        controlRow.Children.Add(new TextBlock { Text = "Test Process:", VerticalAlignment = VerticalAlignment.Center });
        controlRow.Children.Add(_mousePointTestProcessComboBox);
        addButton.Click += (_, _) =>
        {
            _mousePoints.Add(new MousePointDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"Point{_mousePoints.Count + 1}",
                XText = "0.500",
                YText = "0.500",
            });
            RenderMousePoints();
        };
        panel.Children.Add(controlRow);

        _mousePointsHost = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer
        {
            Content = _mousePointsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        panel.Children.Add(scroll);
        RenderMousePoints();
        return panel;
    }

    private void RenderMousePoints()
    {
        if (_mousePointsHost is null)
        {
            return;
        }

        _mousePointsHost.Children.Clear();
        foreach (MousePointDefinition point in _mousePoints.ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var nameBox = new TextBox { Text = point.Name, Width = 180, PlaceholderText = "Point name" };
            var xBox = new TextBox { Text = point.XText, Width = 90, PlaceholderText = "X (0.0-1.0)" };
            var yBox = new TextBox { Text = point.YText, Width = 90, PlaceholderText = "Y (0.0-1.0)" };
            var testButton = new Button { Content = "Test Click" };
            var remove = new Button { Content = "Delete" };
            nameBox.TextChanged += (_, _) => point.Name = nameBox.Text;
            xBox.TextChanged += (_, _) => point.XText = xBox.Text;
            yBox.TextChanged += (_, _) => point.YText = yBox.Text;
            testButton.Click += (_, _) =>
            {
                if (_mousePointTestProcessComboBox.SelectedValue is not int processId)
                {
                    AppendLog("Mouse test: select a process first.");
                    return;
                }

                if (!TryResolveMousePoint(point.Id, out double x, out double y))
                {
                    AppendLog($"Mouse test: invalid point '{point.Name}'.");
                    return;
                }

                bool sent = _keyDispatchService.SendMouseClickNormalized(processId, x, y);
                AppendLog(sent
                    ? $"Mouse test clicked '{point.Name}' ({x:0.###},{y:0.###}) -> {processId}"
                    : $"Mouse test failed '{point.Name}' ({x:0.###},{y:0.###}) -> {processId}");
            };
            remove.Click += (_, _) =>
            {
                _mousePoints.Remove(point);
                RenderMousePoints();
                RenderMembers();
                if (_scheduledJobsInitialized)
                {
                    RenderScheduledJobs();
                }
            };
            row.Children.Add(new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(nameBox);
            row.Children.Add(new TextBlock { Text = "X:", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(xBox);
            row.Children.Add(new TextBlock { Text = "Y:", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(yBox);
            row.Children.Add(testButton);
            row.Children.Add(remove);
            _mousePointsHost.Children.Add(row);
        }
    }

    private void EnsureDefaultMousePoints()
    {
        if (_mousePoints.Count > 0)
        {
            return;
        }

        _mousePoints.Add(new MousePointDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Center",
            XText = "0.500",
            YText = "0.500",
        });
        _mousePoints.Add(new MousePointDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Exit Nova",
            XText = "0.600",
            YText = "0.400",
        });
    }

    private bool TryResolveMousePoint(string pointId, out double x, out double y)
    {
        x = 0;
        y = 0;
        MousePointDefinition? point = _mousePoints.FirstOrDefault(p => string.Equals(p.Id, pointId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
        {
            return false;
        }

        bool hasX = double.TryParse(point.XText, out x);
        bool hasY = double.TryParse(point.YText, out y);
        if (!hasX || !hasY)
        {
            return false;
        }

        x = Math.Clamp(x, 0, 1);
        y = Math.Clamp(y, 0, 1);
        return true;
    }

    private void ShowPanel(string panel)
    {
        if ((panel == "scheduled" || panel == "scheduled-actions") && !_scheduledJobsInitialized)
        {
            try
            {
                _scheduledJobsInitialized = true;
                InitializeScheduledJobs();
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to open Scheduled Jobs tab: {ex.Message}");
                _scheduledJobsInitialized = false;
                panel = "run";
            }
        }

        _runPanel.Visibility = panel == "run" ? Visibility.Visible : Visibility.Collapsed;
        _settingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        _profilesPanel.Visibility = panel == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        _logsPanel.Visibility = panel == "logs" ? Visibility.Visible : Visibility.Collapsed;
        _scheduledActionsPanel.Visibility = panel == "scheduled-actions" ? Visibility.Visible : Visibility.Collapsed;
        _scheduledJobsPanel.Visibility = panel == "scheduled" ? Visibility.Visible : Visibility.Collapsed;
        _mousePointsPanel.Visibility = panel == "mouse-points" ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetCharacterName(string processIdText)
    {
        if (!int.TryParse(processIdText, out int processId))
        {
            return "(Unknown)";
        }

        return _availableProcesses.FirstOrDefault(p => p.ProcessId == processId)?.CharacterName ?? "(Unknown)";
    }

    private string ConvertVirtualKeyToToken(Windows.System.VirtualKey key)
    {
        if (key == Windows.System.VirtualKey.Back)
        {
            return "Back";
        }

        if (key == Windows.System.VirtualKey.Escape)
        {
            return "Esc";
        }

        if (key is >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number9)
        {
            int value = (int)key - (int)Windows.System.VirtualKey.Number0;
            return value.ToString();
        }

        if (key is >= Windows.System.VirtualKey.NumberPad0 and <= Windows.System.VirtualKey.NumberPad9)
        {
            int value = (int)key - (int)Windows.System.VirtualKey.NumberPad0;
            return value.ToString();
        }

        if (key is >= Windows.System.VirtualKey.A and <= Windows.System.VirtualKey.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        return key switch
        {
            Windows.System.VirtualKey.Enter => "ENTER",
            Windows.System.VirtualKey.Up => "UP",
            Windows.System.VirtualKey.Down => "DOWN",
            Windows.System.VirtualKey.Left => "LEFT",
            Windows.System.VirtualKey.Right => "RIGHT",
            Windows.System.VirtualKey.PageUp => "PGUP",
            Windows.System.VirtualKey.PageDown => "PGDN",
            Windows.System.VirtualKey.Tab => "TAB",
            Windows.System.VirtualKey.Space => "SPACE",
            _ => key.ToString().ToUpperInvariant(),
        };
    }

    private TextBlock HeaderText(string text, int col)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.9,
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static string ReadAppVersion()
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plusIndex = informational.IndexOf('+');
            return plusIndex > 0 ? informational[..plusIndex] : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void AppendLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        EnqueueLogLineForFile(line);
        _recentLogLines.Enqueue(line);
        while (_recentLogLines.Count > MaxDisplayedLogLines)
        {
            _recentLogLines.Dequeue();
        }

        _logTextBox.Text = string.Join(Environment.NewLine, _recentLogLines) + Environment.NewLine;
        _logTextBox.SelectionStart = _logTextBox.Text.Length;
    }

    private void ClearLogs()
    {
        _recentLogLines.Clear();
        _logTextBox.Text = string.Empty;
        try
        {
            string logPath = GetTodayLogFilePath();
            if (File.Exists(logPath))
            {
                File.WriteAllText(logPath, string.Empty);
            }

            AppendLog("Log cleared.");
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to clear log file: {ex.Message}");
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            string path = GetLogDirectoryPath();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to open logs folder: {ex.Message}");
        }
    }

    private void WriteLogLineToFile(string line)
    {
        try
        {
            Directory.CreateDirectory(GetLogDirectoryPath());
            lock (_logFileLock)
            {
                File.AppendAllText(GetTodayLogFilePath(), line + Environment.NewLine);
            }
        }
        catch
        {
            // keep app responsive even if file logging fails
        }
    }

    private void EnqueueLogLineForFile(string line)
    {
        lock (_pendingFileLogLock)
        {
            _pendingFileLogLines.Enqueue(line);
        }

        _logSignal.Release();
    }

    private async Task RunLogWriterAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _logSignal.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            List<string> batch = new();
            lock (_pendingFileLogLock)
            {
                while (_pendingFileLogLines.Count > 0)
                {
                    batch.Add(_pendingFileLogLines.Dequeue());
                }
            }

            foreach (string line in batch)
            {
                WriteLogLineToFile(line);
            }
        }

        // Flush remaining log lines before shutdown.
        List<string> remaining = new();
        lock (_pendingFileLogLock)
        {
            while (_pendingFileLogLines.Count > 0)
            {
                remaining.Add(_pendingFileLogLines.Dequeue());
            }
        }

        foreach (string line in remaining)
        {
            WriteLogLineToFile(line);
        }
    }

    private void StopLogWriter()
    {
        try
        {
            _logWriterCts.Cancel();
            _logSignal.Release();
            _logWriterTask?.Wait(1200);
        }
        catch
        {
            // no-op during app shutdown
        }
    }

    private string GetTodayLogFilePath()
    {
        return Path.Combine(GetLogDirectoryPath(), $"{DateTime.Now:yyyy-MM-dd}.log");
    }

    private string GetLogDirectoryPath()
    {
        return Path.Combine(GetAppDataRootPath(), "logs");
    }

    private void LoadSupportedProcessNames()
    {
        _supportedServers.Clear();
        List<string> candidates = new()
        {
            Path.Combine(AppContext.BaseDirectory, "supported_servers.json"),
            Path.Combine(AppContext.BaseDirectory, "bin", "Release", "supported_servers.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "supported_servers.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "bin", "Release", "supported_servers.json"),
        };

        foreach (string filePath in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                string raw = File.ReadAllText(filePath);
                List<SupportedServerDto>? list = JsonSerializer.Deserialize<List<SupportedServerDto>>(raw);
                if (list is null)
                {
                    continue;
                }

                foreach (SupportedServerDto item in list)
                {
                    if (string.IsNullOrWhiteSpace(item.name))
                    {
                        continue;
                    }

                    int hpAddress = 0;
                    int nameAddress = 0;
                    bool hasHpHex = int.TryParse(item.hpAddress ?? string.Empty, System.Globalization.NumberStyles.HexNumber, null, out hpAddress);
                    bool hasNameHex = int.TryParse(item.nameAddress ?? string.Empty, System.Globalization.NumberStyles.HexNumber, null, out nameAddress);
                    if (!hasHpHex && item.hpAddressPointer > 0)
                    {
                        hpAddress = item.hpAddressPointer;
                    }
                    if (!hasNameHex && item.nameAddressPointer > 0)
                    {
                        nameAddress = item.nameAddressPointer;
                    }

                    if (hpAddress <= 0 || nameAddress <= 0)
                    {
                        continue;
                    }

                    _supportedServers.Add(new SupportedServerEntry
                    {
                        ProcessName = item.name,
                        HpAddress = hpAddress,
                        NameAddress = nameAddress,
                    });
                }

                if (_supportedServers.Count > 0)
                {
                    AppendLog($"Loaded {_supportedServers.Count} supported server signature(s) from {Path.GetFileName(filePath)}.");
                    return;
                }
            }
            catch
            {
                // ignore and try next path
            }
        }

        if (!string.IsNullOrWhiteSpace(_serverProcessNameTextBox.Text))
        {
            if (int.TryParse(_hpAddressTextBox.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out int hp) &&
                int.TryParse(_nameAddressTextBox.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out int na))
            {
                _supportedServers.Add(new SupportedServerEntry
                {
                    ProcessName = _serverProcessNameTextBox.Text.Trim(),
                    HpAddress = hp,
                    NameAddress = na,
                });
            }
            AppendLog("supported_servers.json not found. Using executable name textbox as fallback.");
        }
    }

    private void RunDebugForSelectedProcess()
    {
        if (_archbishopProcessComboBox.SelectedItem is not RagnarokProcessInfo selected)
        {
            AppendLog("Debug: select an Archbishop process first.");
            return;
        }

        if (!TryGetAddresses(out int hpAddress, out int nameAddress))
        {
            AppendLog("Debug: invalid fallback address settings.");
            return;
        }

        string report = _processService.BuildDebugReport(
            selected.ProcessId,
            selected.ProcessName,
            _supportedServers,
            hpAddress,
            nameAddress);

        AppendLog("========== DEBUG START ==========");
        AppendLog(report);
        AppendLog("=========== DEBUG END ===========");
        ShowPanel("logs");
    }

    /// <summary>Per-user folder: <c>%LocalAppData%\4RTools\PartyWingBuffTools\profiles\*.json</c>.</summary>
    private string GetProfileDirectoryPath()
    {
        return Path.Combine(GetAppDataRootPath(), "profiles");
    }

    private string GetAppDataRootPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "4RTools",
            "PartyWingBuffTools");
    }

    private string GetProfileFilePath(string profileName)
    {
        return Path.Combine(GetProfileDirectoryPath(), $"{profileName}.json");
    }

    private List<string> ListProfiles()
    {
        try
        {
            if (!Directory.Exists(GetProfileDirectoryPath()))
            {
                return new List<string>();
            }

            return Directory
                .GetFiles(GetProfileDirectoryPath(), "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch
        {
            return new List<string>();
        }
    }

    private void EnsureDefaultProfileExists()
    {
        TryMigrateLegacyProfiles();
        Directory.CreateDirectory(GetProfileDirectoryPath());
        string filePath = GetProfileFilePath(DefaultProfileName);
        if (!File.Exists(filePath))
        {
            SaveProfile(DefaultProfileName, logResult: false);
        }
    }

    /// <summary>
    /// Best-effort copy from portable/legacy folders beside the exe into AppData so upgrades do not lose profiles.
    /// </summary>
    private void TryMigrateLegacyProfiles()
    {
        string targetDir = GetProfileDirectoryPath();

        try
        {
            Directory.CreateDirectory(targetDir);

            TryCopyProfileJsonFiles(Path.Combine(AppContext.BaseDirectory, "profiles"), targetDir);
            TryCopyProfileJsonFiles(Path.Combine(AppContext.BaseDirectory, "Profiles"), targetDir);
        }
        catch
        {
            // best-effort migration only
        }
    }

    private void OpenProfileFolderInExplorer()
    {
        try
        {
            string dir = GetProfileDirectoryPath();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendLog($"Could not open profile folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Uses the Win32 common dialog (not WinRT FileOpenPicker) so import works for unpackaged WinUI.
    /// </summary>
    private void ImportProfilesFromDisk()
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            IReadOnlyList<string> paths = ComDlgOpenMultipleFiles.ShowOpenJson(hwnd);
            if (paths.Count == 0)
            {
                return;
            }

            string targetDir = GetProfileDirectoryPath();
            Directory.CreateDirectory(targetDir);

            int imported = 0;
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string destPath = Path.Combine(targetDir, name);
                File.Copy(path, destPath, overwrite: true);
                imported++;
            }

            AppendLog(imported == 0
                ? "No .json files were imported."
                : $"Imported {imported} profile file(s).");
            RefreshProfileList(selectProfileName: _currentProfileName);
        }
        catch (Exception ex)
        {
            AppendLog($"Import failed: {ex.Message}");
        }
    }

    private static void TryCopyProfileJsonFiles(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        foreach (string sourceFile in Directory.GetFiles(sourceDir, "*.json"))
        {
            string fileName = Path.GetFileName(sourceFile);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            string targetFile = Path.Combine(targetDir, fileName);
            if (!File.Exists(targetFile))
            {
                File.Copy(sourceFile, targetFile);
            }
        }
    }

    private void RefreshProfileList(string? selectProfileName = null)
    {
        List<string> profiles = ListProfiles();
        if (profiles.Count == 0)
        {
            profiles.Add(DefaultProfileName);
        }

        _profileComboBox.ItemsSource = null;
        _profileComboBox.ItemsSource = profiles;
        string target = string.IsNullOrWhiteSpace(selectProfileName) ? _currentProfileName : selectProfileName;
        if (!profiles.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            target = profiles[0];
        }

        _profileComboBox.SelectedItem = profiles.First(p => p.Equals(target, StringComparison.OrdinalIgnoreCase));
        _currentProfileName = target;
    }

    private string GetRequestedProfileName()
    {
        return (_profileNameTextBox.Text ?? string.Empty).Trim();
    }

    private void CreateProfile()
    {
        string requestedName = GetRequestedProfileName();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            AppendLog("Enter a profile name first.");
            return;
        }

        if (File.Exists(GetProfileFilePath(requestedName)))
        {
            AppendLog($"Profile '{requestedName}' already exists.");
            return;
        }

        SaveProfile(requestedName, logResult: false);
        RefreshProfileList(selectProfileName: requestedName);
        AppendLog($"Profile '{requestedName}' created.");
    }

    private void CopyProfile()
    {
        string requestedName = GetRequestedProfileName();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            AppendLog("Enter target profile name for copy.");
            return;
        }

        string sourceProfileName = _currentProfileName;
        string sourcePath = GetProfileFilePath(sourceProfileName);
        string targetPath = GetProfileFilePath(requestedName);
        if (!File.Exists(sourcePath))
        {
            AppendLog($"Current profile '{_currentProfileName}' not found.");
            return;
        }

        if (File.Exists(targetPath))
        {
            AppendLog($"Profile '{requestedName}' already exists.");
            return;
        }

        File.Copy(sourcePath, targetPath);
        RefreshProfileList(selectProfileName: requestedName);
        LoadProfile(_currentProfileName, logResult: false);
        LoadSupportedProcessNames();
        RefreshProcesses();
        AppendLog($"Profile '{_currentProfileName}' copied from '{sourceProfileName}'.");
    }

    private void RenameProfile()
    {
        string requestedName = GetRequestedProfileName();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            AppendLog("Enter new profile name for rename.");
            return;
        }

        if (_currentProfileName.Equals(DefaultProfileName, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Cannot rename Default profile.");
            return;
        }

        string oldPath = GetProfileFilePath(_currentProfileName);
        string newPath = GetProfileFilePath(requestedName);
        if (!File.Exists(oldPath))
        {
            AppendLog($"Current profile '{_currentProfileName}' not found.");
            return;
        }

        if (File.Exists(newPath))
        {
            AppendLog($"Profile '{requestedName}' already exists.");
            return;
        }

        File.Move(oldPath, newPath);
        RefreshProfileList(selectProfileName: requestedName);
        AppendLog($"Profile renamed to '{requestedName}'.");
    }

    private void DeleteProfile()
    {
        if (_currentProfileName.Equals(DefaultProfileName, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Cannot delete Default profile.");
            return;
        }

        string path = GetProfileFilePath(_currentProfileName);
        if (!File.Exists(path))
        {
            AppendLog($"Profile '{_currentProfileName}' not found.");
            return;
        }

        File.Delete(path);
        RefreshProfileList(selectProfileName: DefaultProfileName);
        LoadProfile(_currentProfileName, logResult: false);
        LoadSupportedProcessNames();
        RefreshProcesses();
        AppendLog("Profile deleted.");
    }

    private bool SaveProfile(string profileName, bool logResult)
    {
        try
        {
            Directory.CreateDirectory(GetProfileDirectoryPath());
            var profile = new AppProfile
            {
                ServerProcessName = _serverProcessNameTextBox.Text.Trim(),
                HpAddressHex = _hpAddressTextBox.Text.Trim(),
                NameAddressHex = _nameAddressTextBox.Text.Trim(),
                ToggleHotkey = _toggleHotkey,
                EnableAudioCue = _audioCueCheckBox.IsChecked == true,
                MouseFocusSettleDelayMs = _keyDispatchService.FocusSettleDelayMs,
                ArchbishopProcessId = _archbishopProcessComboBox.SelectedValue as int?,
                MousePoints = _mousePoints.Select(p => new MousePointProfile
                {
                    Id = p.Id,
                    Name = p.Name,
                    XText = p.XText,
                    YText = p.YText,
                }).ToList(),
                Triggers = _triggers.Select(t => new TriggerProfile
                {
                    IsActive = t.IsActive,
                    Name = t.Name,
                    IntervalSecText = t.IntervalSecText,
                    TeleportKey = t.TeleportKey,
                    PostDelayText = t.PostDelayText,
                    Members = t.Members.Select(m => new MemberProfile
                    {
                        IsActive = m.IsActive,
                        Label = m.Label,
                        ProcessIdText = m.ProcessIdText,
                        Steps = m.Steps.Select(s => new StepProfile
                        {
                            StepType = NormalizeStepType(s.StepType),
                            KeyText = s.KeyText,
                            MousePointId = s.MousePointId,
                            DelayText = s.DelayText,
                        }).ToList(),
                        // Keep writing legacy KeySteps for downgrade compatibility (v1.0.4).
                        KeySteps = m.Steps
                            .Where(s => NormalizeStepType(s.StepType) == "KEY")
                            .Select(s => new KeyStepProfile
                            {
                                KeyText = s.KeyText,
                                DelayText = s.DelayText,
                            }).ToList(),
                    }).ToList(),
                }).ToList(),
            };

            string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetProfileFilePath(profileName), json);
            if (logResult)
            {
                AppendLog($"Profile '{profileName}' saved.");
            }

            return true;
        }
        catch (Exception ex)
        {
            if (logResult)
            {
                AppendLog($"Failed to save profile '{profileName}': {ex.Message}");
            }

            return false;
        }
    }

    private bool LoadProfile(string profileName, bool logResult)
    {
        try
        {
            string filePath = GetProfileFilePath(profileName);
            if (!File.Exists(filePath))
            {
                if (logResult)
                {
                    AppendLog($"Profile '{profileName}' not found. Using current settings.");
                }

                return false;
            }

            string raw = File.ReadAllText(filePath);
            AppProfile? profile = JsonSerializer.Deserialize<AppProfile>(raw);
            if (profile is null)
            {
                if (logResult)
                {
                    AppendLog($"Profile '{profileName}' is invalid.");
                }

                return false;
            }

            _serverProcessNameTextBox.Text = profile.ServerProcessName ?? DefaultExecutableName;
            _hpAddressTextBox.Text = string.IsNullOrWhiteSpace(profile.HpAddressHex) ? DefaultHpAddressHex : profile.HpAddressHex;
            _nameAddressTextBox.Text = string.IsNullOrWhiteSpace(profile.NameAddressHex) ? DefaultNameAddressHex : profile.NameAddressHex;
            _toggleHotkey = string.IsNullOrWhiteSpace(profile.ToggleHotkey) ? "PGDN" : profile.ToggleHotkey;
            _ = ApplyGlobalToggleHotkey(_toggleHotkey, logResult: false);
            _toggleHotkeyTextBox.Text = _toggleHotkey;
            _audioCueCheckBox.IsChecked = profile.EnableAudioCue;
            _mouseFocusSettleDelayTextBox.Text = (profile.MouseFocusSettleDelayMs ?? _keyDispatchService.FocusSettleDelayMs).ToString();
            _ = ApplyMouseFocusSettleDelayFromSettings(logResult: false);
            _pendingArchbishopProcessId = profile.ArchbishopProcessId;
            _mousePoints.Clear();
            foreach (MousePointProfile point in profile.MousePoints ?? new List<MousePointProfile>())
            {
                _mousePoints.Add(new MousePointDefinition
                {
                    Id = string.IsNullOrWhiteSpace(point.Id) ? Guid.NewGuid().ToString("N") : point.Id!,
                    Name = string.IsNullOrWhiteSpace(point.Name) ? "Point" : point.Name!,
                    XText = string.IsNullOrWhiteSpace(point.XText) ? "0.500" : point.XText!,
                    YText = string.IsNullOrWhiteSpace(point.YText) ? "0.500" : point.YText!,
                });
            }
            EnsureDefaultMousePoints();
            RenderMousePoints();

            _triggers.Clear();
            _triggerRowsHost.Children.Clear();
            _triggerVisuals.Clear();
            _triggerIndicatorByTrigger.Clear();
            _stepVisualsByReason.Clear();
            _stepIndicatorByReason.Clear();

            foreach (TriggerProfile trigger in profile.Triggers ?? new List<TriggerProfile>())
            {
                AddTrigger(
                    trigger.Name ?? string.Empty,
                    trigger.IntervalSecText ?? string.Empty,
                    trigger.TeleportKey ?? "F1",
                    trigger.PostDelayText ?? "300",
                    trigger.IsActive);
                TriggerDefinition created = _triggers[^1];
                created.Members.Clear();

                foreach (MemberProfile member in trigger.Members ?? new List<MemberProfile>())
                {
                    var mappedMember = new MemberDefinition
                    {
                        IsActive = member.IsActive,
                        Label = member.Label ?? string.Empty,
                        ProcessIdText = member.ProcessIdText ?? string.Empty,
                        Steps = member.Steps.Select(s => new ActionStepDefinition
                        {
                            StepType = NormalizeStepType(s.StepType),
                            KeyText = s.KeyText ?? string.Empty,
                            MousePointId = s.MousePointId ?? string.Empty,
                            DelayText = string.IsNullOrWhiteSpace(s.DelayText) ? "250" : s.DelayText,
                        }).ToList(),
                    };

                    if (mappedMember.Steps.Count == 0)
                    {
                        // Backward compatibility for old key-only profiles.
                        foreach (KeyStepProfile old in member.KeySteps)
                        {
                            mappedMember.Steps.Add(new ActionStepDefinition
                            {
                                StepType = "KEY",
                                KeyText = old.KeyText ?? string.Empty,
                                DelayText = string.IsNullOrWhiteSpace(old.DelayText) ? "250" : old.DelayText!,
                            });
                        }
                    }

                    if (mappedMember.Steps.Count == 0)
                    {
                        mappedMember.Steps.Add(new ActionStepDefinition { StepType = "KEY", KeyText = string.Empty, DelayText = "250" });
                    }

                    created.Members.Add(mappedMember);
                }
            }

            if (_triggers.Count == 0)
            {
                AddTrigger("Buff60", "60", "F1", "300");
            }

            SelectTrigger(_triggers.FirstOrDefault());
            RenderMembers();

            if (logResult)
            {
                AppendLog($"Profile '{profileName}' loaded.");
            }

            return true;
        }
        catch (Exception ex)
        {
            if (logResult)
            {
                AppendLog($"Failed to load profile '{profileName}': {ex.Message}");
            }

            return false;
        }
    }

    private sealed class TriggerDefinition
    {
        public required bool IsActive { get; set; }
        public required string Name { get; set; }
        public required string IntervalSecText { get; set; }
        public required string TeleportKey { get; set; }
        public required string PostDelayText { get; set; }
        public required List<MemberDefinition> Members { get; set; }
    }

    private sealed class MemberDefinition
    {
        public required bool IsActive { get; set; }
        public required string Label { get; set; }
        public required string ProcessIdText { get; set; }
        public required List<ActionStepDefinition> Steps { get; set; }
    }

    private sealed class ActionStepDefinition
    {
        public required string StepType { get; set; }
        public required string KeyText { get; set; }
        public string MousePointId { get; set; } = string.Empty;
        public required string DelayText { get; set; }
    }

    private sealed class MousePointDefinition
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string XText { get; set; }
        public required string YText { get; set; }
    }

    private sealed class SupportedServerDto
    {
        public string name { get; set; } = string.Empty;
        public string? hpAddress { get; set; }
        public string? nameAddress { get; set; }
        public int hpAddressPointer { get; set; }
        public int nameAddressPointer { get; set; }
    }

    private sealed class AppProfile
    {
        public string? ServerProcessName { get; set; }
        public string? HpAddressHex { get; set; }
        public string? NameAddressHex { get; set; }
        public string? ToggleHotkey { get; set; }
        public bool EnableAudioCue { get; set; } = true;
        public int? MouseFocusSettleDelayMs { get; set; }
        public int? ArchbishopProcessId { get; set; }
        public List<MousePointProfile> MousePoints { get; set; } = new();
        public List<TriggerProfile> Triggers { get; set; } = new();
    }

    private sealed class TriggerProfile
    {
        public bool IsActive { get; set; } = true;
        public string? Name { get; set; }
        public string? IntervalSecText { get; set; }
        public string? TeleportKey { get; set; }
        public string? PostDelayText { get; set; }
        public List<MemberProfile> Members { get; set; } = new();
    }

    private sealed class MemberProfile
    {
        public bool IsActive { get; set; } = true;
        public string? Label { get; set; }
        public string? ProcessIdText { get; set; }
        public List<StepProfile> Steps { get; set; } = new();
        public List<KeyStepProfile> KeySteps { get; set; } = new();
    }

    private sealed class StepProfile
    {
        public string? StepType { get; set; }
        public string? KeyText { get; set; }
        public string? MousePointId { get; set; }
        public string? DelayText { get; set; }
    }

    private sealed class KeyStepProfile
    {
        public string? KeyText { get; set; }
        public string? DelayText { get; set; }
    }

    private sealed class MousePointProfile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? XText { get; set; }
        public string? YText { get; set; }
    }
}
