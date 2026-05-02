using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using Noted.Models;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow
{
    /// <summary>Minutes from 1–10 (1-minute steps) where 3600 seconds is evenly divisible by (minutes × 60).</summary>
    private static readonly int[] UptimeHeartbeatIntervalMinutes = [1, 2, 3, 4, 5, 6, 10];

    private static int SnapUptimeHeartbeatToIntervalMinute(int uptimeHeartbeatSeconds)
    {
        foreach (var m in UptimeHeartbeatIntervalMinutes)
        {
            if (uptimeHeartbeatSeconds == m * 60)
                return m;
        }

        return DefaultUptimeHeartbeatSeconds / 60;
    }

    private static List<TaskAreaState> CloneTaskAreas(IEnumerable<TaskAreaState>? source)
    {
        if (source == null)
            return [];
        return source.Select(area => new TaskAreaState
        {
            Id = area.Id,
            Name = area.Name,
            Groups = (area.Groups ?? []).Select(group => new TaskGroupState
            {
                Id = group.Id,
                Name = group.Name,
                ShortcutKey = group.ShortcutKey,
                SortOrder = group.SortOrder,
                CompletedRetentionDays = group.CompletedRetentionDays,
                CompletedRetentionHours = group.CompletedRetentionHours,
                UndoneMarkEnabled = group.UndoneMarkEnabled,
                UndoneMarkDays = group.UndoneMarkDays,
                UndoneMarkHours = group.UndoneMarkHours
            }).ToList()
        }).ToList();
    }

    private string? PromptForText(string title, string label, string initialValue)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 170,
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
        var okBtn = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        var txt = new TextBox { Text = initialValue, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        content.Children.Add(txt);
        root.Children.Add(content);

        string? result = null;
        okBtn.Click += (_, _) =>
        {
            result = txt.Text ?? string.Empty;
            dialog.DialogResult = true;
        };
        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            txt.Focus();
            System.Windows.Input.Keyboard.Focus(txt);
            txt.SelectAll();
        };

        return dialog.ShowDialog() == true ? result : null;
    }

    private void ShowSettingsDialog()
    {
        bool originalFancyBulletsEnabled = _fancyBulletsEnabled;
        bool originalWrapLongLinesVisually = _wrapLongLinesVisually;
        int originalVisualLineWrapColumn = _visualLineWrapColumn;
        bool originalShowHorizontalRuler = _showHorizontalRuler;
        bool originalShowLineAssignments = _showLineAssignments;
        bool originalShowBulletHoverTooltips = _showBulletHoverTooltips;
        bool originalShowInlineImages = _showInlineImages;
        var originalExternalBrowserForLinks = _externalBrowserForLinks;
        var originalFancyBulletStyle = _fancyBulletStyle;
        bool originalFredagspartySessionEnabled = _isFredagspartySessionEnabled;
        bool originalFredagspartyTemporarilyDisabled = _isFredagspartyTemporarilyDisabled;
        bool viewPreviewCommitted = false;

        var dlg = new Window
        {
            Title = "Settings",
            Width = 840,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var tabControl = new TabControl { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(tabControl, Dock.Top);

        var backupPanel = new StackPanel { Margin = new Thickness(12) };
        backupPanel.Children.Add(new TextBlock { Text = "Backup folder:" });
        var backupRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        backupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        backupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtBackup = new TextBox { Text = _backupFolder, Margin = new Thickness(0, 0, 8, 0) };
        var btnBrowseBackup = new Button { Content = "Browse...", Padding = new Thickness(10, 2, 10, 2) };
        Grid.SetColumn(txtBackup, 0);
        Grid.SetColumn(btnBrowseBackup, 1);
        backupRow.Children.Add(txtBackup);
        backupRow.Children.Add(btnBrowseBackup);
        backupPanel.Children.Add(backupRow);
        backupPanel.Children.Add(new TextBlock { Text = "Image folder:" });
        var txtImageFolder = new TextBox
        {
            IsReadOnly = true,
            Margin = new Thickness(0, 4, 0, 10),
            Background = Brushes.WhiteSmoke
        };
        backupPanel.Children.Add(txtImageFolder);

        void RefreshImageFolderPreview()
        {
            var backupText = (txtBackup.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(backupText))
            {
                txtImageFolder.Text = string.Empty;
                return;
            }

            try
            {
                txtImageFolder.Text = Path.Combine(Path.GetFullPath(backupText), BackupImagesFolderName);
            }
            catch
            {
                txtImageFolder.Text = Path.Combine(backupText, BackupImagesFolderName);
            }
        }
        RefreshImageFolderPreview();
        txtBackup.TextChanged += (_, _) => RefreshImageFolderPreview();

        backupPanel.Children.Add(new TextBlock { Text = "Cloud storage folder:" });
        var cloudRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        cloudRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cloudRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtCloudBackup = new TextBox { Text = _cloudBackupFolder, Margin = new Thickness(0, 0, 8, 0) };
        var btnBrowseCloudBackup = new Button { Content = "Browse...", Padding = new Thickness(10, 2, 10, 2) };
        Grid.SetColumn(txtCloudBackup, 0);
        Grid.SetColumn(btnBrowseCloudBackup, 1);
        cloudRow.Children.Add(txtCloudBackup);
        cloudRow.Children.Add(btnBrowseCloudBackup);
        backupPanel.Children.Add(cloudRow);

        // Plain text tab sync controls live on the Sync tab (added below); they are declared here
        // because the Backup tab's "Save now" button references them when triggering a manual cloud save.
        var chkCloudPlainTabs = new CheckBox
        {
            Content = "During cloud save, also sync each tab as a plain text file",
            IsChecked = _cloudSyncTabsPlainTextEnabled,
            Margin = new Thickness(0, 2, 0, 4)
        };
        var txtCloudPlainTabs = new TextBox
        {
            Text = _cloudSyncTabsPlainTextFolder,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = _cloudSyncTabsPlainTextEnabled
        };
        var btnBrowseCloudPlainTabs = new Button
        {
            Content = "Browse...",
            Padding = new Thickness(10, 2, 10, 2),
            IsEnabled = _cloudSyncTabsPlainTextEnabled
        };

        backupPanel.Children.Add(new TextBlock { Text = "Cloud save interval:" });
        var cloudIntervalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var cmbCloudHours = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        for (int h = 0; h <= 50; h++) cmbCloudHours.Items.Add(h);
        cmbCloudHours.SelectedItem = _cloudSaveIntervalHours;
        if (cmbCloudHours.SelectedItem == null) cmbCloudHours.SelectedItem = 0;
        var cmbCloudMinutes = new ComboBox { Width = 80, Margin = new Thickness(8, 0, 8, 0) };
        foreach (var m in CloudMinuteOptions) cmbCloudMinutes.Items.Add(m);
        cmbCloudMinutes.SelectedItem = _cloudSaveIntervalMinutes;
        if (cmbCloudMinutes.SelectedItem == null) cmbCloudMinutes.SelectedItem = 0;
        cloudIntervalRow.Children.Add(cmbCloudHours);
        cloudIntervalRow.Children.Add(new TextBlock { Text = "hours", VerticalAlignment = VerticalAlignment.Center });
        cloudIntervalRow.Children.Add(cmbCloudMinutes);
        cloudIntervalRow.Children.Add(new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center });
        backupPanel.Children.Add(cloudIntervalRow);
        var txtLastCloudCopy = new TextBlock
        {
            Text = $"Last cloud copy: {FormatCloudCopyTimestamp(_lastCloudSaveUtc)}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6)
        };
        backupPanel.Children.Add(txtLastCloudCopy);

        var btnCloudSaveNow = new Button
        {
            Content = "Save now",
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        btnCloudSaveNow.Click += (_, _) =>
        {
            var cloudBackupPath = (txtCloudBackup.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cloudBackupPath))
            {
                MessageBox.Show("Set a cloud storage folder first.", "Cloud save",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var plainEnabled = chkCloudPlainTabs.IsChecked == true;
            var plainFolder = (txtCloudPlainTabs.Text ?? string.Empty).Trim();
            if (plainEnabled && string.IsNullOrWhiteSpace(plainFolder))
            {
                MessageBox.Show("Set a plain text tabs folder or turn off plain text tab sync.", "Cloud save",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveSession(
                updateStatus: true,
                forceCloudBackup: true,
                cloudBackupFolderOverride: cloudBackupPath,
                persistCloudMetadata: false,
                cloudPlainTabsEnabledOverride: plainEnabled,
                cloudPlainTabsFolderOverride: plainFolder);
            txtLastCloudCopy.Text = $"Last cloud copy: {FormatCloudCopyTimestamp(_lastCloudSaveUtc)}";
            RefreshGlobalDirtyStatus();
        };
        backupPanel.Children.Add(btnCloudSaveNow);

        var expanderAdditionalBackup = new Expander
        {
            Header = "Backup additional files",
            IsExpanded = false,
            Margin = new Thickness(0, 4, 0, 10)
        };
        var additionalBackupPanel = new StackPanel { Margin = new Thickness(8, 4, 0, 4) };
        var chkBackupAddSettings = new CheckBox { Content = "Settings file (settings.json)", IsChecked = _backupAdditionalIncludeSettingsFile, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddLog = new CheckBox { Content = "Log file (noted.log)", IsChecked = _backupAdditionalIncludeAppLog, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddHeartbeat = new CheckBox { Content = "Heartbeat (uptime-heartbeat-*.log)", IsChecked = _backupAdditionalIncludeHeartbeatLogs, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddTodos = new CheckBox { Content = "Todo items (todo-items.json)", IsChecked = _backupAdditionalIncludeTodoItems, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddStateConfig = new CheckBox { Content = "UI state (state-config.json)", IsChecked = _backupAdditionalIncludeStateConfig, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddSessionState = new CheckBox { Content = "Session (session-state.json)", IsChecked = _backupAdditionalIncludeSessionState, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddSafePaste = new CheckBox { Content = "Safe paste (safe-paste.dat + keys)", IsChecked = _backupAdditionalIncludeSafePaste, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddTimeReports = new CheckBox { Content = "Time Reports (plugin-time-reports.json)", IsChecked = _backupAdditionalIncludeTimeReports, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddProjectLineCounter = new CheckBox { Content = "Project Line Counter (plugin-project-line-counter.json)", IsChecked = _backupAdditionalIncludeProjectLineCounter, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddTaskPanel = new CheckBox { Content = "Task panel (plugin-task-panel.json)", IsChecked = _backupAdditionalIncludeTaskPanel, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddAlarms = new CheckBox { Content = "Alarms (plugin-alarms.json)", IsChecked = _backupAdditionalIncludeAlarms, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddStandup = new CheckBox { Content = "Standup (plugin-standup.json)", IsChecked = _backupAdditionalIncludeStandup, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddMessageOverlay = new CheckBox { Content = "Message overlay (plugin-msg-overlay.json)", IsChecked = _backupAdditionalIncludeMessageOverlay, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddMidi = new CheckBox { Content = "MIDI custom songs (midi-custom-songs.json)", IsChecked = _backupAdditionalIncludeMidiCustomSongs, Margin = new Thickness(0, 0, 0, 4) };
        var chkBackupAddImages = new CheckBox { Content = "Images (images folder)", IsChecked = _backupAdditionalIncludeImages, Margin = new Thickness(0, 0, 0, 0) };
        additionalBackupPanel.Children.Add(chkBackupAddSettings);
        additionalBackupPanel.Children.Add(chkBackupAddLog);
        additionalBackupPanel.Children.Add(chkBackupAddHeartbeat);
        additionalBackupPanel.Children.Add(chkBackupAddTodos);
        additionalBackupPanel.Children.Add(chkBackupAddStateConfig);
        additionalBackupPanel.Children.Add(chkBackupAddSessionState);
        additionalBackupPanel.Children.Add(chkBackupAddSafePaste);
        additionalBackupPanel.Children.Add(chkBackupAddTimeReports);
        additionalBackupPanel.Children.Add(chkBackupAddProjectLineCounter);
        additionalBackupPanel.Children.Add(chkBackupAddTaskPanel);
        additionalBackupPanel.Children.Add(chkBackupAddAlarms);
        additionalBackupPanel.Children.Add(chkBackupAddStandup);
        additionalBackupPanel.Children.Add(chkBackupAddMessageOverlay);
        additionalBackupPanel.Children.Add(chkBackupAddMidi);
        additionalBackupPanel.Children.Add(chkBackupAddImages);
        expanderAdditionalBackup.Content = additionalBackupPanel;
        backupPanel.Children.Add(expanderAdditionalBackup);

        backupPanel.Children.Add(new TextBlock { Text = "Auto-save interval (seconds):" });
        var txtAutoSave = new TextBox { Text = ((int)_autoSaveTimer.Interval.TotalSeconds).ToString(), Margin = new Thickness(0, 4, 0, 10) };
        backupPanel.Children.Add(txtAutoSave);
        backupPanel.Children.Add(new TextBlock { Text = "Initial lines per new tab:" });
        var txtLines = new TextBox { Text = _initialLines.ToString(), Margin = new Thickness(0, 4, 0, 0) };
        backupPanel.Children.Add(txtLines);
        tabControl.Items.Add(new TabItem { Header = "Backup", Content = new ScrollViewer { Content = backupPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        // ---- Sync tab ---------------------------------------------------------------
        var syncPanel = new StackPanel { Margin = new Thickness(12) };
        syncPanel.Children.Add(new TextBlock
        {
            Text = "Plain text tab sync — keep each open tab in sync with a UTF-8 .txt file. The first line of every file written from Noted is a # lastupdated: <UTC> header used to detect external edits.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        });

        syncPanel.Children.Add(chkCloudPlainTabs);
        syncPanel.Children.Add(new TextBlock { Text = "Plain text tabs folder:" });
        var plainTabsRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        plainTabsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        plainTabsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(txtCloudPlainTabs, 0);
        Grid.SetColumn(btnBrowseCloudPlainTabs, 1);
        plainTabsRow.Children.Add(txtCloudPlainTabs);
        plainTabsRow.Children.Add(btnBrowseCloudPlainTabs);
        syncPanel.Children.Add(plainTabsRow);

        var chkInstream = new CheckBox
        {
            Content = "Sync from plain text tabs folder (pull external edits back into Noted)",
            IsChecked = _cloudSyncTabsPlainTextInstreamEnabled,
            Margin = new Thickness(0, 8, 0, 4)
        };
        syncPanel.Children.Add(chkInstream);

        syncPanel.Children.Add(new TextBlock { Text = "Plain text tabs folder instream sync interval:" });
        var instreamIntervalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var cmbInstreamHours = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        for (int h = 0; h <= 50; h++) cmbInstreamHours.Items.Add(h);
        cmbInstreamHours.SelectedItem = _cloudSyncTabsPlainTextInstreamHours;
        if (cmbInstreamHours.SelectedItem == null) cmbInstreamHours.SelectedItem = 0;
        var cmbInstreamMinutes = new ComboBox { Width = 80, Margin = new Thickness(8, 0, 8, 0) };
        foreach (var m in CloudMinuteOptions) cmbInstreamMinutes.Items.Add(m);
        cmbInstreamMinutes.SelectedItem = _cloudSyncTabsPlainTextInstreamMinutes;
        if (cmbInstreamMinutes.SelectedItem == null) cmbInstreamMinutes.SelectedItem = 5;
        instreamIntervalRow.Children.Add(cmbInstreamHours);
        instreamIntervalRow.Children.Add(new TextBlock { Text = "hours", VerticalAlignment = VerticalAlignment.Center });
        instreamIntervalRow.Children.Add(cmbInstreamMinutes);
        instreamIntervalRow.Children.Add(new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center });
        syncPanel.Children.Add(instreamIntervalRow);

        var txtLastInstream = new TextBlock
        {
            Text = $"Last instream sync: {FormatCloudCopyTimestamp(_lastInstreamPlainTabsSyncUtc)}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6)
        };
        syncPanel.Children.Add(txtLastInstream);

        var btnInstreamSyncNow = new Button
        {
            Content = "Sync now",
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        syncPanel.Children.Add(btnInstreamSyncNow);

        void RefreshCloudPlainTabsEditorsEnabled()
        {
            var outOn = chkCloudPlainTabs.IsChecked == true;
            txtCloudPlainTabs.IsEnabled = outOn;
            btnBrowseCloudPlainTabs.IsEnabled = outOn;

            var folderSet = !string.IsNullOrWhiteSpace(txtCloudPlainTabs.Text);
            var instreamAvailable = outOn && folderSet;
            chkInstream.IsEnabled = instreamAvailable;
            if (!instreamAvailable) chkInstream.IsChecked = false;

            var instreamOn = instreamAvailable && chkInstream.IsChecked == true;
            cmbInstreamHours.IsEnabled = instreamOn;
            cmbInstreamMinutes.IsEnabled = instreamOn;
            btnInstreamSyncNow.IsEnabled = instreamOn;
        }

        chkCloudPlainTabs.Checked += (_, _) => RefreshCloudPlainTabsEditorsEnabled();
        chkCloudPlainTabs.Unchecked += (_, _) => RefreshCloudPlainTabsEditorsEnabled();
        chkInstream.Checked += (_, _) => RefreshCloudPlainTabsEditorsEnabled();
        chkInstream.Unchecked += (_, _) => RefreshCloudPlainTabsEditorsEnabled();
        txtCloudPlainTabs.TextChanged += (_, _) => RefreshCloudPlainTabsEditorsEnabled();
        RefreshCloudPlainTabsEditorsEnabled();

        btnInstreamSyncNow.Click += (_, _) =>
        {
            // Apply the in-dialog values for this manual run so the user does not have to click OK first.
            var folderText = (txtCloudPlainTabs.Text ?? string.Empty).Trim();
            if (chkCloudPlainTabs.IsChecked != true || string.IsNullOrWhiteSpace(folderText))
            {
                MessageBox.Show("Enable plain text tab sync and set a folder first.", "Tab Sync",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var prevOutEnabled = _cloudSyncTabsPlainTextEnabled;
            var prevInEnabled = _cloudSyncTabsPlainTextInstreamEnabled;
            var prevFolder = _cloudSyncTabsPlainTextFolder;
            var prevLastInstream = _lastInstreamPlainTabsSyncUtc;
            try
            {
                _cloudSyncTabsPlainTextEnabled = true;
                _cloudSyncTabsPlainTextInstreamEnabled = true;
                _cloudSyncTabsPlainTextFolder = folderText;
                _lastInstreamPlainTabsSyncUtc = DateTime.MinValue;
                TickInstreamPlainTextTabSync();
                txtLastInstream.Text = $"Last instream sync: {FormatCloudCopyTimestamp(_lastInstreamPlainTabsSyncUtc)}";
            }
            finally
            {
                _cloudSyncTabsPlainTextEnabled = prevOutEnabled;
                _cloudSyncTabsPlainTextInstreamEnabled = prevInEnabled;
                _cloudSyncTabsPlainTextFolder = prevFolder;
                if (_lastInstreamPlainTabsSyncUtc == DateTime.MinValue)
                    _lastInstreamPlainTabsSyncUtc = prevLastInstream;
            }
        };

        tabControl.Items.Add(new TabItem
        {
            Header = "Sync",
            Content = new ScrollViewer { Content = syncPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var heartbeatPanel = new StackPanel { Margin = new Thickness(12) };
        heartbeatPanel.Children.Add(new TextBlock
        {
            Text = "Heartbeat writes periodic uptime markers to the backup folder (uptime-heartbeat-YYYY-MM.log). You can write from Noted, from the standalone Heartbeat app, or both.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var chkWriteUptimeHeartbeatInNoted = new CheckBox
        {
            Content = "Write uptime heartbeat from Noted",
            IsChecked = _writeUptimeHeartbeatInNoted,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkUseStandaloneHeartbeatApp = new CheckBox
        {
            Content = "Using standalone Heartbeat application",
            IsChecked = _useStandaloneHeartbeatApp,
            Margin = new Thickness(0, 0, 0, 12)
        };
        heartbeatPanel.Children.Add(chkWriteUptimeHeartbeatInNoted);
        heartbeatPanel.Children.Add(chkUseStandaloneHeartbeatApp);
        heartbeatPanel.Children.Add(new TextBlock { Text = "Uptime heartbeat interval:" });
        var cmbUptimeHeartbeatMinutes = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var m in UptimeHeartbeatIntervalMinutes)
        {
            cmbUptimeHeartbeatMinutes.Items.Add(new ComboBoxItem
            {
                Content = m == 1 ? "1 minute" : $"{m} minutes",
                Tag = m
            });
        }

        var intervalMinute = SnapUptimeHeartbeatToIntervalMinute(_uptimeHeartbeatSeconds);
        foreach (ComboBoxItem item in cmbUptimeHeartbeatMinutes.Items)
        {
            if (item.Tag is int tag && tag == intervalMinute)
            {
                cmbUptimeHeartbeatMinutes.SelectedItem = item;
                break;
            }
        }

        heartbeatPanel.Children.Add(cmbUptimeHeartbeatMinutes);
        tabControl.Items.Add(new TabItem
        {
            Header = "Heartbeat",
            Content = new ScrollViewer { Content = heartbeatPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var fontPanel = new StackPanel { Margin = new Thickness(12) };
        fontPanel.Children.Add(new TextBlock { Text = "Font family:" });
        var cmbFont = new ComboBox { IsEditable = true, Margin = new Thickness(0, 4, 0, 10) };
        string[] popularFonts =
        {
            "Consolas", "Courier New", "Source Code Pro", "Cascadia Code", "Cascadia Mono", "Fira Code",
            "JetBrains Mono", "Lucida Console", "Menlo", "Monaco", "Roboto Mono", "Ubuntu Mono"
        };
        foreach (var f in popularFonts)
            cmbFont.Items.Add(f);
        cmbFont.Text = _fontFamily;
        fontPanel.Children.Add(cmbFont);

        fontPanel.Children.Add(new TextBlock { Text = "Font size:" });
        var txtFontSize = new TextBox { Text = _fontSize.ToString(), Margin = new Thickness(0, 4, 0, 10) };
        fontPanel.Children.Add(txtFontSize);

        fontPanel.Children.Add(new TextBlock { Text = "Font weight:" });
        var cmbFontWeight = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        var weights = new (string Name, int Value)[]
        {
            ("Thin (100)", 100),
            ("ExtraLight (200)", 200),
            ("Light (300)", 300),
            ("Normal (400)", 400),
            ("Medium (500)", 500),
            ("SemiBold (600)", 600),
            ("Bold (700)", 700),
            ("ExtraBold (800)", 800),
            ("Black (900)", 900)
        };
        int selectedIdx = 3;
        for (int i = 0; i < weights.Length; i++)
        {
            cmbFontWeight.Items.Add(weights[i].Name);
            if (weights[i].Value == _fontWeight) selectedIdx = i;
        }
        cmbFontWeight.SelectedIndex = selectedIdx;
        fontPanel.Children.Add(cmbFontWeight);
        tabControl.Items.Add(new TabItem
        {
            Header = "Fonts",
            Content = new ScrollViewer { Content = fontPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var colorsPanel = new Grid { Margin = new Thickness(12) };
        colorsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        colorsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] colorOptions =
        [
            "Orange",
            "Goldenrod",
            "Tomato",
            "SkyBlue",
            "LightSkyBlue",
            "Khaki",
            "LightGreen",
            "LightCoral",
            "IndianRed",
            "PaleVioletRed",
            "#FFE1F0FF",
            "#FFFFF4B3",
            "#FFFFEA80",
            "#FFFFCDD2",
            "#FFFFB3BA"
        ];

        (ComboBox combo, Border preview) CreateColorPicker(Color initial)
        {
            var combo = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 8, 8) };
            foreach (var option in colorOptions)
                combo.Items.Add(option);
            combo.Text = ColorToHex(initial);

            var preview = new Border
            {
                Width = 54,
                Height = 22,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };

            void RefreshPreview()
            {
                if (TryParseColor(combo.Text, out var color))
                    preview.Background = new SolidColorBrush(color);
            }

            combo.LostFocus += (_, _) => RefreshPreview();
            combo.SelectionChanged += (_, _) => RefreshPreview();
            RefreshPreview();
            return (combo, preview);
        }

        var (cmbSelectedLineColor, selectedLinePreview) = CreateColorPicker(_selectedLineColor);
        var (cmbHighlightedLineColor, highlightedLinePreview) = CreateColorPicker(_highlightedLineColor);
        var (cmbSelectedHighlightedLineColor, selectedHighlightedLinePreview) = CreateColorPicker(_selectedHighlightedLineColor);
        var (cmbCriticalHighlightedLineColor, criticalHighlightedLinePreview) = CreateColorPicker(_criticalHighlightedLineColor);
        var (cmbSelectedCriticalHighlightedLineColor, selectedCriticalHighlightedLinePreview) = CreateColorPicker(_selectedCriticalHighlightedLineColor);

        var lblSelectedLineColor = new TextBlock { Text = "Selected line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblSelectedLineColor, 0);
        colorsPanel.Children.Add(lblSelectedLineColor);
        Grid.SetRow(cmbSelectedLineColor, 0);
        Grid.SetColumn(cmbSelectedLineColor, 1);
        colorsPanel.Children.Add(cmbSelectedLineColor);
        Grid.SetRow(selectedLinePreview, 0);
        Grid.SetColumn(selectedLinePreview, 2);
        colorsPanel.Children.Add(selectedLinePreview);

        var lblHighlightedLineColor = new TextBlock { Text = "Highlighted line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblHighlightedLineColor, 1);
        colorsPanel.Children.Add(lblHighlightedLineColor);
        Grid.SetRow(cmbHighlightedLineColor, 1);
        Grid.SetColumn(cmbHighlightedLineColor, 1);
        colorsPanel.Children.Add(cmbHighlightedLineColor);
        Grid.SetRow(highlightedLinePreview, 1);
        Grid.SetColumn(highlightedLinePreview, 2);
        colorsPanel.Children.Add(highlightedLinePreview);

        var lblSelectedHighlightedLineColor = new TextBlock { Text = "Selected highlighted line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblSelectedHighlightedLineColor, 2);
        colorsPanel.Children.Add(lblSelectedHighlightedLineColor);
        Grid.SetRow(cmbSelectedHighlightedLineColor, 2);
        Grid.SetColumn(cmbSelectedHighlightedLineColor, 1);
        colorsPanel.Children.Add(cmbSelectedHighlightedLineColor);
        Grid.SetRow(selectedHighlightedLinePreview, 2);
        Grid.SetColumn(selectedHighlightedLinePreview, 2);
        colorsPanel.Children.Add(selectedHighlightedLinePreview);

        var lblCriticalHighlightedLineColor = new TextBlock { Text = "Critical highlighted line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblCriticalHighlightedLineColor, 3);
        colorsPanel.Children.Add(lblCriticalHighlightedLineColor);
        Grid.SetRow(cmbCriticalHighlightedLineColor, 3);
        Grid.SetColumn(cmbCriticalHighlightedLineColor, 1);
        colorsPanel.Children.Add(cmbCriticalHighlightedLineColor);
        Grid.SetRow(criticalHighlightedLinePreview, 3);
        Grid.SetColumn(criticalHighlightedLinePreview, 2);
        colorsPanel.Children.Add(criticalHighlightedLinePreview);

        var lblSelectedCriticalHighlightedLineColor = new TextBlock { Text = "Selected critical highlighted line color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        Grid.SetRow(lblSelectedCriticalHighlightedLineColor, 4);
        colorsPanel.Children.Add(lblSelectedCriticalHighlightedLineColor);
        Grid.SetRow(cmbSelectedCriticalHighlightedLineColor, 4);
        Grid.SetColumn(cmbSelectedCriticalHighlightedLineColor, 1);
        colorsPanel.Children.Add(cmbSelectedCriticalHighlightedLineColor);
        Grid.SetRow(selectedCriticalHighlightedLinePreview, 4);
        Grid.SetColumn(selectedCriticalHighlightedLinePreview, 2);
        colorsPanel.Children.Add(selectedCriticalHighlightedLinePreview);

        tabControl.Items.Add(new TabItem
        {
            Header = "Colors",
            Content = new ScrollViewer { Content = colorsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var shortkeysPanel = new StackPanel { Margin = new Thickness(12) };
        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Set shortcuts (format examples: Ctrl+N, Ctrl+Shift+N, F2):",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        shortkeysPanel.Children.Add(new TextBlock { Text = "New tab (primary):" });
        var txtShortcutNewPrimary = new TextBox { Text = _shortcutNewPrimary, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutNewPrimary);

        shortkeysPanel.Children.Add(new TextBlock { Text = "New tab (secondary, optional):" });
        var txtShortcutNewSecondary = new TextBox { Text = _shortcutNewSecondary, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutNewSecondary);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Close current tab:" });
        var txtShortcutClose = new TextBox { Text = _shortcutCloseTab, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutClose);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Rename current tab:" });
        var txtShortcutRename = new TextBox { Text = _shortcutRenameTab, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutRename);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Add 10 blank lines at end:" });
        var txtShortcutAddBlankLines = new TextBox { Text = _shortcutAddBlankLines, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutAddBlankLines);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Remove trailing empty lines (keep one final line):" });
        var txtShortcutTrimTrailingEmptyLines = new TextBox { Text = _shortcutTrimTrailingEmptyLines, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutTrimTrailingEmptyLines);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Toggle highlight on current/selected lines:" });
        var txtShortcutToggleHighlight = new TextBox { Text = _shortcutToggleHighlight, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutToggleHighlight);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Toggle critical highlight on current/selected lines:" });
        var txtShortcutToggleCriticalHighlight = new TextBox { Text = _shortcutToggleCriticalHighlight, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutToggleCriticalHighlight);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Go to line:" });
        var txtShortcutGoToLine = new TextBox { Text = _shortcutGoToLine, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutGoToLine);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Go to tab:" });
        var txtShortcutGoToTab = new TextBox { Text = _shortcutGoToTab, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutGoToTab);

        shortkeysPanel.Children.Add(new TextBlock { Text = "MIDI Player (toggle / dock):" });
        var txtShortcutMidiPlayer = new TextBox { Text = _shortcutMidiPlayer, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutMidiPlayer);

        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Ctrl+Shift+T reopens the most recently closed tab.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4)
        });
        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Ctrl+S does not save your work.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4)
        });
        shortkeysPanel.Children.Add(new TextBlock
        {
            Text = "Ctrl+MouseWheel zooms the editor for this session (saved font size is unchanged).",
            Foreground = Brushes.DimGray
        });
        tabControl.Items.Add(new TabItem
        {
            Header = "Shortcuts",
            Content = new ScrollViewer { Content = shortkeysPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var viewPanel = new StackPanel { Margin = new Thickness(12) };
        viewPanel.Children.Add(new TextBlock
        {
            Text = "Open http(s) links in:",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var cmbExternalBrowser = new ComboBox
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6)
        };
        foreach (var (label, choice) in new (string Label, ExternalBrowserChoice Choice)[]
        {
            ("System default", ExternalBrowserChoice.Default),
            ("Chrome", ExternalBrowserChoice.Chrome),
            ("Edge", ExternalBrowserChoice.Edge),
            ("Firefox", ExternalBrowserChoice.Firefox)
        })
            cmbExternalBrowser.Items.Add(new ComboBoxItem { Content = label, Tag = choice });
        cmbExternalBrowser.SelectedIndex = 0;
        for (int i = 0; i < cmbExternalBrowser.Items.Count; i++)
        {
            if (cmbExternalBrowser.Items[i] is ComboBoxItem item && item.Tag is ExternalBrowserChoice c && c == _externalBrowserForLinks)
            {
                cmbExternalBrowser.SelectedIndex = i;
                break;
            }
        }

        viewPanel.Children.Add(cmbExternalBrowser);
        viewPanel.Children.Add(new TextBlock
        {
            Text = "Applies to the Useful menu, hyperlinks in notes (Ctrl+click), and plugins that open web links.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });
        var chkStyledBullets = new CheckBox
        {
            Content = "Replace '- ' and '* ' prefixes with rendered bullet symbols",
            IsChecked = _fancyBulletsEnabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkWrapLongLinesVisually = new CheckBox
        {
            Content = "Split lines visually",
            IsChecked = _wrapLongLinesVisually,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var visualWrapColumnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(24, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        visualWrapColumnRow.Children.Add(new TextBlock
        {
            Text = "Line length (characters):",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var txtVisualWrapColumn = new TextBox
        {
            Text = _visualLineWrapColumn.ToString(),
            Width = 80,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        visualWrapColumnRow.Children.Add(txtVisualWrapColumn);
        var chkShowHorizontalRuler = new CheckBox
        {
            Content = "Render '---' lines as horizontal dividers",
            IsChecked = _showHorizontalRuler,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkShowInlineImages = new CheckBox
        {
            Content = "Render inline image markers (^<file.png>) as images",
            IsChecked = _showInlineImages,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkShowLineAssignments = new CheckBox
        {
            Content = "Show line assignment badges (Tools → Users)",
            IsChecked = _showLineAssignments,
            Margin = new Thickness(0, 0, 0, 10)
        };
        viewPanel.Children.Add(chkStyledBullets);
        viewPanel.Children.Add(chkWrapLongLinesVisually);
        viewPanel.Children.Add(visualWrapColumnRow);
        viewPanel.Children.Add(chkShowHorizontalRuler);
        viewPanel.Children.Add(chkShowLineAssignments);
        viewPanel.Children.Add(chkShowInlineImages);
        viewPanel.Children.Add(new TextBlock
        {
            Text = "Visual wrapping only changes display. File content and line numbers stay unchanged.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10)
        });
        viewPanel.Children.Add(new TextBlock
        {
            Text = "Rendered bullet style:",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var cmbBulletStyle = new ComboBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var option in FancyBulletStyleOptions)
        {
            cmbBulletStyle.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Style
            });
        }
        for (int i = 0; i < cmbBulletStyle.Items.Count; i++)
        {
            if (cmbBulletStyle.Items[i] is ComboBoxItem item
                && item.Tag is FancyBulletStyle style
                && style == _fancyBulletStyle)
            {
                cmbBulletStyle.SelectedIndex = i;
                break;
            }
        }
        if (cmbBulletStyle.SelectedIndex < 0)
            cmbBulletStyle.SelectedIndex = 0;
        cmbBulletStyle.IsEnabled = chkStyledBullets.IsChecked == true;
        viewPanel.Children.Add(cmbBulletStyle);
        var bulletPreviewBorder = new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = Brushes.WhiteSmoke
        };
        var bulletPreviewStack = new StackPanel();
        bulletPreviewBorder.Child = bulletPreviewStack;
        viewPanel.Children.Add(bulletPreviewBorder);
        viewPanel.Children.Add(new TextBlock
        {
            Text = "Bullet style is only used when bullet symbol rendering is enabled.",
            Foreground = Brushes.DimGray
        });

        static FrameworkElement BuildBulletMarker(FancyBulletStyle style)
        {
            const double markerSize = 8;
            var color = SystemColors.ControlTextColor;
            var brush = new SolidColorBrush(color);
            var strokeBrush = new SolidColorBrush(color);
            brush.Freeze();
            strokeBrush.Freeze();

            return style switch
            {
                FancyBulletStyle.HollowCircle => new Shapes.Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Stroke = strokeBrush,
                    StrokeThickness = 1.25
                },
                FancyBulletStyle.Square => new Shapes.Rectangle
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = brush
                },
                FancyBulletStyle.Diamond => new Shapes.Polygon
                {
                    Fill = brush,
                    Points = new PointCollection
                    {
                        new(markerSize / 2, 0),
                        new(markerSize, markerSize / 2),
                        new(markerSize / 2, markerSize),
                        new(0, markerSize / 2)
                    },
                    Width = markerSize,
                    Height = markerSize,
                    Stretch = Stretch.Fill
                },
                FancyBulletStyle.Dash => new Shapes.Line
                {
                    X1 = 0,
                    Y1 = markerSize / 2,
                    X2 = markerSize + 4,
                    Y2 = markerSize / 2,
                    Stroke = strokeBrush,
                    StrokeThickness = 1.5,
                    Width = markerSize + 4,
                    Height = markerSize
                },
                _ => new Shapes.Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = brush
                }
            };
        }

        void RefreshBulletPreview()
        {
            bulletPreviewStack.Children.Clear();

            var selectedStyle = FancyBulletStyle.Dot;
            if (cmbBulletStyle.SelectedItem is ComboBoxItem selectedStyleItem
                && selectedStyleItem.Tag is FancyBulletStyle style)
            {
                selectedStyle = style;
            }

            string[] sampleLines =
            [
                "First task item",
                "Secondary point",
                "Something to remember"
            ];

            foreach (var line in sampleLines)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(new Border
                {
                    Width = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = BuildBulletMarker(selectedStyle)
                });
                row.Children.Add(new TextBlock
                {
                    Text = line,
                    VerticalAlignment = VerticalAlignment.Center
                });
                bulletPreviewStack.Children.Add(row);
            }
        }

        void ApplyLiveViewPreviewFromSettingsControls()
        {
            cmbBulletStyle.IsEnabled = chkStyledBullets.IsChecked == true;
            txtVisualWrapColumn.IsEnabled = chkWrapLongLinesVisually.IsChecked == true;

            _fancyBulletsEnabled = chkStyledBullets.IsChecked == true;
            _wrapLongLinesVisually = chkWrapLongLinesVisually.IsChecked == true;
            if (int.TryParse(txtVisualWrapColumn.Text, out var visualWrapColumn))
                _visualLineWrapColumn = NormalizeVisualLineWrapColumn(visualWrapColumn);
            _showHorizontalRuler = chkShowHorizontalRuler.IsChecked == true;
            _showLineAssignments = chkShowLineAssignments.IsChecked == true;
            _showInlineImages = chkShowInlineImages.IsChecked == true;
            if (cmbBulletStyle.SelectedItem is ComboBoxItem selectedStyleItem
                && selectedStyleItem.Tag is FancyBulletStyle selectedStyle)
            {
                _fancyBulletStyle = selectedStyle;
            }

            ApplyViewRenderingSettings();
            RefreshBulletPreview();
        }

        txtVisualWrapColumn.IsEnabled = chkWrapLongLinesVisually.IsChecked == true;
        chkStyledBullets.Checked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkStyledBullets.Unchecked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkWrapLongLinesVisually.Checked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkWrapLongLinesVisually.Unchecked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        txtVisualWrapColumn.TextChanged += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkShowHorizontalRuler.Checked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkShowHorizontalRuler.Unchecked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkShowLineAssignments.Checked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkShowLineAssignments.Unchecked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkShowInlineImages.Checked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        chkShowInlineImages.Unchecked += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        cmbBulletStyle.SelectionChanged += (_, _) => ApplyLiveViewPreviewFromSettingsControls();
        RefreshBulletPreview();

        tabControl.Items.Add(new TabItem
        {
            Header = "View",
            Content = new ScrollViewer { Content = viewPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var fridayPanel = new StackPanel { Margin = new Thickness(12) };
        var chkFridayFeeling = new CheckBox
        {
            Content = "Enable Fredagsparty background automatically on Fridays",
            IsChecked = _isFridayFeelingEnabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var untilCloseFredagsparty = _isFredagspartySessionEnabled;
        var btnFredagspartyUntilAppCloses = new Button
        {
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Left-click toggles Fredagsparty until you quit Noted.\nRight-click for \"until restart.\""
        };
        var miFredagspartyUntilRestart = new MenuItem { Header = "Turn off Fredagsparty until restart" };
        btnFredagspartyUntilAppCloses.ContextMenu = new ContextMenu { Items = { miFredagspartyUntilRestart } };
        void RefreshFredagspartyUntilCloseButtonLabel()
        {
            miFredagspartyUntilRestart.IsEnabled = !_isFredagspartyTemporarilyDisabled;
            if (_isFredagspartyTemporarilyDisabled)
            {
                btnFredagspartyUntilAppCloses.Content = "Fredagsparty off until Noted restarts";
                btnFredagspartyUntilAppCloses.IsEnabled = false;
                return;
            }

            btnFredagspartyUntilAppCloses.IsEnabled = true;
            btnFredagspartyUntilAppCloses.Content = untilCloseFredagsparty
                ? "Turn off Fredagsparty until app closes"
                : "Turn on Fredagsparty until app closes";
        }
        RefreshFredagspartyUntilCloseButtonLabel();
        btnFredagspartyUntilAppCloses.Click += (_, _) =>
        {
            untilCloseFredagsparty = !untilCloseFredagsparty;
            _isFredagspartySessionEnabled = untilCloseFredagsparty;
            RefreshFredagspartyUntilCloseButtonLabel();
            ApplyFridayFeelingToOpenEditors();
        };
        miFredagspartyUntilRestart.Click += (_, _) =>
        {
            _isFredagspartyTemporarilyDisabled = true;
            ApplyFridayFeelingToOpenEditors();
            RefreshFredagspartyUntilCloseButtonLabel();
        };
        fridayPanel.Children.Add(chkFridayFeeling);
        fridayPanel.Children.Add(btnFredagspartyUntilAppCloses);
        tabControl.Items.Add(new TabItem
        {
            Header = "Friday",
            Content = new ScrollViewer { Content = fridayPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var tabsSettingsPanel = new StackPanel { Margin = new Thickness(12) };
        tabsSettingsPanel.Children.Add(new TextBlock
        {
            Text = "Tab Cleanup: treat tabs as stale (soft red in Tools → Tab Cleanup) when last edit is older than this many days:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var txtTabStaleDays = new TextBox
        {
            Text = _tabCleanupStaleDays.ToString(),
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 0)
        };
        tabsSettingsPanel.Children.Add(txtTabStaleDays);
        tabsSettingsPanel.Children.Add(new TextBlock
        {
            Text = $"Closed tabs to keep in {ClosedTabsFileName} ({MinClosedTabsMaxCount}-{MaxClosedTabsMaxCount}):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 8)
        });
        var txtClosedTabsMaxCount = new TextBox
        {
            Text = _closedTabsMaxCount.ToString(),
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 0)
        };
        tabsSettingsPanel.Children.Add(txtClosedTabsMaxCount);
        tabsSettingsPanel.Children.Add(new TextBlock
        {
            Text = $"Closed tab retention in days ({MinClosedTabsRetentionDays}-{MaxClosedTabsRetentionDays}, 0 disables age cleanup):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 8)
        });
        var txtClosedTabsRetentionDays = new TextBox
        {
            Text = _closedTabsRetentionDays.ToString(),
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 0)
        };
        tabsSettingsPanel.Children.Add(txtClosedTabsRetentionDays);
        tabsSettingsPanel.Children.Add(new TextBlock
        {
            Text = "Save bullets as:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 4)
        });
        var cmbSaveBulletsAs = new ComboBox
        {
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 0)
        };
        cmbSaveBulletsAs.Items.Add(new ComboBoxItem { Content = "-", Tag = '-' });
        cmbSaveBulletsAs.Items.Add(new ComboBoxItem { Content = "*", Tag = '*' });
        cmbSaveBulletsAs.SelectedItem = _saveBulletsAsMarker == '*'
            ? cmbSaveBulletsAs.Items[1]
            : cmbSaveBulletsAs.Items[0];
        tabsSettingsPanel.Children.Add(cmbSaveBulletsAs);

        var chkBulletHoverTooltips = new CheckBox
        {
            Content = "Show bullet hover tooltips (for '- ' and '* ' lines)",
            IsChecked = _showBulletHoverTooltips,
            Margin = new Thickness(0, 12, 0, 0)
        };
        tabsSettingsPanel.Children.Add(chkBulletHoverTooltips);
        tabControl.Items.Add(new TabItem
        {
            Header = "Tabs",
            Content = new ScrollViewer { Content = tabsSettingsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        // --- Task Panel tab ---
        var workingTaskAreas = CloneTaskAreas(_taskAreas);
        if (workingTaskAreas.Count == 0)
            workingTaskAreas = BuildDefaultTaskAreas();

        var taskPanelTab = new Grid { Margin = new Thickness(12) };
        taskPanelTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        taskPanelTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        taskPanelTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleRow = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        titleRow.Children.Add(new TextBlock { Text = "Task Panel title:", Margin = new Thickness(0, 0, 0, 4) });
        var txtTaskPanelTitle = new TextBox { Text = _taskPanelTitle };
        titleRow.Children.Add(txtTaskPanelTitle);
        Grid.SetRow(titleRow, 0);
        taskPanelTab.Children.Add(titleRow);

        var bodyGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        Grid.SetRow(bodyGrid, 2);
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        taskPanelTab.Children.Add(bodyGrid);

        var areasPanel = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(areasPanel, 0);
        bodyGrid.Children.Add(areasPanel);
        var areasHeader = new TextBlock { Text = "Areas", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(areasHeader, Dock.Top);
        areasPanel.Children.Add(areasHeader);
        var areasButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(areasButtonRow, Dock.Bottom);
        var btnAddArea = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var btnRenameArea = new Button { Content = "Rename", Width = 80, Margin = new Thickness(0, 0, 6, 0) };
        var btnRemoveArea = new Button { Content = "Remove", Width = 80 };
        areasButtonRow.Children.Add(btnAddArea);
        areasButtonRow.Children.Add(btnRenameArea);
        areasButtonRow.Children.Add(btnRemoveArea);
        areasPanel.Children.Add(areasButtonRow);
        var areasList = new ListBox();
        areasPanel.Children.Add(areasList);

        var groupsPanel = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(groupsPanel, 1);
        bodyGrid.Children.Add(groupsPanel);
        var groupsHeader = new TextBlock { Text = "Groups", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(groupsHeader, Dock.Top);
        groupsPanel.Children.Add(groupsHeader);
        var groupsButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(groupsButtonRow, Dock.Bottom);
        var btnAddGroup = new Button { Content = "Add", Width = 60, Margin = new Thickness(0, 0, 6, 0) };
        var btnRenameGroup = new Button { Content = "Rename", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var btnRemoveGroup = new Button { Content = "Remove", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var btnMoveGroupUp = new Button { Content = "Up", Width = 50, Margin = new Thickness(0, 0, 6, 0) };
        var btnMoveGroupDown = new Button { Content = "Down", Width = 60 };
        groupsButtonRow.Children.Add(btnAddGroup);
        groupsButtonRow.Children.Add(btnRenameGroup);
        groupsButtonRow.Children.Add(btnRemoveGroup);
        groupsButtonRow.Children.Add(btnMoveGroupUp);
        groupsButtonRow.Children.Add(btnMoveGroupDown);
        groupsPanel.Children.Add(groupsButtonRow);

        var groupRetentionRow = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        groupRetentionRow.Children.Add(new TextBlock
        {
            Text = "Remove completed tasks from panel after (0 days + 0 hours = never hide):",
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        });
        var retentionInputRow = new StackPanel { Orientation = Orientation.Horizontal };
        var txtGroupRetentionDays = new TextBox { IsEnabled = false, Width = 60, VerticalContentAlignment = VerticalAlignment.Center };
        retentionInputRow.Children.Add(txtGroupRetentionDays);
        retentionInputRow.Children.Add(new TextBlock { Text = " days", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        var cmbGroupRetentionHours = new ComboBox { IsEnabled = false, Width = 70 };
        for (int h = MinCompletedRetentionHours; h <= MaxCompletedRetentionHours; h++)
            cmbGroupRetentionHours.Items.Add(new ComboBoxItem { Content = h.ToString(), Tag = h });
        retentionInputRow.Children.Add(cmbGroupRetentionHours);
        retentionInputRow.Children.Add(new TextBlock { Text = " hours", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        groupRetentionRow.Children.Add(retentionInputRow);
        groupRetentionRow.Children.Add(new TextBlock
        {
            Text = "Hidden items remain in Recently Completed.",
            Margin = new Thickness(0, 4, 0, 0),
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(groupRetentionRow, Dock.Bottom);
        groupsPanel.Children.Add(groupRetentionRow);

        var groupUndoneMarkRow = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var chkGroupUndoneMark = new CheckBox
        {
            Content = "Mark uncompleted tasks as overdue after:",
            IsEnabled = false,
            Margin = new Thickness(0, 0, 0, 6)
        };
        groupUndoneMarkRow.Children.Add(chkGroupUndoneMark);
        var undoneInputRow = new StackPanel { Orientation = Orientation.Horizontal };
        var txtGroupUndoneMarkDays = new TextBox { IsEnabled = false, Width = 60, VerticalContentAlignment = VerticalAlignment.Center };
        undoneInputRow.Children.Add(txtGroupUndoneMarkDays);
        undoneInputRow.Children.Add(new TextBlock { Text = " days", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        var cmbGroupUndoneMarkHours = new ComboBox { IsEnabled = false, Width = 70 };
        for (int h = MinUndoneMarkHours; h <= MaxUndoneMarkHours; h++)
            cmbGroupUndoneMarkHours.Items.Add(new ComboBoxItem { Content = h.ToString(), Tag = h });
        undoneInputRow.Children.Add(cmbGroupUndoneMarkHours);
        undoneInputRow.Children.Add(new TextBlock { Text = " hours", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        groupUndoneMarkRow.Children.Add(undoneInputRow);
        DockPanel.SetDock(groupUndoneMarkRow, Dock.Bottom);
        groupsPanel.Children.Add(groupUndoneMarkRow);

        var groupShortcutRow = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        groupShortcutRow.Children.Add(new TextBlock
        {
            Text = "Selected group shortcut key (single key, e.g. +, W, M):",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var txtGroupShortcut = new TextBox { IsEnabled = false };
        groupShortcutRow.Children.Add(txtGroupShortcut);
        DockPanel.SetDock(groupShortcutRow, Dock.Bottom);
        groupsPanel.Children.Add(groupShortcutRow);

        var groupsList = new ListBox();
        groupsPanel.Children.Add(groupsList);

        void RefreshAreasList(string? selectedAreaId = null)
        {
            areasList.Items.Clear();
            foreach (var area in workingTaskAreas)
                areasList.Items.Add(area);

            TaskAreaState? toSelect = null;
            if (!string.IsNullOrWhiteSpace(selectedAreaId))
                toSelect = workingTaskAreas.FirstOrDefault(a => string.Equals(a.Id, selectedAreaId, StringComparison.OrdinalIgnoreCase));
            toSelect ??= workingTaskAreas.FirstOrDefault();
            if (toSelect != null)
                areasList.SelectedItem = toSelect;
        }

        void RefreshGroupsList(string? selectedGroupId = null)
        {
            groupsList.Items.Clear();
            if (areasList.SelectedItem is not TaskAreaState area)
            {
                txtGroupShortcut.Text = string.Empty;
                txtGroupShortcut.IsEnabled = false;
                txtGroupRetentionDays.Text = string.Empty;
                txtGroupRetentionDays.IsEnabled = false;
                cmbGroupRetentionHours.SelectedIndex = 0;
                cmbGroupRetentionHours.IsEnabled = false;
                chkGroupUndoneMark.IsChecked = false;
                chkGroupUndoneMark.IsEnabled = false;
                txtGroupUndoneMarkDays.Text = string.Empty;
                txtGroupUndoneMarkDays.IsEnabled = false;
                cmbGroupUndoneMarkHours.SelectedIndex = 0;
                cmbGroupUndoneMarkHours.IsEnabled = false;
                return;
            }
            foreach (var group in area.Groups.OrderBy(g => g.SortOrder))
                groupsList.Items.Add(group);

            TaskGroupState? toSelect = null;
            if (!string.IsNullOrWhiteSpace(selectedGroupId))
                toSelect = area.Groups.FirstOrDefault(g => string.Equals(g.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase));
            toSelect ??= area.Groups.OrderBy(g => g.SortOrder).FirstOrDefault();
            if (toSelect != null)
                groupsList.SelectedItem = toSelect;
        }

        void UpdateGroupEditors()
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
            {
                txtGroupShortcut.Text = string.Empty;
                txtGroupShortcut.IsEnabled = false;
                txtGroupRetentionDays.Text = string.Empty;
                txtGroupRetentionDays.IsEnabled = false;
                cmbGroupRetentionHours.SelectedIndex = 0;
                cmbGroupRetentionHours.IsEnabled = false;
                chkGroupUndoneMark.IsChecked = false;
                chkGroupUndoneMark.IsEnabled = false;
                txtGroupUndoneMarkDays.Text = string.Empty;
                txtGroupUndoneMarkDays.IsEnabled = false;
                cmbGroupUndoneMarkHours.SelectedIndex = 0;
                cmbGroupUndoneMarkHours.IsEnabled = false;
                return;
            }

            txtGroupShortcut.IsEnabled = true;
            txtGroupShortcut.Text = group.ShortcutKey ?? string.Empty;

            txtGroupRetentionDays.IsEnabled = true;
            txtGroupRetentionDays.Text = NormalizeCompletedRetentionDays(group.CompletedRetentionDays, group.Id)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

            cmbGroupRetentionHours.IsEnabled = true;
            cmbGroupRetentionHours.SelectedIndex = NormalizeCompletedRetentionHours(group.CompletedRetentionHours);

            bool undoneEnabled = group.UndoneMarkEnabled == true;
            chkGroupUndoneMark.IsEnabled = true;
            chkGroupUndoneMark.IsChecked = undoneEnabled;
            txtGroupUndoneMarkDays.IsEnabled = undoneEnabled;
            txtGroupUndoneMarkDays.Text = NormalizeUndoneMarkDays(group.UndoneMarkDays)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            cmbGroupUndoneMarkHours.IsEnabled = undoneEnabled;
            cmbGroupUndoneMarkHours.SelectedIndex = NormalizeUndoneMarkHours(group.UndoneMarkHours) - MinUndoneMarkHours;
        }

        bool ApplySelectedGroupShortcut()
        {
            if (areasList.SelectedItem is not TaskAreaState area || groupsList.SelectedItem is not TaskGroupState group)
                return true;

            var normalizedShortcut = NormalizeTaskGroupShortcutKey(txtGroupShortcut.Text);
            var raw = (txtGroupShortcut.Text ?? string.Empty).Trim();
            if (raw.Length > 0 && normalizedShortcut.Length == 0)
            {
                MessageBox.Show("Shortcut must be a single key (for example: +, W, M) or blank.", "Task Panel",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtGroupShortcut.Text = group.ShortcutKey ?? string.Empty;
                return false;
            }

            if (normalizedShortcut.Length > 0
                && area.Groups.Any(other => !ReferenceEquals(other, group)
                    && string.Equals(other.ShortcutKey, normalizedShortcut, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Shortcut '{normalizedShortcut}' is already used by another group in this area.", "Task Panel",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtGroupShortcut.Text = group.ShortcutKey ?? string.Empty;
                return false;
            }

            group.ShortcutKey = normalizedShortcut;
            txtGroupShortcut.Text = normalizedShortcut;
            return true;
        }

        bool ApplySelectedGroupRetentionDays()
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
                return true;

            var raw = (txtGroupRetentionDays.Text ?? string.Empty).Trim();
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var days)
                || days < MinCompletedRetentionDays
                || days > MaxCompletedRetentionDays)
            {
                MessageBox.Show($"Days must be a whole number between {MinCompletedRetentionDays} and {MaxCompletedRetentionDays}.",
                    "Task Panel", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtGroupRetentionDays.Text = NormalizeCompletedRetentionDays(group.CompletedRetentionDays, group.Id)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return false;
            }

            group.CompletedRetentionDays = days;
            txtGroupRetentionDays.Text = days.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var hours = cmbGroupRetentionHours.SelectedIndex >= 0 ? cmbGroupRetentionHours.SelectedIndex : 0;
            group.CompletedRetentionHours = hours;
            return true;
        }

        bool ApplySelectedGroupUndoneMark()
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
                return true;

            bool enabled = chkGroupUndoneMark.IsChecked == true;
            group.UndoneMarkEnabled = enabled;
            if (!enabled)
                return true;

            var raw = (txtGroupUndoneMarkDays.Text ?? string.Empty).Trim();
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var days)
                || days < MinUndoneMarkDays
                || days > MaxUndoneMarkDays)
            {
                MessageBox.Show($"Days must be a whole number between {MinUndoneMarkDays} and {MaxUndoneMarkDays}.",
                    "Task Panel", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtGroupUndoneMarkDays.Text = NormalizeUndoneMarkDays(group.UndoneMarkDays)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return false;
            }

            group.UndoneMarkDays = days;
            txtGroupUndoneMarkDays.Text = days.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var hours = cmbGroupUndoneMarkHours.SelectedItem is ComboBoxItem item && item.Tag is int tag
                ? tag
                : MinUndoneMarkHours;
            group.UndoneMarkHours = hours;
            return true;
        }

        areasList.DisplayMemberPath = nameof(TaskAreaState.Name);
        groupsList.DisplayMemberPath = nameof(TaskGroupState.Name);
        areasList.SelectionChanged += (_, _) =>
        {
            RefreshGroupsList();
            UpdateGroupEditors();
        };
        groupsList.SelectionChanged += (_, _) => UpdateGroupEditors();

        btnAddArea.Click += (_, _) =>
        {
            var name = PromptForText("Add Area", "Area name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return;
            var newArea = new TaskAreaState
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name.Trim(),
                Groups = []
            };
            workingTaskAreas.Add(newArea);
            RefreshAreasList(newArea.Id);
            RefreshGroupsList();
        };
        btnRenameArea.Click += (_, _) =>
        {
            if (areasList.SelectedItem is not TaskAreaState area)
                return;
            var name = PromptForText("Rename Area", "Area name:", area.Name);
            if (string.IsNullOrWhiteSpace(name))
                return;
            area.Name = name.Trim();
            RefreshAreasList(area.Id);
        };
        btnRemoveArea.Click += (_, _) =>
        {
            if (areasList.SelectedItem is not TaskAreaState area)
                return;
            if (workingTaskAreas.Count <= 1)
            {
                MessageBox.Show("At least one area must exist.", "Task Panel", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var confirm = MessageBox.Show($"Remove area '{area.Name}'? All tasks in this area will be moved to the '{DefaultTaskAreaName}' area.",
                "Remove Area", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
            workingTaskAreas.Remove(area);
            RefreshAreasList();
            RefreshGroupsList();
        };

        btnAddGroup.Click += (_, _) =>
        {
            if (areasList.SelectedItem is not TaskAreaState area)
                return;
            var name = PromptForText("Add Group", "Group name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return;
            var newGroup = new TaskGroupState
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name.Trim(),
                SortOrder = (area.Groups.Count == 0 ? 0 : area.Groups.Max(g => g.SortOrder)) + 1,
                CompletedRetentionDays = DefaultCompletedRetentionDays
            };
            area.Groups.Add(newGroup);
            RefreshGroupsList(newGroup.Id);
        };
        btnRenameGroup.Click += (_, _) =>
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
                return;
            var name = PromptForText("Rename Group", "Group name:", group.Name);
            if (string.IsNullOrWhiteSpace(name))
                return;
            group.Name = name.Trim();
            RefreshGroupsList(group.Id);
        };
        btnRemoveGroup.Click += (_, _) =>
        {
            if (areasList.SelectedItem is not TaskAreaState area || groupsList.SelectedItem is not TaskGroupState group)
                return;
            if (area.Groups.Count <= 1)
            {
                MessageBox.Show("An area must contain at least one group.", "Task Panel", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var confirm = MessageBox.Show($"Remove group '{group.Name}'? Tasks in it will be moved to the first remaining group in this area.",
                "Remove Group", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
            area.Groups.Remove(group);
            int i = 1;
            foreach (var g in area.Groups.OrderBy(g => g.SortOrder))
                g.SortOrder = i++;
            RefreshGroupsList();
        };
        btnMoveGroupUp.Click += (_, _) =>
        {
            if (areasList.SelectedItem is not TaskAreaState area || groupsList.SelectedItem is not TaskGroupState group)
                return;
            var ordered = area.Groups.OrderBy(g => g.SortOrder).ToList();
            int idx = ordered.IndexOf(group);
            if (idx <= 0)
                return;
            ordered.RemoveAt(idx);
            ordered.Insert(idx - 1, group);
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].SortOrder = i + 1;
            RefreshGroupsList(group.Id);
        };
        btnMoveGroupDown.Click += (_, _) =>
        {
            if (areasList.SelectedItem is not TaskAreaState area || groupsList.SelectedItem is not TaskGroupState group)
                return;
            var ordered = area.Groups.OrderBy(g => g.SortOrder).ToList();
            int idx = ordered.IndexOf(group);
            if (idx < 0 || idx >= ordered.Count - 1)
                return;
            ordered.RemoveAt(idx);
            ordered.Insert(idx + 1, group);
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].SortOrder = i + 1;
            RefreshGroupsList(group.Id);
        };
        txtGroupShortcut.LostFocus += (_, _) => ApplySelectedGroupShortcut();
        txtGroupShortcut.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                ApplySelectedGroupShortcut();
            }
        };
        txtGroupRetentionDays.LostFocus += (_, _) => ApplySelectedGroupRetentionDays();
        txtGroupRetentionDays.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                ApplySelectedGroupRetentionDays();
            }
        };
        cmbGroupRetentionHours.SelectionChanged += (_, _) =>
        {
            if (groupsList.SelectedItem is TaskGroupState group && cmbGroupRetentionHours.SelectedIndex >= 0)
                group.CompletedRetentionHours = cmbGroupRetentionHours.SelectedIndex;
        };

        chkGroupUndoneMark.Checked += (_, _) =>
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
                return;
            group.UndoneMarkEnabled = true;
            txtGroupUndoneMarkDays.IsEnabled = true;
            cmbGroupUndoneMarkHours.IsEnabled = true;
            txtGroupUndoneMarkDays.Text = NormalizeUndoneMarkDays(group.UndoneMarkDays)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            cmbGroupUndoneMarkHours.SelectedIndex = NormalizeUndoneMarkHours(group.UndoneMarkHours) - MinUndoneMarkHours;
        };
        chkGroupUndoneMark.Unchecked += (_, _) =>
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
                return;
            group.UndoneMarkEnabled = false;
            txtGroupUndoneMarkDays.IsEnabled = false;
            cmbGroupUndoneMarkHours.IsEnabled = false;
        };
        txtGroupUndoneMarkDays.LostFocus += (_, _) => ApplySelectedGroupUndoneMark();
        txtGroupUndoneMarkDays.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                ApplySelectedGroupUndoneMark();
            }
        };
        cmbGroupUndoneMarkHours.SelectionChanged += (_, _) =>
        {
            if (groupsList.SelectedItem is not TaskGroupState group)
                return;
            if (cmbGroupUndoneMarkHours.SelectedItem is ComboBoxItem item && item.Tag is int tag)
                group.UndoneMarkHours = tag;
        };

        RefreshAreasList(_currentTaskAreaId);

        tabControl.Items.Add(new TabItem
        {
            Header = "Task Panel",
            Content = new ScrollViewer { Content = taskPanelTab, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        });

        var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonPanel.Children.Add(btnOk);
        buttonPanel.Children.Add(btnCancel);
        DockPanel.SetDock(buttonPanel, Dock.Bottom);

        root.Children.Add(buttonPanel);
        root.Children.Add(tabControl);
        dlg.Content = root;

        btnBrowseBackup.Click += (_, _) =>
        {
            var fbd = new VistaFolderBrowserDialog
            {
                Description = "Choose folder for session backups",
                UseDescriptionForTitle = true
            };
            var cur = txtBackup.Text.Trim();
            try
            {
                if (!string.IsNullOrEmpty(cur))
                {
                    var full = Path.GetFullPath(cur);
                    if (Directory.Exists(full))
                        fbd.SelectedPath = full;
                    else
                    {
                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                            fbd.SelectedPath = parent;
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            if (fbd.ShowDialog(dlg) == true)
                txtBackup.Text = fbd.SelectedPath;
        };

        btnBrowseCloudBackup.Click += (_, _) =>
        {
            var fbd = new VistaFolderBrowserDialog
            {
                Description = "Choose folder for cloud backup copies",
                UseDescriptionForTitle = true
            };
            var cur = txtCloudBackup.Text.Trim();
            try
            {
                if (!string.IsNullOrEmpty(cur))
                {
                    var full = Path.GetFullPath(cur);
                    if (Directory.Exists(full))
                        fbd.SelectedPath = full;
                    else
                    {
                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                            fbd.SelectedPath = parent;
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            if (fbd.ShowDialog(dlg) == true)
                txtCloudBackup.Text = fbd.SelectedPath;
        };

        btnBrowseCloudPlainTabs.Click += (_, _) =>
        {
            var fbd = new VistaFolderBrowserDialog
            {
                Description = "Choose folder for plain text copies of each tab",
                UseDescriptionForTitle = true
            };
            var cur = txtCloudPlainTabs.Text.Trim();
            try
            {
                if (!string.IsNullOrEmpty(cur))
                {
                    var full = Path.GetFullPath(cur);
                    if (Directory.Exists(full))
                        fbd.SelectedPath = full;
                    else
                    {
                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                            fbd.SelectedPath = parent;
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            if (fbd.ShowDialog(dlg) == true)
                txtCloudPlainTabs.Text = fbd.SelectedPath;
        };

        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtBackup.Text))
            {
                MessageBox.Show("Backup folder cannot be empty.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtCloudBackup.Text))
            {
                MessageBox.Show("Cloud storage folder cannot be empty.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string backupPath;
            try
            {
                backupPath = Path.GetFullPath(txtBackup.Text.Trim());
            }
            catch
            {
                MessageBox.Show("Backup folder path is not valid.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string cloudBackupPath;
            try
            {
                cloudBackupPath = Path.GetFullPath(txtCloudBackup.Text.Trim());
            }
            catch
            {
                MessageBox.Show("Cloud storage folder path is not valid.", "Invalid settings", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string cloudPlainTabsFolderValue = string.Empty;
            if (!string.IsNullOrWhiteSpace(txtCloudPlainTabs.Text))
            {
                try
                {
                    cloudPlainTabsFolderValue = Path.GetFullPath(txtCloudPlainTabs.Text.Trim());
                }
                catch
                {
                    MessageBox.Show("Plain text tabs folder path is not valid.", "Invalid settings", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            if (chkCloudPlainTabs.IsChecked == true && string.IsNullOrWhiteSpace(cloudPlainTabsFolderValue))
            {
                MessageBox.Show(
                    "Plain text tabs folder cannot be empty when plain text tab sync is enabled.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var shortcutNewPrimary = txtShortcutNewPrimary.Text.Trim();
            var shortcutNewSecondary = txtShortcutNewSecondary.Text.Trim();
            var shortcutClose = txtShortcutClose.Text.Trim();
            var shortcutRename = txtShortcutRename.Text.Trim();
            var shortcutAddBlankLines = txtShortcutAddBlankLines.Text.Trim();
            var shortcutTrimTrailingEmptyLines = txtShortcutTrimTrailingEmptyLines.Text.Trim();
            var shortcutToggleHighlight = txtShortcutToggleHighlight.Text.Trim();
            var shortcutToggleCriticalHighlight = txtShortcutToggleCriticalHighlight.Text.Trim();
            var shortcutGoToLine = txtShortcutGoToLine.Text.Trim();
            var shortcutGoToTab = txtShortcutGoToTab.Text.Trim();
            var shortcutMidiPlayer = txtShortcutMidiPlayer.Text.Trim();

            if (string.IsNullOrWhiteSpace(shortcutNewPrimary)
                || string.IsNullOrWhiteSpace(shortcutClose)
                || string.IsNullOrWhiteSpace(shortcutRename)
                || string.IsNullOrWhiteSpace(shortcutAddBlankLines)
                || string.IsNullOrWhiteSpace(shortcutTrimTrailingEmptyLines)
                || string.IsNullOrWhiteSpace(shortcutToggleHighlight)
                || string.IsNullOrWhiteSpace(shortcutToggleCriticalHighlight)
                || string.IsNullOrWhiteSpace(shortcutGoToLine)
                || string.IsNullOrWhiteSpace(shortcutGoToTab)
                || string.IsNullOrWhiteSpace(shortcutMidiPlayer))
            {
                MessageBox.Show("Shortcut fields cannot be empty (except secondary New tab shortcut).",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseKeyGesture(shortcutNewPrimary, out _)
                || (!string.IsNullOrWhiteSpace(shortcutNewSecondary) && !TryParseKeyGesture(shortcutNewSecondary, out _))
                || !TryParseKeyGesture(shortcutClose, out _)
                || !TryParseKeyGesture(shortcutRename, out _)
                || !TryParseKeyGesture(shortcutAddBlankLines, out _)
                || !TryParseKeyGesture(shortcutTrimTrailingEmptyLines, out _)
                || !TryParseKeyGesture(shortcutToggleHighlight, out _)
                || !TryParseKeyGesture(shortcutToggleCriticalHighlight, out _)
                || !TryParseKeyGesture(shortcutGoToLine, out _)
                || !TryParseKeyGesture(shortcutGoToTab, out _)
                || !TryParseKeyGesture(shortcutMidiPlayer, out _))
            {
                MessageBox.Show("One or more shortcuts have an invalid format.\nUse values like Ctrl+N, Ctrl+Shift+N, F2.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var shortcutList = new List<string>
            {
                shortcutNewPrimary,
                shortcutClose,
                shortcutRename,
                shortcutAddBlankLines,
                shortcutTrimTrailingEmptyLines,
                shortcutToggleHighlight,
                shortcutToggleCriticalHighlight,
                shortcutGoToLine,
                shortcutGoToTab,
                shortcutMidiPlayer
            };
            if (!string.IsNullOrWhiteSpace(shortcutNewSecondary))
                shortcutList.Add(shortcutNewSecondary);
            shortcutList.Add(DefaultShortcutFakeSave);
            if (shortcutList.Count != shortcutList.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                MessageBox.Show("Shortcut keys must be unique across actions (Ctrl+S is reserved and does not save your work).", "Invalid settings",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ApplySelectedGroupShortcut())
                return;
            if (!ApplySelectedGroupRetentionDays())
                return;
            if (!ApplySelectedGroupUndoneMark())
                return;

            foreach (var area in workingTaskAreas)
            {
                foreach (var group in area.Groups)
                {
                    group.ShortcutKey = NormalizeTaskGroupShortcutKey(group.ShortcutKey);
                    group.CompletedRetentionDays = NormalizeCompletedRetentionDays(group.CompletedRetentionDays, group.Id);
                    group.CompletedRetentionHours = NormalizeCompletedRetentionHours(group.CompletedRetentionHours);
                    group.UndoneMarkEnabled = group.UndoneMarkEnabled == true;
                    group.UndoneMarkDays = NormalizeUndoneMarkDays(group.UndoneMarkDays);
                    group.UndoneMarkHours = NormalizeUndoneMarkHours(group.UndoneMarkHours);
                }

                var usedShortcuts = area.Groups
                    .Select(group => group.ShortcutKey ?? string.Empty)
                    .Where(shortcut => shortcut.Length > 0)
                    .ToList();
                if (usedShortcuts.Count != usedShortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                {
                    MessageBox.Show($"Area '{area.Name}' has duplicate group shortcut keys. Each group in an area must have a unique shortcut.",
                        "Task Panel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var selectedFancyBulletStyle = FancyBulletStyle.Dot;
            if (cmbBulletStyle.SelectedItem is ComboBoxItem selectedStyleItem
                && selectedStyleItem.Tag is FancyBulletStyle selectedStyle)
            {
                selectedFancyBulletStyle = selectedStyle;
            }

            var uptimeHeartbeatSeconds = 0;
            if (cmbUptimeHeartbeatMinutes.SelectedItem is ComboBoxItem uptimeItem
                && uptimeItem.Tag is int uptimeMinutes
                && UptimeHeartbeatIntervalMinutes.Contains(uptimeMinutes))
            {
                uptimeHeartbeatSeconds = uptimeMinutes * 60;
            }

            if (int.TryParse(txtAutoSave.Text, out int secs) && secs >= 5
                && uptimeHeartbeatSeconds > 0
                && int.TryParse(txtLines.Text, out int lines) && lines >= 1
                && double.TryParse(txtFontSize.Text, out double fsize) && fsize >= 6
                && !string.IsNullOrWhiteSpace(cmbFont.Text)
                && int.TryParse(txtVisualWrapColumn.Text, out int visualWrapColumn)
                && visualWrapColumn >= MinVisualLineWrapColumn
                && visualWrapColumn <= MaxVisualLineWrapColumn
                && cmbCloudHours.SelectedItem is int cloudHours && cloudHours >= 0 && cloudHours <= 50
                && cmbCloudMinutes.SelectedItem is int cloudMinutes && cloudMinutes >= 0
                && cloudMinutes <= 55 && cloudMinutes % 5 == 0
                && (cloudHours > 0 || cloudMinutes > 0)
                && TryParseColor(cmbSelectedLineColor.Text, out var selectedLineColor)
                && TryParseColor(cmbHighlightedLineColor.Text, out var highlightedLineColor)
                && TryParseColor(cmbSelectedHighlightedLineColor.Text, out var selectedHighlightedLineColor)
                && TryParseColor(cmbCriticalHighlightedLineColor.Text, out var criticalHighlightedLineColor)
                && TryParseColor(cmbSelectedCriticalHighlightedLineColor.Text, out var selectedCriticalHighlightedLineColor)
                && int.TryParse(txtTabStaleDays.Text, out int staleDays) && staleDays >= 1 && staleDays <= 3650
                && int.TryParse(txtClosedTabsMaxCount.Text, out int closedTabsMaxCount)
                && closedTabsMaxCount >= MinClosedTabsMaxCount && closedTabsMaxCount <= MaxClosedTabsMaxCount
                && int.TryParse(txtClosedTabsRetentionDays.Text, out int closedTabsRetentionDays)
                && closedTabsRetentionDays >= MinClosedTabsRetentionDays && closedTabsRetentionDays <= MaxClosedTabsRetentionDays)
            {
                _backupAdditionalIncludeSettingsFile = chkBackupAddSettings.IsChecked == true;
                _backupAdditionalIncludeAppLog = chkBackupAddLog.IsChecked == true;
                _backupAdditionalIncludeHeartbeatLogs = chkBackupAddHeartbeat.IsChecked == true;
                _backupAdditionalIncludeTodoItems = chkBackupAddTodos.IsChecked == true;
                _backupAdditionalIncludeStateConfig = chkBackupAddStateConfig.IsChecked == true;
                _backupAdditionalIncludeSessionState = chkBackupAddSessionState.IsChecked == true;
                _backupAdditionalIncludeSafePaste = chkBackupAddSafePaste.IsChecked == true;
                _backupAdditionalIncludeTimeReports = chkBackupAddTimeReports.IsChecked == true;
                _backupAdditionalIncludeProjectLineCounter = chkBackupAddProjectLineCounter.IsChecked == true;
                _backupAdditionalIncludeTaskPanel = chkBackupAddTaskPanel.IsChecked == true;
                _backupAdditionalIncludeAlarms = chkBackupAddAlarms.IsChecked == true;
                _backupAdditionalIncludeStandup = chkBackupAddStandup.IsChecked == true;
                _backupAdditionalIncludeMessageOverlay = chkBackupAddMessageOverlay.IsChecked == true;
                _backupAdditionalIncludeMidiCustomSongs = chkBackupAddMidi.IsChecked == true;
                _backupAdditionalIncludeImages = chkBackupAddImages.IsChecked == true;

                var previousBackupFolder = _backupFolder;
                if (!string.Equals(Path.GetFullPath(previousBackupFolder), Path.GetFullPath(backupPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    CopyClosedTabsFileToBackupFolder(previousBackupFolder, backupPath);
                    CopySearchFilesHistoryFileToBackupFolder(previousBackupFolder, backupPath);
                    CopySelectedAdditionalBackupArtifacts(previousBackupFolder, backupPath);
                }

                _backupFolder = backupPath;
                EnsureBackupImagesFolderExists();
                _inlineImageCache.Clear();
                _cloudBackupFolder = cloudBackupPath;
                _cloudSyncTabsPlainTextEnabled = chkCloudPlainTabs.IsChecked == true;
                _cloudSyncTabsPlainTextFolder = cloudPlainTabsFolderValue;
                _cloudSaveIntervalHours = cloudHours;
                _cloudSaveIntervalMinutes = cloudMinutes;
                _cloudSyncTabsPlainTextInstreamEnabled = _cloudSyncTabsPlainTextEnabled
                    && !string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextFolder)
                    && chkInstream.IsChecked == true;
                if (cmbInstreamHours.SelectedItem is int instreamHoursValue && instreamHoursValue >= 0 && instreamHoursValue <= 50)
                    _cloudSyncTabsPlainTextInstreamHours = instreamHoursValue;
                if (cmbInstreamMinutes.SelectedItem is int instreamMinutesValue
                    && instreamMinutesValue >= 0 && instreamMinutesValue <= 55 && instreamMinutesValue % 5 == 0)
                    _cloudSyncTabsPlainTextInstreamMinutes = instreamMinutesValue;
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(secs);
                _uptimeHeartbeatSeconds = uptimeHeartbeatSeconds;
                _writeUptimeHeartbeatInNoted = chkWriteUptimeHeartbeatInNoted.IsChecked == true;
                _useStandaloneHeartbeatApp = chkUseStandaloneHeartbeatApp.IsChecked == true;
                StartBackupHeartbeatTimer();
                _initialLines = lines;
                _fontFamily = cmbFont.Text.Trim();
                _fontSize = fsize;
                _fontWeight = weights[cmbFontWeight.SelectedIndex].Value;
                _shortcutNewPrimary = shortcutNewPrimary;
                _shortcutNewSecondary = shortcutNewSecondary;
                _shortcutCloseTab = shortcutClose;
                _shortcutRenameTab = shortcutRename;
                _shortcutAddBlankLines = shortcutAddBlankLines;
                _shortcutTrimTrailingEmptyLines = shortcutTrimTrailingEmptyLines;
                _shortcutToggleHighlight = shortcutToggleHighlight;
                _shortcutToggleCriticalHighlight = shortcutToggleCriticalHighlight;
                _shortcutGoToLine = shortcutGoToLine;
                _shortcutGoToTab = shortcutGoToTab;
                _shortcutMidiPlayer = shortcutMidiPlayer;
                _selectedLineColor = selectedLineColor;
                _highlightedLineColor = highlightedLineColor;
                _selectedHighlightedLineColor = selectedHighlightedLineColor;
                _criticalHighlightedLineColor = criticalHighlightedLineColor;
                _selectedCriticalHighlightedLineColor = selectedCriticalHighlightedLineColor;
                _isFridayFeelingEnabled = chkFridayFeeling.IsChecked == true;
                _isFredagspartySessionEnabled = untilCloseFredagsparty;
                _fancyBulletsEnabled = chkStyledBullets.IsChecked == true;
                _wrapLongLinesVisually = chkWrapLongLinesVisually.IsChecked == true;
                _visualLineWrapColumn = NormalizeVisualLineWrapColumn(visualWrapColumn);
                _showHorizontalRuler = chkShowHorizontalRuler.IsChecked == true;
                _showLineAssignments = chkShowLineAssignments.IsChecked == true;
                _showInlineImages = chkShowInlineImages.IsChecked == true;
                _fancyBulletStyle = selectedFancyBulletStyle;
                _externalBrowserForLinks = NormalizeExternalBrowserChoice(
                    cmbExternalBrowser.SelectedItem is ComboBoxItem browserItem && browserItem.Tag is ExternalBrowserChoice eb
                        ? eb
                        : ExternalBrowserChoice.Default);
                SyncExternalBrowserLauncherPreference();

                // Task Panel settings
                var newTitle = (txtTaskPanelTitle.Text ?? string.Empty).Trim();
                _taskPanelTitle = string.IsNullOrWhiteSpace(newTitle) ? DefaultTaskPanelTitle : newTitle;
                _taskAreas = workingTaskAreas;
                if (!_taskAreas.Any(a => string.Equals(a.Id, _currentTaskAreaId, StringComparison.OrdinalIgnoreCase)))
                    _currentTaskAreaId = _taskAreas.Count > 0 ? _taskAreas[0].Id : DefaultTaskAreaId;
                MigrateLegacyTodoItemsToGroups();
                UpdateTodoPanelTitleText();
                RefreshTodoAreaSelector();
                RenderTodoLists();

                _tabCleanupStaleDays = staleDays;
                _closedTabsMaxCount = closedTabsMaxCount;
                _closedTabsRetentionDays = closedTabsRetentionDays;
                _showBulletHoverTooltips = chkBulletHoverTooltips.IsChecked == true;
                _saveBulletsAsMarker = cmbSaveBulletsAs.SelectedItem is ComboBoxItem saveBulletItem
                    && saveBulletItem.Tag is char saveBulletTag
                    && saveBulletTag == '*'
                    ? '*'
                    : '-';
                SaveClosedTabHistory();

                // Apply font to all open editors
                var family = new FontFamily(_fontFamily);
                var weight = FontWeight.FromOpenTypeWeight(_fontWeight);
                foreach (var doc in _docs.Values)
                {
                    doc.Editor.FontFamily = family;
                    doc.Editor.FontSize = ClampedEditorDisplayFontSize();
                    doc.Editor.FontWeight = weight;
                    doc.Editor.TextArea.TextView.Redraw();
                }
                ApplyShortcutBindings();
                ApplyColorThemeToOpenEditors();
                ApplyFridayFeelingToOpenEditors();
                ApplyViewRenderingSettings();

                viewPreviewCommitted = true;
                SaveWindowSettings();
                dlg.DialogResult = true;
            }
            else
            {
                MessageBox.Show($"Auto-save must be >= 5 seconds.\nUptime heartbeat interval must be selected (1, 2, 3, 4, 5, 6, or 10 minutes — each divides evenly into one hour).\nInitial lines must be >= 1.\nFont size must be >= 6.\nVisual wrap column must be {MinVisualLineWrapColumn}-{MaxVisualLineWrapColumn}.\nCloud interval must be 0-50 hours and minutes in 5-minute steps (not 0h 0m).\nColor values must be valid WPF colors (name or #AARRGGBB).\nShortcuts must be valid key gestures.\nTab Cleanup stale days must be 1–3650.\nClosed tabs max count must be {MinClosedTabsMaxCount}–{MaxClosedTabsMaxCount}.\nClosed tab retention days must be {MinClosedTabsRetentionDays}–{MaxClosedTabsRetentionDays}.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        dlg.ShowDialog();
        if (!viewPreviewCommitted)
        {
            _fancyBulletsEnabled = originalFancyBulletsEnabled;
            _wrapLongLinesVisually = originalWrapLongLinesVisually;
            _visualLineWrapColumn = originalVisualLineWrapColumn;
            _showHorizontalRuler = originalShowHorizontalRuler;
            _showLineAssignments = originalShowLineAssignments;
            _showBulletHoverTooltips = originalShowBulletHoverTooltips;
            _showInlineImages = originalShowInlineImages;
            _fancyBulletStyle = originalFancyBulletStyle;
            _externalBrowserForLinks = originalExternalBrowserForLinks;
            SyncExternalBrowserLauncherPreference();
            _isFredagspartySessionEnabled = originalFredagspartySessionEnabled;
            _isFredagspartyTemporarilyDisabled = originalFredagspartyTemporarilyDisabled;
            ApplyFridayFeelingToOpenEditors();
            ApplyViewRenderingSettings();
        }
    }
}
