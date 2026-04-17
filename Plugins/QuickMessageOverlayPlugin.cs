using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const string DefaultQuickMessageColorHex = "#FFFF66CC";
    private const string QuickMessageButtonBackgroundHex = "#D9111827";
    private static readonly string[] DefaultQuickMessagePresets = ["GG", "AFK", "BRB", "GLHF", "LOL"];
    private const int DefaultMessageOverlayBlinkIntervalMs = 1000;
    private const int DefaultMessageOverlayFadeMs = 2000;
    private const int MinMessageOverlayTimingMs = 50;
    private const int MaxMessageOverlayTimingMs = 20000;
    private const int CharacterBlinkTickMs = 40;
    private const string BlinkModeWholeText = "whole-text";
    private const string BlinkModeCharacterSweep = "character-sweep";
    private List<string> _quickMessagePresets = [.. DefaultQuickMessagePresets];
    private string _quickMessageCustom = string.Empty;
    private string _quickMessageColorHex = DefaultQuickMessageColorHex;
    private bool _isMessageOverlayBlinking;
    private int _messageOverlayBlinkIntervalMs = DefaultMessageOverlayBlinkIntervalMs;
    private int _messageOverlayFadeMs = DefaultMessageOverlayFadeMs;
    private string _messageOverlayBlinkMode = BlinkModeWholeText;
    private DispatcherTimer? _messageOverlayCharacterTimer;
    private int _messageOverlayCharacterFadeMs;
    private int _messageOverlayCharacterHoldMs;
    private string _messageOverlayCharacterBaseText = string.Empty;
    private Brush _messageOverlayCharacterForeground = Brushes.White;
    private int _messageOverlayCharacterVisibleChars;
    private int _messageOverlayActiveColorIndex = -1;

    private static readonly (string Name, string Hex)[] QuickMessageColorOptions =
    [
        ("Ocean Blue", "#FF62E5FF"),
        ("Lime Punch", "#FFB9FF66"),
        ("Sunset Orange", "#FFFF9962"),
        ("Neon Pink", "#FFFF66CC"),
        ("Classic White", "#FFF7FAFF")
    ];
    private static readonly (string Label, string Value)[] MessageOverlayBlinkModes =
    [
        ("Whole text", BlinkModeWholeText),
        ("Character sweep", BlinkModeCharacterSweep)
    ];

    private void ResetQuickMessageOverlaySettings()
    {
        _quickMessagePresets = [.. DefaultQuickMessagePresets];
        _quickMessageCustom = string.Empty;
        _quickMessageColorHex = DefaultQuickMessageColorHex;
        _messageOverlayBlinkIntervalMs = DefaultMessageOverlayBlinkIntervalMs;
        _messageOverlayFadeMs = DefaultMessageOverlayFadeMs;
        _messageOverlayBlinkMode = BlinkModeWholeText;
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
        _messageOverlayBlinkIntervalMs = NormalizeMessageOverlayTimingMs(
            state.MessageOverlayBlinkIntervalMs ?? state.MessageOverlayHoldMs,
            DefaultMessageOverlayBlinkIntervalMs);
        _messageOverlayFadeMs = NormalizeMessageOverlayTimingMs(
            state.MessageOverlayFadeMs ?? state.MessageOverlayBlinkPhaseMs,
            DefaultMessageOverlayFadeMs);
        _messageOverlayBlinkMode = NormalizeMessageOverlayBlinkMode(state.MessageOverlayBlinkMode);
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

    private static int NormalizeMessageOverlayTimingMs(int? value, int fallback)
    {
        if (value is >= MinMessageOverlayTimingMs and <= MaxMessageOverlayTimingMs)
            return value.Value;
        return fallback;
    }

    private static string NormalizeMessageOverlayBlinkMode(string? value)
    {
        return string.Equals(value, BlinkModeCharacterSweep, StringComparison.OrdinalIgnoreCase)
            ? BlinkModeCharacterSweep
            : BlinkModeWholeText;
    }

    private static int FindQuickMessageColorIndex(Brush brush)
    {
        if (brush is not SolidColorBrush solid)
            return -1;

        var hex = ColorToHex(solid.Color);
        return Array.FindIndex(
            QuickMessageColorOptions,
            option => string.Equals(option.Hex, hex, StringComparison.OrdinalIgnoreCase));
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
            Title = "Message Overlay",
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
            Title = "Message Overlay Settings",
            Width = 520,
            Height = 470,
            MinWidth = 380,
            MinHeight = 360,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // Layout: list on the left, icon button column on the right, input at bottom
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

        var iconStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top
        };
        iconStack.Children.Add(btnAdd);
        iconStack.Children.Add(btnUpdate);
        iconStack.Children.Add(btnRemove);

        var sidePanel = new DockPanel { LastChildFill = false };
        sidePanel.Children.Add(iconStack);

        Grid.SetRow(sidePanel, 0);
        Grid.SetColumn(sidePanel, 1);
        root.Children.Add(sidePanel);

        var txtMessage = new TextBox { Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(txtMessage, 1);
        Grid.SetColumn(txtMessage, 0);
        Grid.SetColumnSpan(txtMessage, 2);
        root.Children.Add(txtMessage);

        var behaviorGroup = new GroupBox
        {
            Header = "Blink behavior",
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(behaviorGroup, 2);
        Grid.SetColumn(behaviorGroup, 0);
        Grid.SetColumnSpan(behaviorGroup, 2);

        var behaviorGrid = new Grid { Margin = new Thickness(8) };
        behaviorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        behaviorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        behaviorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        behaviorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        behaviorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblMode = new TextBlock
        {
            Text = "Blink type",
            Margin = new Thickness(0, 0, 8, 8),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        behaviorGrid.Children.Add(lblMode);

        var cmbBlinkMode = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var mode in MessageOverlayBlinkModes)
        {
            cmbBlinkMode.Items.Add(new ComboBoxItem
            {
                Content = mode.Label,
                Tag = mode.Value
            });
        }

        var selectedModeIndex = Array.FindIndex(
            MessageOverlayBlinkModes,
            mode => string.Equals(mode.Value, _messageOverlayBlinkMode, StringComparison.OrdinalIgnoreCase));
        cmbBlinkMode.SelectedIndex = selectedModeIndex >= 0 ? selectedModeIndex : 0;
        Grid.SetColumn(cmbBlinkMode, 1);
        behaviorGrid.Children.Add(cmbBlinkMode);

        var lblIntervalMs = new TextBlock
        {
            Text = "Time between blinks (ms)",
            Margin = new Thickness(0, 0, 8, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(lblIntervalMs, 1);
        behaviorGrid.Children.Add(lblIntervalMs);

        var txtIntervalMs = new TextBox
        {
            Text = _messageOverlayBlinkIntervalMs.ToString(),
            Margin = new Thickness(0, 0, 0, 8),
            MinWidth = 120
        };
        Grid.SetRow(txtIntervalMs, 1);
        Grid.SetColumn(txtIntervalMs, 1);
        behaviorGrid.Children.Add(txtIntervalMs);

        var lblFadeMs = new TextBlock
        {
            Text = "Fade time (ms)",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(lblFadeMs, 2);
        behaviorGrid.Children.Add(lblFadeMs);

        var txtFadeMs = new TextBox
        {
            Text = _messageOverlayFadeMs.ToString(),
            MinWidth = 120
        };
        Grid.SetRow(txtFadeMs, 2);
        Grid.SetColumn(txtFadeMs, 1);
        behaviorGrid.Children.Add(txtFadeMs);

        behaviorGroup.Content = behaviorGrid;
        root.Children.Add(behaviorGroup);

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

        static bool TryParseTimingInput(TextBox input, int fallback, out int value)
        {
            if (!int.TryParse((input.Text ?? string.Empty).Trim(), out var parsed))
            {
                value = fallback;
                return false;
            }

            if (parsed < MinMessageOverlayTimingMs || parsed > MaxMessageOverlayTimingMs)
            {
                value = fallback;
                return false;
            }

            value = parsed;
            return true;
        }

        void CommitBlinkBehaviorSettings()
        {
            var intervalOk = TryParseTimingInput(txtIntervalMs, _messageOverlayBlinkIntervalMs, out var nextIntervalMs);
            var fadeOk = TryParseTimingInput(txtFadeMs, _messageOverlayFadeMs, out var nextFadeMs);
            if (!intervalOk)
                txtIntervalMs.Text = _messageOverlayBlinkIntervalMs.ToString();
            if (!fadeOk)
                txtFadeMs.Text = _messageOverlayFadeMs.ToString();

            _messageOverlayFadeMs = nextFadeMs;
            _messageOverlayBlinkIntervalMs = nextIntervalMs;
            _messageOverlayBlinkMode = cmbBlinkMode.SelectedItem is ComboBoxItem item && item.Tag is string value
                ? NormalizeMessageOverlayBlinkMode(value)
                : BlinkModeWholeText;

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
        txtIntervalMs.LostFocus += (_, _) => CommitBlinkBehaviorSettings();
        txtFadeMs.LostFocus += (_, _) => CommitBlinkBehaviorSettings();
        txtIntervalMs.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            CommitBlinkBehaviorSettings();
        };
        txtFadeMs.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            CommitBlinkBehaviorSettings();
        };
        cmbBlinkMode.SelectionChanged += (_, _) => CommitBlinkBehaviorSettings();

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
            CommitBlinkBehaviorSettings();
            dlg.Close();
        };

        dlg.Loaded += (_, _) =>
        {
            txtMessage.Focus();
            RefreshButtonState();
        };
        dlg.Closing += (_, _) => CommitBlinkBehaviorSettings();

        dlg.Content = root;
        return dlg.ShowDialog();
    }

    private void ShowQuickMessageOverlay(string text, Brush foreground)
    {
        SetMessageOverlayBlinking(false);
        _messageOverlayCharacterBaseText = text;
        _messageOverlayCharacterForeground = foreground;
        _messageOverlayActiveColorIndex = FindQuickMessageColorIndex(foreground);
        MessageOverlayText.Text = text;
        MessageOverlayText.Foreground = foreground;
        MessageOverlay.Visibility = Visibility.Visible;
        MessageOverlay.Focus();
        Keyboard.Focus(MessageOverlay);
    }

    private void HideQuickMessageOverlay()
    {
        SetMessageOverlayBlinking(false);
        MessageOverlay.Visibility = Visibility.Collapsed;
    }

    private void SetMessageOverlayBlinking(bool enabled)
    {
        _isMessageOverlayBlinking = enabled;
        StopMessageOverlayCharacterBlinkTimer();

        if (enabled)
        {
            if (string.Equals(_messageOverlayBlinkMode, BlinkModeCharacterSweep, StringComparison.OrdinalIgnoreCase))
            {
                StartCharacterSweepBlink();
                return;
            }

            StartWholeTextBlink();
            return;
        }

        MessageOverlayText.BeginAnimation(UIElement.OpacityProperty, null);
        MessageOverlayText.Opacity = 1.0;
        if (_messageOverlayCharacterBaseText.Length > 0)
            MessageOverlayText.Text = _messageOverlayCharacterBaseText;
    }

    private void StartWholeTextBlink()
    {
        _messageOverlayCharacterBaseText = MessageOverlayText.Text ?? string.Empty;
        var fadeMs = NormalizeMessageOverlayTimingMs(_messageOverlayFadeMs, DefaultMessageOverlayFadeMs);
        var intervalMs = NormalizeMessageOverlayTimingMs(_messageOverlayBlinkIntervalMs, DefaultMessageOverlayBlinkIntervalMs);
        var holdMs = intervalMs;
        var cycleMs = holdMs + fadeMs;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(cycleMs),
            RepeatBehavior = RepeatBehavior.Forever
        };
        var halfFadeMs = fadeMs / 2.0;
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs + halfFadeMs))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(cycleMs))));
        MessageOverlayText.Text = _messageOverlayCharacterBaseText;
        MessageOverlayText.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void StartCharacterSweepBlink()
    {
        _messageOverlayCharacterBaseText = MessageOverlayText.Text ?? string.Empty;
        _messageOverlayCharacterForeground = MessageOverlayText.Foreground;
        if (_messageOverlayCharacterBaseText.Length == 0)
        {
            MessageOverlayText.BeginAnimation(UIElement.OpacityProperty, null);
            MessageOverlayText.Opacity = 1.0;
            return;
        }

        _messageOverlayCharacterFadeMs = NormalizeMessageOverlayTimingMs(_messageOverlayFadeMs, DefaultMessageOverlayFadeMs);
        var intervalMs = NormalizeMessageOverlayTimingMs(_messageOverlayBlinkIntervalMs, DefaultMessageOverlayBlinkIntervalMs);
        _messageOverlayCharacterHoldMs = intervalMs;
        var charCount = _messageOverlayCharacterBaseText.Length;
        var stepMs = Math.Max(20, _messageOverlayCharacterFadeMs / Math.Max(charCount, 1));
        _messageOverlayCharacterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Min(CharacterBlinkTickMs, stepMs))
        };

        var elapsedMs = 0;
        var holding = false;
        MessageOverlayText.BeginAnimation(UIElement.OpacityProperty, null);
        MessageOverlayText.Opacity = 1.0;
        _messageOverlayCharacterVisibleChars = 0;
        RenderCharacterSweepText(0);

        _messageOverlayCharacterTimer.Tick += (_, _) =>
        {
            elapsedMs += (int)_messageOverlayCharacterTimer.Interval.TotalMilliseconds;
            if (!holding)
            {
                var progress = Math.Min(1.0, (double)elapsedMs / _messageOverlayCharacterFadeMs);
                var visibleChars = Math.Max(1, (int)Math.Ceiling(progress * charCount));
                _messageOverlayCharacterVisibleChars = visibleChars;
                RenderCharacterSweepText(visibleChars);
                if (elapsedMs >= _messageOverlayCharacterFadeMs)
                {
                    holding = true;
                    elapsedMs = 0;
                    _messageOverlayCharacterVisibleChars = charCount;
                    RenderCharacterSweepText(charCount);
                }
                return;
            }

            if (elapsedMs >= _messageOverlayCharacterHoldMs)
            {
                holding = false;
                elapsedMs = 0;
                _messageOverlayCharacterVisibleChars = 0;
                RenderCharacterSweepText(0);
            }
        };
        _messageOverlayCharacterTimer.Start();
    }

    private void RenderCharacterSweepText(int visibleChars)
    {
        var source = _messageOverlayCharacterBaseText ?? string.Empty;
        var maxVisible = Math.Max(0, Math.Min(visibleChars, source.Length));
        var transparentBrush = Brushes.Transparent;

        MessageOverlayText.Inlines.Clear();
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '\r')
                continue;
            if (ch == '\n')
            {
                MessageOverlayText.Inlines.Add(new LineBreak());
                continue;
            }

            MessageOverlayText.Inlines.Add(new Run(ch.ToString())
            {
                Foreground = i < maxVisible ? _messageOverlayCharacterForeground : transparentBrush
            });
        }
    }

    private void StopMessageOverlayCharacterBlinkTimer()
    {
        if (_messageOverlayCharacterTimer == null)
            return;

        _messageOverlayCharacterTimer.Stop();
        _messageOverlayCharacterTimer = null;
    }

    private void MessageOverlay_DismissByMouseDown(object sender, MouseButtonEventArgs e)
    {
        HideQuickMessageOverlay();
        e.Handled = true;
    }

    private void MessageOverlay_DismissByKeyDown(object sender, KeyEventArgs e)
    {
        HandleMessageOverlayKey(e);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleMessageOverlayKey(e);
    }

    private bool HandleMessageOverlayKey(KeyEventArgs e)
    {
        if (MessageOverlay.Visibility != Visibility.Visible)
            return false;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Up || key == Key.Down)
        {
            CycleMessageOverlayColor(key == Key.Down ? 1 : -1);
            e.Handled = true;
            return true;
        }

        if (key == Key.B)
        {
            SetMessageOverlayBlinking(!_isMessageOverlayBlinking);
            e.Handled = true;
            return true;
        }

        HideQuickMessageOverlay();
        e.Handled = true;
        return true;
    }

    private void CycleMessageOverlayColor(int direction)
    {
        if (QuickMessageColorOptions.Length == 0)
            return;

        if (_messageOverlayActiveColorIndex < 0)
            _messageOverlayActiveColorIndex = FindQuickMessageColorIndex(MessageOverlayText.Foreground);
        if (_messageOverlayActiveColorIndex < 0)
            _messageOverlayActiveColorIndex = 0;

        _messageOverlayActiveColorIndex =
            (_messageOverlayActiveColorIndex + direction + QuickMessageColorOptions.Length) % QuickMessageColorOptions.Length;
        var nextHex = QuickMessageColorOptions[_messageOverlayActiveColorIndex].Hex;
        if (!TryParseColor(nextHex, out var color))
            return;

        var nextBrush = new SolidColorBrush(color);
        MessageOverlayText.Foreground = nextBrush;
        _messageOverlayCharacterForeground = nextBrush;
        if (_isMessageOverlayBlinking
            && string.Equals(_messageOverlayBlinkMode, BlinkModeCharacterSweep, StringComparison.OrdinalIgnoreCase))
        {
            RenderCharacterSweepText(_messageOverlayCharacterVisibleChars);
        }
    }
}
