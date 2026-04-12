using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Noted.Models;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow
{
    private void ShowSettingsDialog()
    {
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

            SaveSession(
                updateStatus: true,
                forceCloudBackup: true,
                cloudBackupFolderOverride: cloudBackupPath,
                persistCloudMetadata: false);
            txtLastCloudCopy.Text = $"Last cloud copy: {FormatCloudCopyTimestamp(_lastCloudSaveUtc)}";
            RefreshGlobalDirtyStatus();
        };
        backupPanel.Children.Add(btnCloudSaveNow);

        backupPanel.Children.Add(new TextBlock { Text = "Auto-save interval (seconds):" });
        var txtAutoSave = new TextBox { Text = ((int)_autoSaveTimer.Interval.TotalSeconds).ToString(), Margin = new Thickness(0, 4, 0, 10) };
        backupPanel.Children.Add(txtAutoSave);
        backupPanel.Children.Add(new TextBlock { Text = "Uptime heartbeat interval (seconds):" });
        var txtUptimeHeartbeat = new TextBox { Text = _uptimeHeartbeatSeconds.ToString(), Margin = new Thickness(0, 4, 0, 10) };
        backupPanel.Children.Add(txtUptimeHeartbeat);
        backupPanel.Children.Add(new TextBlock { Text = "Initial lines per new tab:" });
        var txtLines = new TextBox { Text = _initialLines.ToString(), Margin = new Thickness(0, 4, 0, 0) };
        backupPanel.Children.Add(txtLines);
        tabControl.Items.Add(new TabItem { Header = "Backup", Content = new ScrollViewer { Content = backupPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

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

        string[] colorOptions =
        [
            "Orange",
            "Goldenrod",
            "Tomato",
            "SkyBlue",
            "LightSkyBlue",
            "Khaki",
            "LightGreen",
            "PaleVioletRed",
            "#FFE1F0FF",
            "#FFFFF4B3",
            "#FFFFEA80"
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

        shortkeysPanel.Children.Add(new TextBlock { Text = "Go to line:" });
        var txtShortcutGoToLine = new TextBox { Text = _shortcutGoToLine, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutGoToLine);

        shortkeysPanel.Children.Add(new TextBlock { Text = "Go to tab:" });
        var txtShortcutGoToTab = new TextBox { Text = _shortcutGoToTab, Margin = new Thickness(0, 4, 0, 8) };
        shortkeysPanel.Children.Add(txtShortcutGoToTab);

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

        var fridayPanel = new StackPanel { Margin = new Thickness(12) };
        var chkFridayFeeling = new CheckBox
        {
            Content = "Enable Fredagsparty background automatically on Fridays",
            IsChecked = _isFridayFeelingEnabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkFredagsparty = new CheckBox
        {
            Content = "Enable Fredagsparty until app closes",
            IsChecked = _isFredagspartySessionEnabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var btnTurnOffFredagspartyTemporarily = new Button
        {
            Content = "Turn off Fredagsparty until restart",
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6)
        };
        btnTurnOffFredagspartyTemporarily.IsEnabled = !_isFredagspartyTemporarilyDisabled;
        btnTurnOffFredagspartyTemporarily.Click += (_, _) =>
        {
            _isFredagspartyTemporarilyDisabled = true;
            ApplyFridayFeelingToOpenEditors();
            btnTurnOffFredagspartyTemporarily.IsEnabled = false;
        };
        fridayPanel.Children.Add(chkFridayFeeling);
        fridayPanel.Children.Add(chkFredagsparty);
        fridayPanel.Children.Add(btnTurnOffFredagspartyTemporarily);
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
        tabControl.Items.Add(new TabItem
        {
            Header = "Tabs",
            Content = new ScrollViewer { Content = tabsSettingsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
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

            var shortcutNewPrimary = txtShortcutNewPrimary.Text.Trim();
            var shortcutNewSecondary = txtShortcutNewSecondary.Text.Trim();
            var shortcutClose = txtShortcutClose.Text.Trim();
            var shortcutRename = txtShortcutRename.Text.Trim();
            var shortcutAddBlankLines = txtShortcutAddBlankLines.Text.Trim();
            var shortcutTrimTrailingEmptyLines = txtShortcutTrimTrailingEmptyLines.Text.Trim();
            var shortcutToggleHighlight = txtShortcutToggleHighlight.Text.Trim();
            var shortcutGoToLine = txtShortcutGoToLine.Text.Trim();
            var shortcutGoToTab = txtShortcutGoToTab.Text.Trim();

            if (string.IsNullOrWhiteSpace(shortcutNewPrimary)
                || string.IsNullOrWhiteSpace(shortcutClose)
                || string.IsNullOrWhiteSpace(shortcutRename)
                || string.IsNullOrWhiteSpace(shortcutAddBlankLines)
                || string.IsNullOrWhiteSpace(shortcutTrimTrailingEmptyLines)
                || string.IsNullOrWhiteSpace(shortcutToggleHighlight)
                || string.IsNullOrWhiteSpace(shortcutGoToLine)
                || string.IsNullOrWhiteSpace(shortcutGoToTab))
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
                || !TryParseKeyGesture(shortcutGoToLine, out _)
                || !TryParseKeyGesture(shortcutGoToTab, out _))
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
                shortcutGoToLine,
                shortcutGoToTab
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

            if (int.TryParse(txtAutoSave.Text, out int secs) && secs >= 5
                && int.TryParse(txtUptimeHeartbeat.Text, out int uptimeHeartbeatSeconds)
                && uptimeHeartbeatSeconds >= 60 && uptimeHeartbeatSeconds <= 3600
                && 3600 % uptimeHeartbeatSeconds == 0
                && int.TryParse(txtLines.Text, out int lines) && lines >= 1
                && double.TryParse(txtFontSize.Text, out double fsize) && fsize >= 6
                && !string.IsNullOrWhiteSpace(cmbFont.Text)
                && cmbCloudHours.SelectedItem is int cloudHours && cloudHours >= 0 && cloudHours <= 50
                && cmbCloudMinutes.SelectedItem is int cloudMinutes && cloudMinutes >= 0
                && cloudMinutes <= 55 && cloudMinutes % 5 == 0
                && (cloudHours > 0 || cloudMinutes > 0)
                && TryParseColor(cmbSelectedLineColor.Text, out var selectedLineColor)
                && TryParseColor(cmbHighlightedLineColor.Text, out var highlightedLineColor)
                && TryParseColor(cmbSelectedHighlightedLineColor.Text, out var selectedHighlightedLineColor)
                && int.TryParse(txtTabStaleDays.Text, out int staleDays) && staleDays >= 1 && staleDays <= 3650)
            {
                var previousBackupFolder = _backupFolder;
                if (!string.Equals(Path.GetFullPath(previousBackupFolder), Path.GetFullPath(backupPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    CopySettingsFileToBackupFolder(previousBackupFolder, backupPath);
                    CopyClosedTabsFileToBackupFolder(previousBackupFolder, backupPath);
                    CopyImageFolderToBackupFolder(previousBackupFolder, backupPath);
                }

                _backupFolder = backupPath;
                EnsureBackupImagesFolderExists();
                _inlineImageCache.Clear();
                _cloudBackupFolder = cloudBackupPath;
                _cloudSaveIntervalHours = cloudHours;
                _cloudSaveIntervalMinutes = cloudMinutes;
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(secs);
                _uptimeHeartbeatSeconds = uptimeHeartbeatSeconds;
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
                _shortcutGoToLine = shortcutGoToLine;
                _shortcutGoToTab = shortcutGoToTab;
                _selectedLineColor = selectedLineColor;
                _highlightedLineColor = highlightedLineColor;
                _selectedHighlightedLineColor = selectedHighlightedLineColor;
                _isFridayFeelingEnabled = chkFridayFeeling.IsChecked == true;
                _isFredagspartySessionEnabled = chkFredagsparty.IsChecked == true;
                _tabCleanupStaleDays = staleDays;
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

                SaveWindowSettings();
                dlg.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Auto-save must be >= 5 seconds.\nUptime heartbeat must be 60-3600 seconds and divide evenly into 3600 (3600/value must be a whole number).\nInitial lines must be >= 1.\nFont size must be >= 6.\nCloud interval must be 0-50 hours and minutes in 5-minute steps (not 0h 0m).\nColor values must be valid WPF colors (name or #AARRGGBB).\nShortcuts must be valid key gestures.\nTab Cleanup stale days must be 1–3650.",
                    "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        dlg.ShowDialog();
    }
}
