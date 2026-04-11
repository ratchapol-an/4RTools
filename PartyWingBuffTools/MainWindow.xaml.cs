using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PartyWingBuffTools.Core.Models;
using PartyWingBuffTools.Core.Services;
using PartyWingBuffTools.Services;

namespace PartyWingBuffTools;

public sealed partial class MainWindow : Window
{
    private const string DefaultExecutableName = "Ragexe";
    private const string DefaultHpAddressHex = "0146F28C";
    private const string DefaultNameAddressHex = "01471CD8";
    private const string DefaultProfileName = "Default";
    private const int MaxDisplayedLogLines = 50;

    private readonly RagnarokProcessService _processService = new();
    private readonly KeyDispatchService _keyDispatchService = new();
    private readonly PartyBuffScheduler _scheduler = new();
    private readonly List<RagnarokProcessInfo> _availableProcesses = new();
    private readonly List<TriggerDefinition> _triggers = new();
    private readonly Queue<DispatchPlan> _triggerQueue = new();
    private readonly object _triggerQueueLock = new();
    private readonly List<SupportedServerEntry> _supportedServers = new();
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

    private ComboBox _archbishopProcessComboBox = null!;
    private TextBlock _archbishopCharacterText = null!;
    private TextBox _logTextBox = null!;
    private TextBlock _statusText = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private StackPanel _triggerRowsHost = null!;
    private StackPanel _memberRowsHost = null!;
    private TextBlock _memberEditorTitle = null!;
    private Grid _runPanel = null!;
    private StackPanel _settingsPanel = null!;
    private StackPanel _logsPanel = null!;
    private StackPanel _profilesPanel = null!;
    private TextBox _serverProcessNameTextBox = null!;
    private TextBox _hpAddressTextBox = null!;
    private TextBox _nameAddressTextBox = null!;
    private ComboBox _profileComboBox = null!;
    private TextBox _profileNameTextBox = null!;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        BuildUi();
        EnsureDefaultProfileExists();
        RefreshProfileList(selectProfileName: DefaultProfileName);
        LoadProfile(_currentProfileName, logResult: false);
        LoadSupportedProcessNames();
        RefreshProcesses();
        _logWriterTask = Task.Run(() => RunLogWriterAsync(_logWriterCts.Token));
        Closed += (_, _) =>
        {
            SaveProfile(_currentProfileName, logResult: false);
            StopLogWriter();
        };
    }

    private void BuildUi()
    {
        var host = new Grid { ColumnSpacing = 12 };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftNav = new Grid { Padding = new Thickness(8, 4, 8, 8) };
        leftNav.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftNav.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftNav.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var navBorder = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            Child = leftNav,
        };

        var navTitle = new TextBlock
        {
            Text = "PartyWingBuffTools",
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Margin = new Thickness(8, 8, 8, 14),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
        };
        Grid.SetRow(navTitle, 0);
        leftNav.Children.Add(navTitle);

        var navItems = new StackPanel { Spacing = 8 };
        var runNavButton = new Button { Content = "⚔ Party Trigger", HorizontalAlignment = HorizontalAlignment.Stretch };
        var settingsNavButton = new Button { Content = "⚙ Address Settings", HorizontalAlignment = HorizontalAlignment.Stretch };
        var profilesNavButton = new Button { Content = "👤 Profiles", HorizontalAlignment = HorizontalAlignment.Stretch };
        var logsNavButton = new Button { Content = "📝 Logs", HorizontalAlignment = HorizontalAlignment.Stretch };
        runNavButton.Click += (_, _) => ShowPanel("run");
        settingsNavButton.Click += (_, _) => ShowPanel("settings");
        profilesNavButton.Click += (_, _) => ShowPanel("profiles");
        logsNavButton.Click += (_, _) => ShowPanel("logs");
        navItems.Children.Add(runNavButton);
        navItems.Children.Add(settingsNavButton);
        navItems.Children.Add(profilesNavButton);
        navItems.Children.Add(logsNavButton);
        Grid.SetRow(navItems, 1);
        leftNav.Children.Add(navItems);

        var navFooter = new TextBlock
        {
            Text = "Profiles and logs are on left menu",
            Margin = new Thickness(8),
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        Grid.SetRow(navFooter, 2);
        leftNav.Children.Add(navFooter);

        Grid.SetColumn(navBorder, 0);
        host.Children.Add(navBorder);

        var rightRoot = new Grid { RowSpacing = 10 };
        rightRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var topActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 6) };
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
        _startButton = new Button { Content = "▶ Start" };
        _startButton.Click += async (_, _) => await StartSchedulerAsync();
        _stopButton = new Button { Content = "■ Stop", IsEnabled = false };
        _stopButton.Click += (_, _) => _runCts?.Cancel();
        _statusText = new TextBlock { Text = "Status: Idle", VerticalAlignment = VerticalAlignment.Center };
        topActions.Children.Add(refreshButton);
        topActions.Children.Add(new TextBlock { Text = "Profile:", VerticalAlignment = VerticalAlignment.Center });
        topActions.Children.Add(_profileComboBox);
        topActions.Children.Add(saveProfileButton);
        topActions.Children.Add(_startButton);
        topActions.Children.Add(_stopButton);
        topActions.Children.Add(_statusText);
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

        Grid.SetColumn(rightRoot, 1);
        host.Children.Add(rightRoot);

        ContentHost.Children.Add(host);

        AddTrigger("Buff60", "60", "F1", "300");
        AddTrigger("Buff180", "180", "F1", "300");
        SelectTrigger(_triggers.FirstOrDefault());
        ShowPanel("run");

        _archbishopProcessComboBox.SelectionChanged += (_, _) => UpdateArchbishopCharacterPreview();
    }

    private Grid BuildRunPanel()
    {
        var runGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        runGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        runGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        runGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        runGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        runGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftTop = new StackPanel { Spacing = 8 };
        leftTop.Children.Add(new TextBlock { Text = "👑 Archbishop Process", FontWeight = FontWeights.Bold, FontSize = 16 });
        _archbishopProcessComboBox = new ComboBox { DisplayMemberPath = "DisplayName", SelectedValuePath = "ProcessId" };
        _archbishopCharacterText = new TextBlock { Text = "Character: -" };
        leftTop.Children.Add(_archbishopProcessComboBox);
        Grid.SetRow(leftTop, 0);
        Grid.SetColumn(leftTop, 0);
        runGrid.Children.Add(leftTop);

        var rightTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        rightTop.Children.Add(new TextBlock
        {
            Text = "Set key press and delay in separate member fields.",
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(rightTop, 0);
        Grid.SetColumn(rightTop, 1);
        runGrid.Children.Add(rightTop);

        FrameworkElement triggerEditor = BuildTriggerEditor();
        Grid.SetRow(triggerEditor, 1);
        Grid.SetColumn(triggerEditor, 0);
        Grid.SetColumnSpan(triggerEditor, 2);
        runGrid.Children.Add(triggerEditor);

        FrameworkElement memberEditor = BuildMemberEditor();
        Grid.SetRow(memberEditor, 2);
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
        settings.Children.Add(new TextBlock { Text = "🎯 Ragnarok Executable Name", FontWeight = FontWeights.Bold, FontSize = 15 });
        settings.Children.Add(_serverProcessNameTextBox);
        settings.Children.Add(new TextBlock { Text = "❤ HP Address (hex)", FontWeight = FontWeights.SemiBold });
        settings.Children.Add(_hpAddressTextBox);
        settings.Children.Add(new TextBlock { Text = "🏷 Name Address (hex)", FontWeight = FontWeights.SemiBold });
        settings.Children.Add(_nameAddressTextBox);
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
        panel.Children.Add(new TextBlock
        {
            Text = "Notes: Default profile cannot be renamed/deleted. Save button on top bar saves current profile.",
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
        title.Children.Add(new TextBlock { Text = "🔁 Trigger Sequences", FontWeight = FontWeights.Bold, FontSize = 15 });
        var addButton = new Button { Content = "+ Add Trigger" };
        addButton.Click += (_, _) => AddTrigger();
        title.Children.Add(addButton);
        wrapper.Children.Add(title);

        var header = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(HeaderText("Active", 0));
        header.Children.Add(HeaderText("Name", 1));
        header.Children.Add(HeaderText("Interval(s)", 2));
        header.Children.Add(HeaderText("Teleport", 3));
        header.Children.Add(HeaderText("Post Delay(ms)", 4));
        header.Children.Add(HeaderText("Members", 5));
        header.Children.Add(HeaderText("Remove", 6));
        Grid.SetRow(header, 1);
        wrapper.Children.Add(header);

        _triggerRowsHost = new StackPanel { Spacing = 6 };
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
        _memberEditorTitle = new TextBlock { Text = "👥 Members for trigger: -", FontWeight = FontWeights.Bold, FontSize = 15 };
        title.Children.Add(_memberEditorTitle);
        var addButton = new Button { Content = "+ Add Member" };
        addButton.Click += (_, _) => AddMemberToSelectedTrigger();
        title.Children.Add(addButton);
        title.Children.Add(new TextBlock
        {
            Text = "Add key-step rows per member. Each row has a key capture box and delay (ms).",
            VerticalAlignment = VerticalAlignment.Center,
        });
        wrapper.Children.Add(title);

        var header = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.8, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(HeaderText("Active", 0));
        header.Children.Add(HeaderText("Role/Label", 1));
        header.Children.Add(HeaderText("Process", 2));
        header.Children.Add(HeaderText("Key Steps", 3));
        header.Children.Add(HeaderText("Remove", 4));
        Grid.SetRow(header, 1);
        wrapper.Children.Add(header);

        _memberRowsHost = new StackPanel { Spacing = 6 };
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

        var rowGrid = new Grid { ColumnSpacing = 6 };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var activeCheck = new CheckBox { IsChecked = isActive, VerticalAlignment = VerticalAlignment.Center };
        var nameBox = new TextBox { Text = name, PlaceholderText = "Buff60" };
        var intervalBox = new TextBox { Text = interval, PlaceholderText = "60" };
        var teleportBox = new TextBox { Text = teleport, PlaceholderText = "F1", IsReadOnly = true };
        var delayBox = new TextBox { Text = postDelay, PlaceholderText = "300" };
        var membersButton = new Button { Content = "Edit" };
        var removeButton = new Button { Content = "X" };

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
            _triggerRowsHost.Children.Remove(rowGrid);
            if (_selectedTrigger == trigger)
            {
                SelectTrigger(_triggers.FirstOrDefault());
            }
        };

        Grid.SetColumn(activeCheck, 0);
        Grid.SetColumn(nameBox, 1);
        Grid.SetColumn(intervalBox, 2);
        Grid.SetColumn(teleportBox, 3);
        Grid.SetColumn(delayBox, 4);
        Grid.SetColumn(membersButton, 5);
        Grid.SetColumn(removeButton, 6);
        rowGrid.Children.Add(activeCheck);
        rowGrid.Children.Add(nameBox);
        rowGrid.Children.Add(intervalBox);
        rowGrid.Children.Add(teleportBox);
        rowGrid.Children.Add(delayBox);
        rowGrid.Children.Add(membersButton);
        rowGrid.Children.Add(removeButton);

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
            KeySteps = new List<KeyStepDefinition>
            {
                new() { KeyText = string.Empty, DelayText = "250" },
            },
        });
        RenderMembers();
    }

    private void RenderMembers()
    {
        _memberRowsHost.Children.Clear();
        if (_selectedTrigger is null)
        {
            _memberEditorTitle.Text = "Members for trigger: -";
            return;
        }

        _memberEditorTitle.Text = $"Members for trigger: {_selectedTrigger.Name}";
        foreach (MemberDefinition member in _selectedTrigger.Members)
        {
            var rowGrid = new Grid { ColumnSpacing = 6 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.8, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var activeCheck = new CheckBox { IsChecked = member.IsActive, VerticalAlignment = VerticalAlignment.Center };
            var labelBox = new TextBox { Text = member.Label, PlaceholderText = "Bard" };
            var processCombo = new ComboBox { ItemsSource = _availableProcesses, DisplayMemberPath = "DisplayName", SelectedValuePath = "ProcessId" };
            if (int.TryParse(member.ProcessIdText, out int pid))
            {
                processCombo.SelectedValue = pid;
            }
            var removeButton = new Button { Content = "X" };
            var stepsHost = new StackPanel { Spacing = 4 };
            var addStepButton = new Button { Content = "+ Step", HorizontalAlignment = HorizontalAlignment.Left };

            if (member.KeySteps.Count == 0)
            {
                member.KeySteps.Add(new KeyStepDefinition { KeyText = string.Empty, DelayText = "250" });
            }

            foreach (KeyStepDefinition step in member.KeySteps.ToList())
            {
                var stepGrid = new Grid { ColumnSpacing = 4 };
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var stepKeyBox = new TextBox { Text = step.KeyText, PlaceholderText = "Press key", IsReadOnly = true };
                var stepDelayBox = new TextBox { Text = step.DelayText, PlaceholderText = "250" };
                var removeStepButton = new Button { Content = "-" };

                stepKeyBox.TextChanged += (_, _) => step.KeyText = stepKeyBox.Text;
                stepDelayBox.TextChanged += (_, _) => step.DelayText = stepDelayBox.Text;
                stepKeyBox.KeyDown += (_, e) =>
                {
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
                    member.KeySteps.Remove(step);
                    RenderMembers();
                };

                Grid.SetColumn(stepKeyBox, 0);
                Grid.SetColumn(stepDelayBox, 1);
                Grid.SetColumn(removeStepButton, 2);
                stepGrid.Children.Add(stepKeyBox);
                stepGrid.Children.Add(stepDelayBox);
                stepGrid.Children.Add(removeStepButton);
                stepsHost.Children.Add(stepGrid);
            }

            addStepButton.Click += (_, _) =>
            {
                member.KeySteps.Add(new KeyStepDefinition { KeyText = string.Empty, DelayText = "250" });
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
            _runCts?.Dispose();
            _runCts = null;
            _startButton.IsEnabled = true;
            _stopButton.IsEnabled = false;
            _statusText.Text = "Status: Idle";
            AppendLog("Scheduler stopped.");
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

                List<KeyStepConfig> steps = new();
                foreach (KeyStepDefinition step in member.KeySteps)
                {
                    string keyToken = step.KeyText.Trim();
                    if (string.IsNullOrWhiteSpace(keyToken))
                    {
                        continue;
                    }

                    int delayMs = 250;
                    if (int.TryParse(step.DelayText.Trim(), out int parsedDelay))
                    {
                        delayMs = Math.Max(0, parsedDelay);
                    }

                    steps.Add(new KeyStepConfig
                    {
                        Key = keyToken.ToUpperInvariant(),
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
                    KeySequence = steps,
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

        // Execute Archbishop teleport first (if present), then run members in parallel.
        DispatchAction? teleportAction = plan.Actions.FirstOrDefault(a => a.Reason.EndsWith(":Teleport", StringComparison.OrdinalIgnoreCase));
        if (teleportAction is not null)
        {
            bool sent = _keyDispatchService.SendKey(teleportAction.ProcessId, teleportAction.Key);
            AppendLog(sent
                ? $"Key {teleportAction.Key} -> {teleportAction.ProcessId} ({teleportAction.Reason})"
                : $"Failed {teleportAction.Key} -> {teleportAction.ProcessId} ({teleportAction.Reason})");

            if (teleportAction.DelayAfterMs > 0)
            {
                await Task.Delay(teleportAction.DelayAfterMs, ct);
            }
        }

        var memberSequences = plan.Actions
            .Where(a => !a.Reason.EndsWith(":Teleport", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => (a.ProcessId, a.Reason))
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

    private async Task RunMemberSequenceAsync(IReadOnlyList<DispatchAction> sequence, CancellationToken ct)
    {
        foreach (DispatchAction action in sequence)
        {
            bool sent = _keyDispatchService.SendKey(action.ProcessId, action.Key);
            AppendLog(sent
                ? $"Key {action.Key} -> {action.ProcessId} ({action.Reason})"
                : $"Failed {action.Key} -> {action.ProcessId} ({action.Reason})");

            if (action.DelayAfterMs > 0)
            {
                await Task.Delay(action.DelayAfterMs, ct);
            }
        }
    }

    private bool TryGetAddresses(out int hpAddress, out int nameAddress)
    {
        hpAddress = 0;
        nameAddress = 0;
        bool hasHp = int.TryParse(_hpAddressTextBox.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out hpAddress);
        bool hasName = int.TryParse(_nameAddressTextBox.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out nameAddress);
        return hasHp && hasName;
    }

    private void ShowPanel(string panel)
    {
        _runPanel.Visibility = panel == "run" ? Visibility.Visible : Visibility.Collapsed;
        _settingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        _profilesPanel.Visibility = panel == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        _logsPanel.Visibility = panel == "logs" ? Visibility.Visible : Visibility.Collapsed;
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
            Windows.System.VirtualKey.Tab => "TAB",
            Windows.System.VirtualKey.Space => "SPACE",
            _ => key.ToString().ToUpperInvariant(),
        };
    }

    private TextBlock HeaderText(string text, int col)
    {
        var tb = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(tb, col);
        return tb;
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

    private string GetProfileDirectoryPath()
    {
        return Path.Combine(GetAppDataRootPath(), "profiles");
    }

    private string GetLegacyProfileDirectoryPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Profiles");
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

    private void TryMigrateLegacyProfiles()
    {
        string sourceDir = GetLegacyProfileDirectoryPath();
        string targetDir = GetProfileDirectoryPath();

        try
        {
            if (!Directory.Exists(sourceDir))
            {
                return;
            }

            Directory.CreateDirectory(targetDir);
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
        catch
        {
            // best-effort migration only
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
                ArchbishopProcessId = _archbishopProcessComboBox.SelectedValue as int?,
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
                        KeySteps = m.KeySteps.Select(s => new KeyStepProfile
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
            _pendingArchbishopProcessId = profile.ArchbishopProcessId;

            _triggers.Clear();
            _triggerRowsHost.Children.Clear();

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
                        KeySteps = member.KeySteps.Select(s => new KeyStepDefinition
                        {
                            KeyText = s.KeyText ?? string.Empty,
                            DelayText = string.IsNullOrWhiteSpace(s.DelayText) ? "250" : s.DelayText,
                        }).ToList(),
                    };

                    if (mappedMember.KeySteps.Count == 0)
                    {
                        mappedMember.KeySteps.Add(new KeyStepDefinition { KeyText = string.Empty, DelayText = "250" });
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
        public required List<KeyStepDefinition> KeySteps { get; set; }
    }

    private sealed class KeyStepDefinition
    {
        public required string KeyText { get; set; }
        public required string DelayText { get; set; }
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
        public int? ArchbishopProcessId { get; set; }
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
        public List<KeyStepProfile> KeySteps { get; set; } = new();
    }

    private sealed class KeyStepProfile
    {
        public string? KeyText { get; set; }
        public string? DelayText { get; set; }
    }
}
