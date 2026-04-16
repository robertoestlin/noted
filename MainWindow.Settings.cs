using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private List<UserProfile> NormalizeUsers(IEnumerable<UserProfile>? users)
        => _userProfileService.NormalizeUsers(users);

    private List<UserProfile> BuildUsersFromLegacyNames(IEnumerable<string>? userNames)
        => _userProfileService.BuildUsersFromLegacyNames(userNames);

    private Color RandomUserColor()
        => _userProfileService.RandomUserColor();

    private Color GetUserColor(string person)
        => _userProfileService.ResolveUserColor(_users, person);

    // -- Window settings ------------------------------------------------------------------

    private void SaveWindowSettings()
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            Directory.CreateDirectory(_backupFolder);
            var state = CreateWindowSettingsSnapshot();
            _windowSettingsService.SaveWithBootstrap(
                _windowSettingsStore,
                state,
                _backupFolder,
                DefaultBackupFolder(),
                SettingsFileName,
                opts);
            SaveTimeReports(opts);
            SaveSearchFilesHistory(opts);
            SaveTodoItems(opts);
        }
        catch { /* non-critical */ }
    }

    private WindowSettings CreateWindowSettingsSnapshot()
        => new()
        {
            Left = WindowState == WindowState.Normal ? Left : RestoreBounds.Left,
            Top = WindowState == WindowState.Normal ? Top : RestoreBounds.Top,
            Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
            Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height,
            Maximized = WindowState == WindowState.Maximized,
            AutoSaveSeconds = (int)_autoSaveTimer.Interval.TotalSeconds,
            InitialLines = _initialLines,
            FontFamily = _fontFamily,
            FontSize = _fontSize,
            FontWeight = _fontWeight,
            ShortcutNewPrimary = _shortcutNewPrimary,
            ShortcutNewSecondary = _shortcutNewSecondary,
            ShortcutCloseTab = _shortcutCloseTab,
            ShortcutRenameTab = _shortcutRenameTab,
            ShortcutAddBlankLines = _shortcutAddBlankLines,
            ShortcutTrimTrailingEmptyLines = _shortcutTrimTrailingEmptyLines,
            ShortcutToggleHighlight = _shortcutToggleHighlight,
            ShortcutGoToLine = _shortcutGoToLine,
            ShortcutGoToTab = _shortcutGoToTab,
            SelectedLineColor = ColorToHex(_selectedLineColor),
            HighlightedLineColor = ColorToHex(_highlightedLineColor),
            SelectedHighlightedLineColor = ColorToHex(_selectedHighlightedLineColor),
            BackupFolder = _backupFolder,
            CloudBackupFolder = _cloudBackupFolder,
            CloudSaveHours = _cloudSaveIntervalHours,
            CloudSaveMinutes = _cloudSaveIntervalMinutes,
            LastCloudCopyUtc = _lastCloudSaveUtc == DateTime.MinValue ? null : _lastCloudSaveUtc,
            ActiveTabIndex = MainTabControl.SelectedIndex,
            FridayFeelingEnabled = _isFridayFeelingEnabled,
            FancyBulletsEnabled = _fancyBulletsEnabled,
            WrapLongLinesVisually = _wrapLongLinesVisually,
            VisualLineWrapColumn = _visualLineWrapColumn,
            ShowSmileys = _showSmileys,
            ShowHorizontalRuler = _showHorizontalRuler,
            ShowInlineImages = _showInlineImages,
            FancyBulletStyle = FancyBulletStyleToSetting(_fancyBulletStyle),
            UptimeHeartbeatSeconds = _uptimeHeartbeatSeconds,
            Users = _users.Select(user => user.Name).ToList(),
            UserProfiles = NormalizeUsers(_users),
            PluginAlarms = BuildPluginAlarmsSnapshot(),
            PluginAlarmsEnabled = _pluginAlarmsEnabled,
            AlarmPopupLeft = _alarmPopupLeft,
            AlarmPopupTop = _alarmPopupTop,
            ProjectLineCounterProjects = BuildProjectLineCounterProjectsSnapshot(),
            ProjectLineCounterTypes = BuildProjectLineCounterTypesSnapshot(),
            ProjectLineCounterAutoDetectedFileTypes = BuildProjectLineCounterAutoDetectedFileTypesSnapshot(),
            ProjectLineCounterIgnoredFileTypes = BuildProjectLineCounterIgnoredFileTypesSnapshot(),
            ProjectLineCounterIgnoredFolders = BuildProjectLineCounterIgnoredFoldersSnapshot(),
            SearchFilesHistoryLimit = _searchFilesHistoryLimit,
            TabCleanupStaleDays = _tabCleanupStaleDays,
            QuickMessagePresets = BuildQuickMessagePresetsSnapshot(),
            QuickMessageColor = _quickMessageColorHex,
            QuickMessageCustom = _quickMessageCustom
        };

    private void LoadWindowSettings()
    {
        try
        {
            ResetSettingsToDefaults();
            var loaded = _windowSettingsService.LoadWithFallback(
                _windowSettingsStore,
                DefaultBackupFolder(),
                DefaultCloudBackupFolder(),
                SettingsFileName);
            if (loaded == null)
                return;

            ApplyBootstrapSettings(loaded.BootstrapBackupFolder, loaded.BootstrapCloudBackupFolder, loaded.BootstrapSettings);
            ApplyEffectiveWindowSettings(loaded.EffectiveSettings);
            LoadTimeReports();
            LoadSearchFilesHistory();
            LoadTodoItems();
            if (_lastCloudSaveUtc == DateTime.MinValue)
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
            ApplyColorThemeToOpenEditors();
            ApplyFridayFeelingToOpenEditors();
        }
        catch { /* ignore corrupt settings */ }
    }

    private void SaveSearchFilesHistory(JsonSerializerOptions options)
    {
        var historyPath = Path.Combine(_backupFolder, SearchFilesHistoryFileName);
        _windowSettingsStore.Save(historyPath, BuildSearchFilesHistorySnapshot(), options);
    }

    private void SaveTimeReports(JsonSerializerOptions options)
    {
        var timeReportsPath = Path.Combine(_backupFolder, TimeReportsFileName);
        _windowSettingsStore.Save(timeReportsPath, BuildTimeReportSettings(), options);
    }

    private void SaveTodoItems(JsonSerializerOptions options)
    {
        var todoItemsPath = Path.Combine(_backupFolder, TodoItemsFileName);
        _windowSettingsStore.Save(todoItemsPath, BuildTodoItemsSnapshot(), options);
    }

    private void LoadSearchFilesHistory()
    {
        var historyPath = Path.Combine(_backupFolder, SearchFilesHistoryFileName);
        var history = _windowSettingsStore.Load<List<SearchFilesHistoryEntry>>(historyPath);
        ApplySearchFilesHistorySettings(history, _searchFilesHistoryLimit);
        if (history == null)
            SaveSearchFilesHistory(new JsonSerializerOptions { WriteIndented = true });
    }

    private void LoadTimeReports()
    {
        var timeReportsPath = Path.Combine(_backupFolder, TimeReportsFileName);
        var records = _windowSettingsStore.Load<List<TimeReportMonthRecord>>(timeReportsPath);
        LoadTimeReportSettings(records);
        if (records == null)
            SaveTimeReports(new JsonSerializerOptions { WriteIndented = true });
    }

    private void LoadTodoItems()
    {
        var todoItemsPath = Path.Combine(_backupFolder, TodoItemsFileName);
        var items = _windowSettingsStore.Load<List<TodoItemState>>(todoItemsPath);
        ApplyTodoItems(items);
        if (items == null)
            SaveTodoItems(new JsonSerializerOptions { WriteIndented = true });
    }

    private void ResetSettingsToDefaults()
    {
        _backupFolder = DefaultBackupFolder();
        _cloudBackupFolder = DefaultCloudBackupFolder();
        _selectedLineColor = DefaultSelectedLineColor;
        _highlightedLineColor = DefaultHighlightedLineColor;
        _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
        _shortcutNewPrimary = DefaultShortcutNewPrimary;
        _shortcutNewSecondary = DefaultShortcutNewSecondary;
        _shortcutCloseTab = DefaultShortcutCloseTab;
        _shortcutRenameTab = DefaultShortcutRenameTab;
        _shortcutAddBlankLines = DefaultShortcutAddBlankLines;
        _shortcutTrimTrailingEmptyLines = DefaultShortcutTrimTrailingEmptyLines;
        _shortcutToggleHighlight = DefaultShortcutToggleHighlight;
        _shortcutGoToLine = DefaultShortcutGoToLine;
        _shortcutGoToTab = DefaultShortcutGoToTab;
        _uptimeHeartbeatSeconds = DefaultUptimeHeartbeatSeconds;
        _isFridayFeelingEnabled = true;
        _fancyBulletsEnabled = false;
        _wrapLongLinesVisually = true;
        _visualLineWrapColumn = DefaultVisualLineWrapColumn;
        _showSmileys = true;
        _showHorizontalRuler = true;
        _showInlineImages = true;
        _fancyBulletStyle = FancyBulletStyle.Dot;
        _isFredagspartySessionEnabled = false;
        _users = [];
        _timeReports.Clear();
        _pluginAlarms = [];
        _pluginAlarmsEnabled = true;
        _alarmPopupLeft = null;
        _alarmPopupTop = null;
        _projectLineCounterProjects = [];
        _projectLineCounterTypes = [];
        _projectLineCounterAutoDetectedFileTypes = [];
        _projectLineCounterIgnoredFileTypes = [];
        _projectLineCounterIgnoredFolders = [];
        _searchFilesHistory = [];
        _searchFilesHistoryLimit = DefaultSearchFilesHistoryLimit;
        _tabCleanupStaleDays = DefaultTabCleanupStaleDays;
        _todoItems.Clear();
        _todoPanelVisible = false;
        ResetQuickMessageOverlaySettings();
    }

    private void ApplyBootstrapSettings(string backupFolder, string cloudBackupFolder, WindowSettings bootstrap)
    {
        _backupFolder = backupFolder;
        _cloudBackupFolder = cloudBackupFolder;
        if (_windowSettingsService.TryGetValidCloudHours(bootstrap.CloudSaveHours, out var cloudHours))
            _cloudSaveIntervalHours = cloudHours;
        if (_windowSettingsService.TryGetValidCloudMinutes(bootstrap.CloudSaveMinutes, out var cloudMinutes))
            _cloudSaveIntervalMinutes = cloudMinutes;
        if (_windowSettingsService.TryGetNormalizedUtc(bootstrap.LastCloudCopyUtc, out var cloudCopyUtc))
            _lastCloudSaveUtc = cloudCopyUtc;
    }

    private void ApplyEffectiveWindowSettings(WindowSettings state)
    {
        Left = state.Left;
        Top = state.Top;
        Width = state.Width;
        Height = state.Height;
        if (state.AutoSaveSeconds > 0)
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(state.AutoSaveSeconds);
        if (state.InitialLines >= 1)
            _initialLines = state.InitialLines;
        if (!string.IsNullOrWhiteSpace(state.FontFamily))
            _fontFamily = state.FontFamily;
        if (state.FontSize >= 6)
            _fontSize = state.FontSize;
        if (state.FontWeight >= 100 && state.FontWeight <= 900)
            _fontWeight = state.FontWeight;

        ApplyShortcutSettings(state);

        _backupFolder = _windowSettingsService.NormalizePathOrFallback(state.BackupFolder, _backupFolder);
        _cloudBackupFolder = _windowSettingsService.NormalizePathOrFallback(state.CloudBackupFolder, _cloudBackupFolder);
        if (_windowSettingsService.TryGetValidCloudHours(state.CloudSaveHours, out var cloudHours))
            _cloudSaveIntervalHours = cloudHours;
        if (_windowSettingsService.TryGetValidCloudMinutes(state.CloudSaveMinutes, out var cloudMinutes))
            _cloudSaveIntervalMinutes = cloudMinutes;
        if (_windowSettingsService.TryGetValidUptimeHeartbeatSeconds(state.UptimeHeartbeatSeconds, out var uptimeHeartbeatSeconds))
            _uptimeHeartbeatSeconds = uptimeHeartbeatSeconds;
        if (_windowSettingsService.TryGetNormalizedUtc(state.LastCloudCopyUtc, out var cloudCopyUtc))
            _lastCloudSaveUtc = cloudCopyUtc;
        if (state.ActiveTabIndex >= 0)
            _activeTabIndex = state.ActiveTabIndex;
        _isFridayFeelingEnabled = state.FridayFeelingEnabled;
        _fancyBulletsEnabled = state.FancyBulletsEnabled;
        _wrapLongLinesVisually = state.WrapLongLinesVisually;
        _visualLineWrapColumn = NormalizeVisualLineWrapColumn(state.VisualLineWrapColumn);
        _showSmileys = state.ShowSmileys;
        _showHorizontalRuler = state.ShowHorizontalRuler;
        _showInlineImages = state.ShowInlineImages;
        _fancyBulletStyle = ParseFancyBulletStyle(state.FancyBulletStyle);
        UpdateViewMenuChecks();

        var loadedUsers = NormalizeUsers(state.UserProfiles);
        if (loadedUsers.Count == 0)
            loadedUsers = BuildUsersFromLegacyNames(state.Users);
        _users = loadedUsers;
        ApplyPluginAlarmSettings(state.PluginAlarms);
        _pluginAlarmsEnabled = state.PluginAlarmsEnabled;
        ApplyProjectLineCounterSettings(
            state.ProjectLineCounterProjects,
            state.ProjectLineCounterTypes,
            state.ProjectLineCounterAutoDetectedFileTypes,
            state.ProjectLineCounterIgnoredFileTypes,
            state.ProjectLineCounterIgnoredFolders);
        _searchFilesHistoryLimit = NormalizeSearchFilesHistoryLimit(state.SearchFilesHistoryLimit);
        if (state.AlarmPopupLeft is double popupLeft
            && !double.IsNaN(popupLeft)
            && !double.IsInfinity(popupLeft))
        {
            _alarmPopupLeft = popupLeft;
        }
        if (state.AlarmPopupTop is double popupTop
            && !double.IsNaN(popupTop)
            && !double.IsInfinity(popupTop))
        {
            _alarmPopupTop = popupTop;
        }
        if (state.TabCleanupStaleDays >= 1 && state.TabCleanupStaleDays <= 3650)
            _tabCleanupStaleDays = state.TabCleanupStaleDays;
        ApplyQuickMessageOverlaySettings(state);

        ApplyThemeColorsFromSettings(state);
        _startMaximized = state.Maximized;
    }

    private void ApplyShortcutSettings(WindowSettings state)
    {
        if (TryParseKeyGesture(state.ShortcutNewPrimary, out _))
            _shortcutNewPrimary = state.ShortcutNewPrimary!.Trim();
        if (string.IsNullOrWhiteSpace(state.ShortcutNewSecondary))
            _shortcutNewSecondary = string.Empty;
        else if (TryParseKeyGesture(state.ShortcutNewSecondary, out _))
            _shortcutNewSecondary = state.ShortcutNewSecondary.Trim();
        if (TryParseKeyGesture(state.ShortcutCloseTab, out _))
            _shortcutCloseTab = state.ShortcutCloseTab!.Trim();
        if (TryParseKeyGesture(state.ShortcutRenameTab, out _))
            _shortcutRenameTab = state.ShortcutRenameTab!.Trim();
        if (TryParseKeyGesture(state.ShortcutAddBlankLines, out _))
            _shortcutAddBlankLines = state.ShortcutAddBlankLines!.Trim();
        if (TryParseKeyGesture(state.ShortcutTrimTrailingEmptyLines, out _))
            _shortcutTrimTrailingEmptyLines = state.ShortcutTrimTrailingEmptyLines!.Trim();
        if (TryParseKeyGesture(state.ShortcutToggleHighlight, out _))
            _shortcutToggleHighlight = state.ShortcutToggleHighlight!.Trim();
        if (TryParseKeyGesture(state.ShortcutGoToLine, out _))
            _shortcutGoToLine = state.ShortcutGoToLine!.Trim();
        if (TryParseKeyGesture(state.ShortcutGoToTab, out _))
            _shortcutGoToTab = state.ShortcutGoToTab!.Trim();
    }

    private void ApplyThemeColorsFromSettings(WindowSettings state)
    {
        if (TryParseColor(state.SelectedLineColor, out var selectedLineColor))
            _selectedLineColor = selectedLineColor;
        if (TryParseColor(state.HighlightedLineColor, out var highlightedLineColor))
            _highlightedLineColor = MigrateHighlightedLineColor(highlightedLineColor);
        if (TryParseColor(state.SelectedHighlightedLineColor, out var selectedHighlightedLineColor))
            _selectedHighlightedLineColor = MigrateSelectedHighlightedLineColor(selectedHighlightedLineColor);
    }

    private Color MigrateHighlightedLineColor(Color color)
        => _colorThemeService.MigrateHighlightedLineColor(color, DefaultHighlightedLineColor);

    private Color MigrateSelectedHighlightedLineColor(Color color)
        => _colorThemeService.MigrateSelectedHighlightedLineColor(color, DefaultSelectedHighlightedLineColor);

    /// <summary>Creates <see cref="SettingsFileName"/> under the current backup folder when missing.</summary>
    private void EnsureSettingsFileExists()
        => _settingsService.EnsureFileExists(_backupFolder, SettingsFileName, SaveWindowSettings);

    private void CopySettingsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, SettingsFileName);

    private void CopyClosedTabsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, ClosedTabsFileName);

    private void CopySearchFilesHistoryFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, SearchFilesHistoryFileName);

    private void CopyTimeReportsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, TimeReportsFileName);

    private void CopyTodoItemsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, TodoItemsFileName);

    private void CopyImageFolderToBackupFolder(string fromFolder, string toFolder)
    {
        try
        {
            var sourceImageFolder = Path.Combine(fromFolder, BackupImagesFolderName);
            if (!Directory.Exists(sourceImageFolder))
                return;

            var destinationImageFolder = Path.Combine(toFolder, BackupImagesFolderName);
            Directory.CreateDirectory(destinationImageFolder);
            foreach (var sourcePath in Directory.GetFiles(sourceImageFolder, "*.png"))
            {
                var targetPath = Path.Combine(destinationImageFolder, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
        }
        catch
        {
            // Best-effort migration.
        }
    }

    // --- Settings dialog ----------------------------------------------------

    private void ShowUsersDialog()
    {
        var users = NormalizeUsers(_users);
        var dlg = new Window
        {
            Title = "Users",
            Width = 520,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Users available for line assignment:",
            Margin = new Thickness(0, 0, 0, 8)
        });

        var list = new ListBox
        {
            Height = 200,
            Margin = new Thickness(0, 0, 0, 10)
        };
        foreach (var user in users)
            list.Items.Add(user);
        content.Children.Add(list);

        var addRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtUser = new TextBox { Margin = new Thickness(0, 0, 8, 0) };
        var btnAdd = new Button { Content = "Add", Width = 80 };
        Grid.SetColumn(txtUser, 0);
        Grid.SetColumn(btnAdd, 1);
        addRow.Children.Add(txtUser);
        addRow.Children.Add(btnAdd);
        content.Children.Add(addRow);

        content.Children.Add(new TextBlock
        {
            Text = "Selected user's color:",
            Margin = new Thickness(0, 0, 0, 6)
        });

        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string[] userColorOptions =
        [
            "LightSkyBlue",
            "LightGreen",
            "Khaki",
            "LightSalmon",
            "Plum",
            "PaleTurquoise",
            "MistyRose",
            "PeachPuff",
            "Lavender",
            "#FF8BD3DD",
            "#FFE0B3FF",
            "#FFFFD58A",
            "#FFC6E8A8"
        ];

        var cmbUserColor = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 8, 0), MinWidth = 200 };
        foreach (var option in userColorOptions)
            cmbUserColor.Items.Add(option);

        var btnApplyColor = new Button { Content = "Apply", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var btnRandomColor = new Button { Content = "Randomize", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var colorPreview = new Border
        {
            Width = 24,
            Height = 24,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent
        };

        Grid.SetColumn(cmbUserColor, 0);
        Grid.SetColumn(btnApplyColor, 1);
        Grid.SetColumn(btnRandomColor, 2);
        Grid.SetColumn(colorPreview, 3);
        colorRow.Children.Add(cmbUserColor);
        colorRow.Children.Add(btnApplyColor);
        colorRow.Children.Add(btnRandomColor);
        colorRow.Children.Add(colorPreview);
        content.Children.Add(colorRow);

        var btnRemove = new Button
        {
            Content = "Remove Selected",
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(btnRemove);

        void RefreshUsers(string? selectedUserName = null)
        {
            users = NormalizeUsers(users);
            list.Items.Clear();
            foreach (var user in users)
                list.Items.Add(user);

            if (!string.IsNullOrWhiteSpace(selectedUserName))
            {
                var selected = users.FirstOrDefault(user => string.Equals(user.Name, selectedUserName, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    list.SelectedItem = selected;
            }
        }

        void UpdateSelectedColorEditor()
        {
            bool hasSelection = list.SelectedItem is UserProfile;
            btnApplyColor.IsEnabled = hasSelection;
            btnRandomColor.IsEnabled = hasSelection;
            if (!hasSelection)
            {
                cmbUserColor.Text = string.Empty;
                colorPreview.Background = Brushes.Transparent;
                return;
            }

            var selected = (UserProfile)list.SelectedItem;
            cmbUserColor.Text = selected.Color;
            if (TryParseColor(selected.Color, out var selectedColor))
                colorPreview.Background = new SolidColorBrush(selectedColor);
            else
                colorPreview.Background = Brushes.Transparent;
        }

        void ApplyColorToSelectedUser(bool randomize)
        {
            if (list.SelectedItem is not UserProfile selectedUser)
                return;

            Color color;
            if (randomize)
            {
                color = RandomUserColor();
            }
            else if (!TryParseColor(cmbUserColor.Text, out color))
            {
                MessageBox.Show("Please enter a valid color name or hex value.", "Users",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            selectedUser.Color = ColorToHex(color);
            RefreshUsers(selectedUser.Name);
            UpdateSelectedColorEditor();
        }

        btnAdd.Click += (_, _) =>
        {
            var name = (txtUser.Text ?? string.Empty).Trim();
            if (name.Length == 0)
                return;

            var existing = users.FirstOrDefault(user => string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                users.Add(new UserProfile { Name = name, Color = ColorToHex(RandomUserColor()) });

            txtUser.SelectAll();
            RefreshUsers(name);
            UpdateSelectedColorEditor();
        };

        txtUser.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                btnAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };

        btnRemove.Click += (_, _) =>
        {
            if (list.SelectedItem is UserProfile selectedUser)
            {
                users.RemoveAll(user => string.Equals(user.Name, selectedUser.Name, StringComparison.OrdinalIgnoreCase));
                RefreshUsers();
                UpdateSelectedColorEditor();
            }
        };

        btnApplyColor.Click += (_, _) => ApplyColorToSelectedUser(randomize: false);
        btnRandomColor.Click += (_, _) => ApplyColorToSelectedUser(randomize: true);
        cmbUserColor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                btnApplyColor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is UserProfile selected)
                txtUser.Text = selected.Name;
            UpdateSelectedColorEditor();
        };

        ok.Click += (_, _) =>
        {
            users = NormalizeUsers(users);
            dlg.DialogResult = true;
        };

        root.Children.Add(content);
        dlg.Content = root;
        dlg.Loaded += (_, _) =>
        {
            txtUser.Focus();
            Keyboard.Focus(txtUser);
            UpdateSelectedColorEditor();
        };

        if (dlg.ShowDialog() != true)
            return;

        _users = NormalizeUsers(users);
        foreach (var doc in _docs.Values)
            RedrawHighlight(doc);
        SaveWindowSettings();
    }
}
