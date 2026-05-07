using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Noted.Models;
using Noted.Services;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow
{
    private readonly ScriptPackService _scriptPackService = new();
    private readonly BuiltinScriptPackComposer _builtinScriptPackComposer = new();

    /// <summary>
    /// Writes the shipped built-in pack to <c>{BackupFolder}/script-packages/builtin.script-pack</c>
    /// when the shipped pack version is newer (or the file is missing). User-authored packs are
    /// never touched.
    /// </summary>
    private void EnsureBuiltinScriptPackUpToDate()
    {
        try
        {
            var pack = _builtinScriptPackComposer.Compose();
            if (pack == null) return;

            _scriptPackService.EnsureFolderExists(_backupFolder);
            var targetPath = _scriptPackService.GetBuiltinPackPath(_backupFolder);
            var installedVersion = _scriptPackService.ReadPackVersion(targetPath);

            if (pack.Version > installedVersion)
                _scriptPackService.WritePack(targetPath, ScriptPackService.SerializePack(pack));
        }
        catch
        {
            // Best effort — never block startup on this.
        }
    }

    private void ShowScriptsDialog()
    {
        EnsureBuiltinScriptPackUpToDate();

        var dlg = new Window
        {
            Title = "Scripts",
            Width = 1200,
            Height = 720,
            MinWidth = 900,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var packs = _scriptPackService.LoadAllPacks(_backupFolder);

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var btnInstallAll = new Button
        {
            Content = "📥",
            Width = 30,
            Height = 26,
            Padding = new Thickness(0),
            FontSize = 14,
            ToolTip = "Install many scripts from this pack into a folder...",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        DockPanel.SetDock(btnInstallAll, Dock.Left);
        bottom.Children.Add(btnInstallAll);

        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        bottom.Children.Add(btnClose);
        root.Children.Add(bottom);

        if (packs.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No script packs found in " + _scriptPackService.GetSubfolderPath(_backupFolder),
                Foreground = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            dlg.Content = root;
            dlg.ShowDialog();
            return;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(420),
            MinWidth = 200
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left column: pack combo (top) + scripts list.
        var left = new DockPanel();
        Grid.SetColumn(left, 0);

        var packCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(packCombo, Dock.Top);
        foreach (var (fileName, pack) in packs)
        {
            var label = string.IsNullOrWhiteSpace(pack.Name) ? fileName : pack.Name;
            packCombo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = pack,
                ToolTip = fileName + "  (v" + pack.Version + ")"
            });
        }
        left.Children.Add(packCombo);

        var scriptsList = new ListBox
        {
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetIsSharedSizeScope(scriptsList, true);
        left.Children.Add(scriptsList);
        grid.Children.Add(left);

        var splitter = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        // Right column: header + toolbar + body.
        var right = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(right, 2);

        var titleBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(titleBlock, Dock.Top);
        right.Children.Add(titleBlock);

        var languageBlock = new TextBlock
        {
            Foreground = Brushes.DimGray,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(languageBlock, Dock.Top);
        right.Children.Add(languageBlock);

        var descriptionBlock = new TextBlock
        {
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(descriptionBlock, Dock.Top);
        right.Children.Add(descriptionBlock);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        var btnCopy = new Button
        {
            Content = "📋 Copy",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Copy script body to clipboard"
        };
        var btnInstall = new Button
        {
            Content = "Install...",
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Save script to a file on disk"
        };
        toolbar.Children.Add(btnCopy);
        toolbar.Children.Add(btnInstall);
        right.Children.Add(toolbar);

        var statusBlock = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(statusBlock, Dock.Bottom);
        right.Children.Add(statusBlock);

        var bodyBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7))
        };
        right.Children.Add(bodyBox);

        grid.Children.Add(right);
        root.Children.Add(grid);

        ScriptItem? currentItem = null;
        ScriptPack? currentPack = null;

        void ShowScript(ScriptItem? item)
        {
            currentItem = item;
            if (item == null)
            {
                titleBlock.Text = string.Empty;
                languageBlock.Text = string.Empty;
                descriptionBlock.Text = string.Empty;
                bodyBox.Text = string.Empty;
                btnCopy.IsEnabled = false;
                btnInstall.IsEnabled = false;
                return;
            }
            titleBlock.Text = item.Title;
            languageBlock.Text = string.IsNullOrWhiteSpace(item.Language) ? string.Empty : item.Language;
            descriptionBlock.Text = item.Description;
            bodyBox.Text = item.Body;
            btnCopy.IsEnabled = true;
            btnInstall.IsEnabled = true;
        }

        void ShowPack(ScriptPack pack)
        {
            currentPack = pack;
            scriptsList.Items.Clear();
            foreach (var s in pack.Scripts)
                scriptsList.Items.Add(new ListBoxItem { Content = BuildScriptRow(s), Tag = s });
            btnInstallAll.IsEnabled = pack.Scripts.Count > 0;
            if (scriptsList.Items.Count > 0)
                scriptsList.SelectedIndex = 0;
            else
                ShowScript(null);
        }

        scriptsList.SelectionChanged += (_, _) =>
        {
            if (scriptsList.SelectedItem is ListBoxItem item && item.Tag is ScriptItem script)
                ShowScript(script);
            else
                ShowScript(null);
        };

        packCombo.SelectionChanged += (_, _) =>
        {
            if (packCombo.SelectedItem is ComboBoxItem item && item.Tag is ScriptPack pack)
                ShowPack(pack);
        };

        btnCopy.Click += (_, _) =>
        {
            if (currentItem == null) return;
            try
            {
                Clipboard.SetText(currentItem.Body ?? string.Empty);
                statusBlock.Text = "Copied to clipboard.";
                statusBlock.Foreground = Brushes.SeaGreen;
            }
            catch (Exception ex)
            {
                statusBlock.Text = "Copy failed: " + ex.Message;
                statusBlock.Foreground = Brushes.IndianRed;
            }
        };

        btnInstall.Click += (_, _) =>
        {
            if (currentItem == null) return;
            var save = new SaveFileDialog
            {
                FileName = string.IsNullOrWhiteSpace(currentItem.Filename)
                    ? SuggestFileName(currentItem)
                    : currentItem.Filename,
                Filter = "All files (*.*)|*.*",
                Title = "Install script"
            };
            if (save.ShowDialog(dlg) != true) return;
            try
            {
                File.WriteAllText(save.FileName, currentItem.Body ?? string.Empty,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                statusBlock.Text = "Saved to " + save.FileName;
                statusBlock.Foreground = Brushes.SeaGreen;
            }
            catch (Exception ex)
            {
                statusBlock.Text = "Save failed: " + ex.Message;
                statusBlock.Foreground = Brushes.IndianRed;
            }
        };

        btnInstallAll.Click += (_, _) =>
        {
            if (currentPack == null || currentPack.Scripts.Count == 0) return;
            ShowBulkInstallDialog(dlg, currentPack, (text, brush) =>
            {
                statusBlock.Text = text;
                statusBlock.Foreground = brush;
            });
        };

        packCombo.SelectedIndex = 0;
        ShowPack(packs[0].Pack);

        dlg.Content = root;
        dlg.ShowDialog();
    }

    private void ShowBulkInstallDialog(Window owner, ScriptPack pack, Action<string, Brush> reportStatus)
    {
        var dlg = new Window
        {
            Title = "Install scripts",
            Width = 520,
            Height = 520,
            MinWidth = 420,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.CanResize
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        // Folder row (top).
        var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(folderRow, Dock.Top);

        var folderLabel = new TextBlock
        {
            Text = "Folder:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(folderLabel, 0);
        folderRow.Children.Add(folderLabel);

        var txtFolder = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(txtFolder, 1);
        folderRow.Children.Add(txtFolder);

        var btnBrowse = new Button
        {
            Content = "Browse...",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(btnBrowse, 2);
        folderRow.Children.Add(btnBrowse);
        root.Children.Add(folderRow);

        // Select-all row (above the list, sits between folder and list).
        var selectRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(selectRow, Dock.Top);
        selectRow.Children.Add(new TextBlock
        {
            Text = "Scripts to install:",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 0)
        });
        var btnAll = new Button
        {
            Content = "All",
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0, 4, 0)
        };
        var btnNone = new Button
        {
            Content = "None",
            Padding = new Thickness(10, 2, 10, 2)
        };
        selectRow.Children.Add(btnAll);
        selectRow.Children.Add(btnNone);
        root.Children.Add(selectRow);

        // Bottom button row.
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var btnInstall = new Button
        {
            Content = "Install",
            Width = 100,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            IsCancel = true
        };
        bottom.Children.Add(btnInstall);
        bottom.Children.Add(btnCancel);
        root.Children.Add(bottom);

        // Checklist (fills remaining space).
        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };
        var listStack = new StackPanel();
        listScroll.Content = listStack;

        var checkBoxes = new List<(CheckBox Box, ScriptItem Item)>();
        foreach (var script in pack.Scripts)
        {
            var cb = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(script.Title) ? script.Filename : script.Title,
                IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2)
            };
            listStack.Children.Add(cb);
            checkBoxes.Add((cb, script));
        }
        root.Children.Add(listScroll);

        btnAll.Click += (_, _) => { foreach (var (cb, _) in checkBoxes) cb.IsChecked = true; };
        btnNone.Click += (_, _) => { foreach (var (cb, _) in checkBoxes) cb.IsChecked = false; };

        btnBrowse.Click += (_, _) =>
        {
            var picker = new VistaFolderBrowserDialog
            {
                Description = "Select folder to install scripts into",
                UseDescriptionForTitle = true
            };
            var current = (txtFolder.Text ?? string.Empty).Trim();
            try
            {
                if (current.Length > 0)
                {
                    var full = Path.GetFullPath(current);
                    if (Directory.Exists(full)) picker.SelectedPath = full;
                }
            }
            catch { /* ignore invalid pre-fill */ }

            if (picker.ShowDialog(dlg) == true)
                txtFolder.Text = picker.SelectedPath;
        };

        btnInstall.Click += (_, _) =>
        {
            var folder = (txtFolder.Text ?? string.Empty).Trim();
            if (folder.Length == 0)
            {
                MessageBox.Show(dlg, "Choose a destination folder.", "Install scripts",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = checkBoxes
                .Where(p => p.Box.IsChecked == true)
                .Select(p => p.Item)
                .ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(dlg, "Select at least one script to install.", "Install scripts",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(dlg, "Could not create folder: " + ex.Message, "Install scripts",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var plan = selected
                .Select(s => new
                {
                    Item = s,
                    Path = Path.Combine(folder, string.IsNullOrWhiteSpace(s.Filename) ? SuggestFileName(s) : s.Filename)
                })
                .ToList();

            var conflicts = plan.Where(p => File.Exists(p.Path)).ToList();
            if (conflicts.Count > 0)
            {
                var conflictNames = string.Join("\n  ", conflicts.Select(c => Path.GetFileName(c.Path)));
                var answer = MessageBox.Show(dlg,
                    $"{conflicts.Count} file(s) already exist in this folder:\n  {conflictNames}\n\nOverwrite?",
                    "Install scripts",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            int written = 0;
            var failures = new List<string>();
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            foreach (var entry in plan)
            {
                try
                {
                    File.WriteAllText(entry.Path, entry.Item.Body ?? string.Empty, encoding);
                    written++;
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(entry.Path) + ": " + ex.Message);
                }
            }

            dlg.DialogResult = true;
            dlg.Close();

            if (failures.Count == 0)
            {
                reportStatus($"Installed {written} script(s) to {folder}", Brushes.SeaGreen);
            }
            else
            {
                reportStatus(
                    $"Installed {written} of {plan.Count}; {failures.Count} failed. First error: {failures[0]}",
                    Brushes.IndianRed);
            }
        };

        dlg.Content = root;
        dlg.ShowDialog();
    }

    /// <summary>
    /// Renders one script-list row as <c>name --- tagline</c>. The first column uses
    /// <see cref="Grid.IsSharedSizeScopeProperty"/> so the <c>---</c> separator and
    /// taglines line up across all rows in the pack.
    /// </summary>
    private static Grid BuildScriptRow(ScriptItem s)
    {
        var (name, tagline) = SplitScriptHeader(s);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "ScriptName"
        });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nameBlock = new TextBlock { Text = name };
        Grid.SetColumn(nameBlock, 0);
        row.Children.Add(nameBlock);

        var sepBlock = new Border
        {
            Width = 24,
            Height = 1,
            Background = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(sepBlock, 1);
        row.Children.Add(sepBlock);

        var taglineBlock = new TextBlock
        {
            Text = tagline,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.DimGray
        };
        Grid.SetColumn(taglineBlock, 2);
        row.Children.Add(taglineBlock);

        return row;
    }

    private static (string Name, string Tagline) SplitScriptHeader(ScriptItem s)
    {
        var title = s.Title ?? string.Empty;
        // Match either em-dash, en-dash, or hyphen with surrounding spaces.
        foreach (var sep in new[] { " — ", " – ", " - " })
        {
            var idx = title.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
                return (title[..idx].Trim(), title[(idx + sep.Length)..].Trim());
        }
        var name = string.IsNullOrWhiteSpace(s.Filename) ? title : s.Filename;
        return (name, s.Description ?? string.Empty);
    }

    private static string SuggestFileName(ScriptItem item)
    {
        var baseName = string.IsNullOrWhiteSpace(item.Title) ? "script" : item.Title;
        var safe = new StringBuilder(baseName.Length);
        foreach (var c in baseName)
            safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        var ext = item.Language.ToLowerInvariant() switch
        {
            "powershell" or "ps1" => ".ps1",
            "bash" or "sh" or "shell" => ".sh",
            "python" or "py" => ".py",
            "javascript" or "js" => ".js",
            "typescript" or "ts" => ".ts",
            "csharp" or "cs" => ".cs",
            "batch" or "cmd" or "bat" => ".bat",
            "sql" => ".sql",
            _ => ".txt"
        };
        return safe.ToString() + ext;
    }
}
