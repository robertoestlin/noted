using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const string DefaultQuickMessageColorHex = "#FF62E5FF";
    private const string QuickMessageButtonBackgroundHex = "#D9111827";
    private static readonly string[] DefaultQuickMessagePresets = ["GG", "AFK", "BRB"];
    private List<string> _quickMessagePresets = [.. DefaultQuickMessagePresets];
    private string _quickMessageCustom = string.Empty;
    private string _quickMessageColorHex = DefaultQuickMessageColorHex;

    private static readonly (string Name, string Hex)[] QuickMessageColorOptions =
    [
        ("Ocean Blue", "#FF62E5FF"),
        ("Lime Punch", "#FFB9FF66"),
        ("Sunset Orange", "#FFFF9962"),
        ("Neon Pink", "#FFFF66CC"),
        ("Classic White", "#FFF7FAFF")
    ];

    private void ResetQuickMessageOverlaySettings()
    {
        _quickMessagePresets = [.. DefaultQuickMessagePresets];
        _quickMessageCustom = string.Empty;
        _quickMessageColorHex = DefaultQuickMessageColorHex;
    }

    private List<string> BuildQuickMessagePresetsSnapshot()
    {
        var normalized = NormalizeQuickMessagePresets(_quickMessagePresets);
        return normalized.Count == 0 ? [.. DefaultQuickMessagePresets] : normalized;
    }

    private void ApplyQuickMessageOverlaySettings(WindowSettings state)
    {
        var presets = NormalizeQuickMessagePresets(state.QuickMessagePresets);
        _quickMessagePresets = presets.Count == 0 ? [.. DefaultQuickMessagePresets] : presets;
        _quickMessageCustom = (state.QuickMessageCustom ?? string.Empty).Trim();
        _quickMessageColorHex = NormalizeQuickMessageColorHex(state.QuickMessageColor);
    }

    private static List<string> NormalizeQuickMessagePresets(IEnumerable<string>? presets)
    {
        var result = new List<string>();
        if (presets == null)
            return result;

        foreach (var preset in presets)
        {
            var value = (preset ?? string.Empty).Trim();
            if (value.Length > 0)
                result.Add(value);
        }

        return result;
    }

    private static string NormalizeQuickMessageColorHex(string? colorHex)
    {
        if (TryParseColor(colorHex, out var color))
            return ColorToHex(color);
        return DefaultQuickMessageColorHex;
    }

    private Brush ResolveQuickMessageBrush()
    {
        if (TryParseColor(_quickMessageColorHex, out var color))
            return new SolidColorBrush(color);

        return (Brush)new BrushConverter().ConvertFromString(DefaultQuickMessageColorHex)!;
    }

    private static Brush ResolveQuickMessageButtonBackgroundBrush()
        => (Brush)new BrushConverter().ConvertFromString(QuickMessageButtonBackgroundHex)!;

    private void ShowQuickMessageOverlayDialog()
    {
        var dlg = new Window
        {
            Title = "Quick Message Overlay",
            Width = 620,
            Height = 420,
            MinWidth = 500,
            MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "Click a message button to show it full-screen",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray
        };
        header.Children.Add(title);
        var btnMessageSettings = new Button
        {
            Content = "⚙",
            Width = 30,
            Height = 30,
            ToolTip = "Edit message list",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(btnMessageSettings, 1);
        header.Children.Add(btnMessageSettings);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.Children.Add(new TextBlock
        {
            Text = "Color",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var cmbColor = new ComboBox();
        foreach (var option in QuickMessageColorOptions)
        {
            cmbColor.Items.Add(new ComboBoxItem
            {
                Content = option.Name,
                Tag = option.Hex
            });
        }

        var selectedColorIndex = Array.FindIndex(
            QuickMessageColorOptions,
            option => string.Equals(option.Hex, _quickMessageColorHex, StringComparison.OrdinalIgnoreCase));
        cmbColor.SelectedIndex = selectedColorIndex >= 0 ? selectedColorIndex : 0;

        Grid.SetColumn(cmbColor, 1);
        colorRow.Children.Add(cmbColor);
        DockPanel.SetDock(colorRow, Dock.Top);
        root.Children.Add(colorRow);

        var messagesWrap = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 10)
        };

        var customRow = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        customRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lblCustom = new TextBlock
        {
            Text = "Custom message",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        customRow.Children.Add(lblCustom);
        var txtCustom = new TextBox
        {
            MinWidth = 260,
            Margin = new Thickness(0, 0, 0, 6),
            Text = _quickMessageCustom
        };
        Grid.SetColumn(txtCustom, 1);
        Grid.SetRow(txtCustom, 0);
        var btnCustom = new Button
        {
            Padding = new Thickness(14, 8, 14, 8),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(btnCustom, 0);
        Grid.SetColumnSpan(btnCustom, 2);
        Grid.SetRow(btnCustom, 1);
        customRow.Children.Add(txtCustom);
        customRow.Children.Add(btnCustom);
        DockPanel.SetDock(customRow, Dock.Bottom);
        root.Children.Add(customRow);
        root.Children.Add(messagesWrap);

        void ShowAndClose(string text)
        {
            var message = string.IsNullOrWhiteSpace(text) ? "..." : text.Trim();
            ShowQuickMessageOverlay(message, ResolveQuickMessageBrush());
            dlg.Close();
        }

        void UpdateCustomButton()
        {
            var message = (txtCustom.Text ?? string.Empty).Trim();
            btnCustom.Content = message.Length == 0 ? "(empty)" : message;
            btnCustom.Foreground = ResolveQuickMessageBrush();
            btnCustom.Background = ResolveQuickMessageButtonBackgroundBrush();
            btnCustom.IsEnabled = message.Length > 0;
            btnCustom.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void RebuildPresetButtons()
        {
            messagesWrap.Children.Clear();
            var brush = ResolveQuickMessageBrush();
            var background = ResolveQuickMessageButtonBackgroundBrush();

            foreach (var message in _quickMessagePresets)
            {
                var button = new Button
                {
                    Content = message,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(14, 8, 14, 8),
                    MinWidth = 120,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = brush,
                    Background = background
                };
                button.Click += (_, _) => ShowAndClose(message);
                messagesWrap.Children.Add(button);
            }

            if (messagesWrap.Children.Count == 0)
            {
                messagesWrap.Children.Add(new TextBlock
                {
                    Text = "No preset messages. Use the settings icon to add some.",
                    Foreground = Brushes.IndianRed
                });
            }

            UpdateCustomButton();
        }

        string GetSelectedColorHex()
        {
            return cmbColor.SelectedItem is ComboBoxItem item && item.Tag is string hex
                ? hex
                : DefaultQuickMessageColorHex;
        }

        btnMessageSettings.Click += (_, _) =>
        {
            ShowQuickMessageListSettingsDialog(dlg);
            RebuildPresetButtons();
        };

        cmbColor.SelectionChanged += (_, _) =>
        {
            _quickMessageColorHex = NormalizeQuickMessageColorHex(GetSelectedColorHex());
            RebuildPresetButtons();
            SaveWindowSettings();
        };

        txtCustom.TextChanged += (_, _) =>
        {
            _quickMessageCustom = (txtCustom.Text ?? string.Empty).Trim();
            UpdateCustomButton();
        };

        btnCustom.Click += (_, _) =>
        {
            _quickMessageCustom = (txtCustom.Text ?? string.Empty).Trim();
            SaveWindowSettings();
            ShowAndClose(_quickMessageCustom);
        };

        dlg.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dlg.Close();
                return;
            }

            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                var count = cmbColor.Items.Count;
                if (count == 0)
                    return;
                var next = (cmbColor.SelectedIndex + (e.Key == Key.Down ? 1 : -1) + count) % count;
                cmbColor.SelectedIndex = next;
                e.Handled = true;
            }
        };
        dlg.Closing += (_, _) =>
        {
            _quickMessageCustom = (txtCustom.Text ?? string.Empty).Trim();
            SaveWindowSettings();
        };

        RebuildPresetButtons();
        dlg.Content = root;
        dlg.ShowDialog();
    }

    private bool? ShowQuickMessageListSettingsDialog(Window owner)
    {
        var dlg = new Window
        {
            Title = "Quick Message Settings",
            Width = 480,
            Height = 380,
            MinWidth = 380,
            MinHeight = 300,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // Layout: list on the left, icon button column on the right, input at bottom
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var list = new ListBox { Margin = new Thickness(0, 0, 6, 0) };
        foreach (var preset in _quickMessagePresets)
            list.Items.Add(preset);
        Grid.SetRow(list, 0);
        Grid.SetColumn(list, 0);
        root.Children.Add(list);

        static Button MakeIconButton(string icon, string tooltip, bool enabled = true) => new()
        {
            Content = icon,
            Width = 30,
            Height = 30,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = tooltip,
            IsEnabled = enabled
        };

        var btnAdd    = MakeIconButton("+", "Add");
        var btnUpdate = MakeIconButton("✎", "Update selected", enabled: false);
        var btnRemove = MakeIconButton("−", "Remove selected", enabled: false);
        var btnReset  = MakeIconButton("↺", "Reset defaults");

        var iconStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top
        };
        iconStack.Children.Add(btnAdd);
        iconStack.Children.Add(btnUpdate);
        iconStack.Children.Add(btnRemove);

        var resetStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        resetStack.Children.Add(btnReset);

        var sidePanel = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(resetStack, Dock.Bottom);
        sidePanel.Children.Add(resetStack);
        sidePanel.Children.Add(iconStack);

        Grid.SetRow(sidePanel, 0);
        Grid.SetColumn(sidePanel, 1);
        root.Children.Add(sidePanel);

        var txtMessage = new TextBox { Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(txtMessage, 1);
        Grid.SetColumn(txtMessage, 0);
        Grid.SetColumnSpan(txtMessage, 2);
        root.Children.Add(txtMessage);

        void CommitPresets()
        {
            var lines = new List<string>();
            foreach (var item in list.Items)
            {
                var text = (item?.ToString() ?? string.Empty).Trim();
                if (text.Length > 0)
                    lines.Add(text);
            }
            _quickMessagePresets = lines.Count == 0 ? [.. DefaultQuickMessagePresets] : lines;
            SaveWindowSettings();
        }

        void RefreshButtonState()
        {
            var hasSelection = list.SelectedItem is string;
            btnRemove.IsEnabled = hasSelection;
            btnUpdate.IsEnabled = hasSelection;
            btnAdd.IsEnabled = (txtMessage.Text ?? string.Empty).Trim().Length > 0;
        }

        void AddCurrentText()
        {
            var message = (txtMessage.Text ?? string.Empty).Trim();
            if (message.Length == 0)
                return;

            list.Items.Add(message);
            CommitPresets();
            txtMessage.Clear();
            list.SelectedItem = null;
            txtMessage.Focus();
            RefreshButtonState();
        }

        void UpdateSelectedText()
        {
            var message = (txtMessage.Text ?? string.Empty).Trim();
            if (message.Length == 0 || list.SelectedIndex < 0)
                return;

            list.Items[list.SelectedIndex] = message;
            CommitPresets();
            txtMessage.Clear();
            list.SelectedItem = null;
            txtMessage.Focus();
            RefreshButtonState();
        }

        txtMessage.TextChanged += (_, _) => RefreshButtonState();

        btnAdd.Click += (_, _) => AddCurrentText();
        btnUpdate.Click += (_, _) => UpdateSelectedText();

        txtMessage.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            if (list.SelectedItem is string)
                UpdateSelectedText();
            else
                AddCurrentText();
        };

        btnRemove.Click += (_, _) =>
        {
            var idx = list.SelectedIndex;
            if (idx < 0)
                return;
            list.Items.RemoveAt(idx);
            CommitPresets();
            list.SelectedIndex = idx < list.Items.Count ? idx : list.Items.Count - 1;
            if (list.SelectedIndex < 0)
                txtMessage.Clear();
            RefreshButtonState();
        };

        btnReset.Click += (_, _) =>
        {
            list.Items.Clear();
            foreach (var preset in DefaultQuickMessagePresets)
                list.Items.Add(preset);
            list.SelectedIndex = -1;
            txtMessage.Clear();
            CommitPresets();
            RefreshButtonState();
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string selected)
                txtMessage.Text = selected;
            else
                txtMessage.Clear();
            RefreshButtonState();
        };

        dlg.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            dlg.Close();
        };

        dlg.Loaded += (_, _) =>
        {
            txtMessage.Focus();
            RefreshButtonState();
        };

        dlg.Content = root;
        return dlg.ShowDialog();
    }

    private void ShowQuickMessageOverlay(string text, Brush foreground)
    {
        MessageOverlayText.Text = text;
        MessageOverlayText.Foreground = foreground;
        MessageOverlay.Visibility = Visibility.Visible;
        MessageOverlay.Focus();
        Keyboard.Focus(MessageOverlay);
    }

    private void HideQuickMessageOverlay()
    {
        MessageOverlay.Visibility = Visibility.Collapsed;
    }

    private void MessageOverlay_DismissByMouseDown(object sender, MouseButtonEventArgs e)
    {
        HideQuickMessageOverlay();
        e.Handled = true;
    }

    private void MessageOverlay_DismissByKeyDown(object sender, KeyEventArgs e)
    {
        HideQuickMessageOverlay();
        e.Handled = true;
    }
}
