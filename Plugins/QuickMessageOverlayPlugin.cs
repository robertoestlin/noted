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
    private const int CharacterSweepBlankPauseMs = 500;
    private const string BlinkModeWholeText = "whole-text";
    private const string BlinkModeCharacterSweep = "character-sweep";
    private const string MessageOverlayEffectSnow = "snow";
    private const string MessageOverlayEffectRain = "rain";
    private const string DefaultMessageOverlayEffect = MessageOverlayEffectSnow;
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
    private string _messageOverlayActiveBlinkMode = BlinkModeWholeText;
    private List<string> _messageOverlaySavedMessages = [];
    private int _messageOverlaySavedMessageIndex = -1;
    private const int MaxMessageOverlayCountdownMinutes = 59;
    private const int MaxMessageOverlayCountdownSeconds = 59;
    private int _messageOverlayCountdownMinutes;
    private int _messageOverlayCountdownSeconds;
    private DispatcherTimer? _messageOverlayCountdownTimer;
    private DateTime _messageOverlayCountdownEndUtc;
    private int _messageOverlayCountdownInitialSeconds;
    private bool _messageOverlayShowNowPlaying;
    private bool _messageOverlayEffectEnabled;
    private string _messageOverlayEffect = DefaultMessageOverlayEffect;
    private DispatcherTimer? _messageOverlayEffectTimer;
    private readonly List<MessageOverlayEffectParticle> _messageOverlayEffectParticles = [];
    private DateTime _messageOverlayEffectLastTickUtc;

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
    private static readonly (string Label, string Value)[] MessageOverlayEffectOptions =
    [
        ("Snow", MessageOverlayEffectSnow),
        ("Rain", MessageOverlayEffectRain)
    ];

    private sealed class MessageOverlayEffectParticle
    {
        public required System.Windows.Shapes.Shape Shape { get; init; }
        public double X;
        public double Y;
        public double SpeedY;
        public double DriftAmplitude;
        public double DriftPhase;
        public double DriftFrequency;
        public double Size;
    }

    private void ResetQuickMessageOverlaySettings()
    {
        _quickMessagePresets = [.. DefaultQuickMessagePresets];
        _quickMessageCustom = string.Empty;
        _quickMessageColorHex = DefaultQuickMessageColorHex;
        _messageOverlayBlinkIntervalMs = DefaultMessageOverlayBlinkIntervalMs;
        _messageOverlayFadeMs = DefaultMessageOverlayFadeMs;
        _messageOverlayBlinkMode = BlinkModeWholeText;
        _messageOverlayCountdownMinutes = 0;
        _messageOverlayCountdownSeconds = 0;
        _messageOverlayShowNowPlaying = false;
        _messageOverlayEffectEnabled = false;
        _messageOverlayEffect = DefaultMessageOverlayEffect;
    }

    private static int ClampMessageOverlayCountdown(int? value, int max)
    {
        if (value is null)
            return 0;
        if (value < 0)
            return 0;
        return value > max ? max : value.Value;
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
        _messageOverlayCountdownMinutes = ClampMessageOverlayCountdown(
            state.MessageOverlayCountdownMinutes,
            MaxMessageOverlayCountdownMinutes);
        _messageOverlayCountdownSeconds = ClampMessageOverlayCountdown(
            state.MessageOverlayCountdownSeconds,
            MaxMessageOverlayCountdownSeconds);
        _messageOverlayEffectEnabled = state.MessageOverlayEffectEnabled ?? false;
        _messageOverlayEffect = NormalizeMessageOverlayEffect(state.MessageOverlayEffect);
    }

    private static string NormalizeMessageOverlayEffect(string? value)
    {
        return string.Equals(value, MessageOverlayEffectRain, StringComparison.OrdinalIgnoreCase)
            ? MessageOverlayEffectRain
            : MessageOverlayEffectSnow;
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

        // ---- Countdown row -----------------------------------------------
        var countdownRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var chkCountdown = new CheckBox
        {
            Content = "Show countdown",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 0)
        };
        countdownRow.Children.Add(chkCountdown);
        _messageOverlayShowNowPlaying = false;
        var chkNowPlaying = new CheckBox
        {
            Content = "Now playing",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 0),
            Visibility = _midiPlayerIsPlaying ? Visibility.Visible : Visibility.Collapsed
        };
        chkNowPlaying.Checked += (_, _) => _messageOverlayShowNowPlaying = true;
        chkNowPlaying.Unchecked += (_, _) => _messageOverlayShowNowPlaying = false;
        var cmbCountdownMinutes = new ComboBox { Width = 60 };
        for (var i = 0; i <= MaxMessageOverlayCountdownMinutes; i++)
            cmbCountdownMinutes.Items.Add(i);
        cmbCountdownMinutes.SelectedItem = ClampMessageOverlayCountdown(
            _messageOverlayCountdownMinutes, MaxMessageOverlayCountdownMinutes);
        if (cmbCountdownMinutes.SelectedItem == null)
            cmbCountdownMinutes.SelectedIndex = 0;
        countdownRow.Children.Add(cmbCountdownMinutes);
        countdownRow.Children.Add(new TextBlock
        {
            Text = "min",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0)
        });
        var cmbCountdownSeconds = new ComboBox { Width = 60 };
        for (var i = 0; i <= MaxMessageOverlayCountdownSeconds; i++)
            cmbCountdownSeconds.Items.Add(i);
        cmbCountdownSeconds.SelectedItem = ClampMessageOverlayCountdown(
            _messageOverlayCountdownSeconds, MaxMessageOverlayCountdownSeconds);
        if (cmbCountdownSeconds.SelectedItem == null)
            cmbCountdownSeconds.SelectedIndex = 0;
        countdownRow.Children.Add(cmbCountdownSeconds);
        countdownRow.Children.Add(new TextBlock
        {
            Text = "sec",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 16, 0)
        });
        countdownRow.Children.Add(chkNowPlaying);
        DockPanel.SetDock(countdownRow, Dock.Top);
        root.Children.Add(countdownRow);

        var effectRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        // Always start this dialog with effect disabled. We still remember the
        // last selected effect in the dropdown for quick re-enable.
        _messageOverlayEffectEnabled = false;
        var chkEffect = new CheckBox
        {
            Content = "Enable effect",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = MessageOverlay.Visibility != Visibility.Visible
        };
        effectRow.Children.Add(chkEffect);

        var cmbEffect = new ComboBox { Width = 140 };
        foreach (var option in MessageOverlayEffectOptions)
        {
            cmbEffect.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Value
            });
        }
        var selectedEffectIndex = Array.FindIndex(
            MessageOverlayEffectOptions,
            option => string.Equals(option.Value, _messageOverlayEffect, StringComparison.OrdinalIgnoreCase));
        cmbEffect.SelectedIndex = selectedEffectIndex >= 0 ? selectedEffectIndex : 0;
        effectRow.Children.Add(cmbEffect);
        DockPanel.SetDock(effectRow, Dock.Top);
        root.Children.Add(effectRow);

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

        int CurrentCountdownTotalSeconds()
        {
            if (chkCountdown.IsChecked != true)
                return 0;
            var minutes = cmbCountdownMinutes.SelectedItem is int m ? m : 0;
            var seconds = cmbCountdownSeconds.SelectedItem is int s ? s : 0;
            minutes = ClampMessageOverlayCountdown(minutes, MaxMessageOverlayCountdownMinutes);
            seconds = ClampMessageOverlayCountdown(seconds, MaxMessageOverlayCountdownSeconds);
            return minutes * 60 + seconds;
        }

        void ShowAndClose(string text)
        {
            var message = string.IsNullOrWhiteSpace(text) ? "..." : text.Trim();
            ShowQuickMessageOverlay(message, ResolveQuickMessageBrush(), CurrentCountdownTotalSeconds());
            dlg.Close();
        }

        void RefreshEffectPickerVisibility()
            => cmbEffect.Visibility = chkEffect.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

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

        cmbEffect.SelectionChanged += (_, _) =>
        {
            _messageOverlayEffect = cmbEffect.SelectedItem is ComboBoxItem item && item.Tag is string value
                ? NormalizeMessageOverlayEffect(value)
                : DefaultMessageOverlayEffect;
            SaveWindowSettings();
        };
        chkEffect.Checked += (_, _) =>
        {
            _messageOverlayEffectEnabled = true;
            RefreshEffectPickerVisibility();
            SaveWindowSettings();
        };
        chkEffect.Unchecked += (_, _) =>
        {
            _messageOverlayEffectEnabled = false;
            RefreshEffectPickerVisibility();
            SaveWindowSettings();
        };

        cmbCountdownMinutes.SelectionChanged += (_, _) =>
        {
            if (cmbCountdownMinutes.SelectedItem is int minutes)
            {
                _messageOverlayCountdownMinutes = ClampMessageOverlayCountdown(
                    minutes, MaxMessageOverlayCountdownMinutes);
                SaveWindowSettings();
            }
        };
        cmbCountdownSeconds.SelectionChanged += (_, _) =>
        {
            if (cmbCountdownSeconds.SelectedItem is int seconds)
            {
                _messageOverlayCountdownSeconds = ClampMessageOverlayCountdown(
                    seconds, MaxMessageOverlayCountdownSeconds);
                SaveWindowSettings();
            }
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

        RefreshEffectPickerVisibility();
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
        => ShowQuickMessageOverlay(text, foreground, 0);

    private void ShowQuickMessageOverlay(string text, Brush foreground, int countdownSeconds)
    {
        SetMessageOverlayBlinking(false);
        _messageOverlayCharacterBaseText = text;
        _messageOverlayCharacterForeground = foreground;
        _messageOverlayActiveColorIndex = FindQuickMessageColorIndex(foreground);
        _messageOverlayActiveBlinkMode = _messageOverlayBlinkMode;
        _messageOverlaySavedMessages = BuildQuickMessagePresetsSnapshot();
        _messageOverlaySavedMessageIndex = _messageOverlaySavedMessages.FindIndex(
            value => string.Equals(value, text, StringComparison.Ordinal));
        MessageOverlayText.Text = text;
        MessageOverlayText.Foreground = foreground;
        MessageOverlay.Visibility = Visibility.Visible;
        StartMessageOverlayCountdown(countdownSeconds, foreground);
        RefreshMessageOverlayNowPlaying();
        StartMessageOverlayEffect();
        if (MessageOverlayHelpContainer != null)
            MessageOverlayHelpContainer.Visibility = Visibility.Collapsed;
        MessageOverlay.Focus();
        Keyboard.Focus(MessageOverlay);
    }

    private void HideQuickMessageOverlay()
    {
        SetMessageOverlayBlinking(false);
        StopMessageOverlayCountdown();
        StopMessageOverlayCountdownExpiredBlink();
        StopMessageOverlayEffect();
        MessageOverlayCountdownContainer.Visibility = Visibility.Collapsed;
        MessageOverlayNowPlayingContainer.Visibility = Visibility.Collapsed;
        if (MessageOverlayHelpContainer != null)
            MessageOverlayHelpContainer.Visibility = Visibility.Collapsed;
        MessageOverlay.Visibility = Visibility.Collapsed;
    }

    private void ToggleMessageOverlayHelp()
    {
        if (MessageOverlayHelpContainer == null)
            return;
        MessageOverlayHelpContainer.Visibility = MessageOverlayHelpContainer.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Cycles the overlay effect: off → first effect → ... → last effect → off → ...
    /// </summary>
    private void CycleMessageOverlayEffect()
    {
        if (MessageOverlayEffectOptions.Length == 0)
            return;

        if (!_messageOverlayEffectEnabled)
        {
            _messageOverlayEffectEnabled = true;
            _messageOverlayEffect = MessageOverlayEffectOptions[0].Value;
        }
        else
        {
            var currentIndex = Array.FindIndex(
                MessageOverlayEffectOptions,
                option => string.Equals(option.Value, _messageOverlayEffect, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                _messageOverlayEffect = MessageOverlayEffectOptions[0].Value;
            }
            else if (currentIndex + 1 < MessageOverlayEffectOptions.Length)
            {
                _messageOverlayEffect = MessageOverlayEffectOptions[currentIndex + 1].Value;
            }
            else
            {
                _messageOverlayEffectEnabled = false;
            }
        }

        if (_messageOverlayEffectEnabled)
            StartMessageOverlayEffect();
        else
            StopMessageOverlayEffect();
        SaveWindowSettings();
    }

    private void StartMessageOverlayEffect()
    {
        StopMessageOverlayEffect();
        if (!_messageOverlayEffectEnabled || MessageOverlayEffectCanvas == null)
            return;

        var width = MessageOverlayEffectCanvas.ActualWidth;
        var height = MessageOverlayEffectCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            MessageOverlayEffectCanvas.Loaded -= MessageOverlayEffectCanvas_StartOnSizeReady;
            MessageOverlayEffectCanvas.SizeChanged -= MessageOverlayEffectCanvas_StartOnSizeReady;
            MessageOverlayEffectCanvas.SizeChanged += MessageOverlayEffectCanvas_StartOnSizeReady;
            return;
        }

        SpawnMessageOverlayEffectParticles(width, height);
        _messageOverlayEffectLastTickUtc = DateTime.UtcNow;
        _messageOverlayEffectTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _messageOverlayEffectTimer.Tick += MessageOverlayEffectTimer_Tick;
        _messageOverlayEffectTimer.Start();
    }

    private void MessageOverlayEffectCanvas_StartOnSizeReady(object? sender, EventArgs e)
    {
        MessageOverlayEffectCanvas.SizeChanged -= MessageOverlayEffectCanvas_StartOnSizeReady;
        if (MessageOverlay.Visibility == Visibility.Visible && _messageOverlayEffectEnabled)
            StartMessageOverlayEffect();
    }

    private void StopMessageOverlayEffect()
    {
        if (_messageOverlayEffectTimer != null)
        {
            _messageOverlayEffectTimer.Stop();
            _messageOverlayEffectTimer.Tick -= MessageOverlayEffectTimer_Tick;
            _messageOverlayEffectTimer = null;
        }
        if (MessageOverlayEffectCanvas != null)
        {
            MessageOverlayEffectCanvas.SizeChanged -= MessageOverlayEffectCanvas_StartOnSizeReady;
            MessageOverlayEffectCanvas.Children.Clear();
        }
        _messageOverlayEffectParticles.Clear();
    }

    private void SpawnMessageOverlayEffectParticles(double width, double height)
    {
        MessageOverlayEffectCanvas.Children.Clear();
        _messageOverlayEffectParticles.Clear();

        var isRain = string.Equals(_messageOverlayEffect, MessageOverlayEffectRain, StringComparison.OrdinalIgnoreCase);
        var count = isRain ? 160 : 90;
        var rng = Random.Shared;

        for (var i = 0; i < count; i++)
        {
            System.Windows.Shapes.Shape shape;
            double size;
            double speedY;
            double drift;
            if (isRain)
            {
                size = 1.6 + rng.NextDouble() * 1.2;
                speedY = 700 + rng.NextDouble() * 400;
                drift = 0;
                shape = new System.Windows.Shapes.Rectangle
                {
                    Width = size,
                    Height = 14 + rng.NextDouble() * 10,
                    Fill = new SolidColorBrush(Color.FromArgb(180, 200, 220, 255))
                };
            }
            else
            {
                size = 3 + rng.NextDouble() * 5;
                speedY = 40 + rng.NextDouble() * 60;
                drift = 12 + rng.NextDouble() * 18;
                shape = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
                };
            }

            var x = rng.NextDouble() * width;
            var y = rng.NextDouble() * height;
            Canvas.SetLeft(shape, x);
            Canvas.SetTop(shape, y);
            MessageOverlayEffectCanvas.Children.Add(shape);

            _messageOverlayEffectParticles.Add(new MessageOverlayEffectParticle
            {
                Shape = shape,
                X = x,
                Y = y,
                SpeedY = speedY,
                DriftAmplitude = drift,
                DriftPhase = rng.NextDouble() * Math.PI * 2,
                DriftFrequency = 0.6 + rng.NextDouble() * 1.2,
                Size = size
            });
        }
    }

    private void MessageOverlayEffectTimer_Tick(object? sender, EventArgs e)
    {
        if (MessageOverlayEffectCanvas == null)
            return;
        var now = DateTime.UtcNow;
        var dt = (now - _messageOverlayEffectLastTickUtc).TotalSeconds;
        _messageOverlayEffectLastTickUtc = now;
        if (dt <= 0 || dt > 0.25)
            dt = 0.016;

        var width = MessageOverlayEffectCanvas.ActualWidth;
        var height = MessageOverlayEffectCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var rng = Random.Shared;
        foreach (var p in _messageOverlayEffectParticles)
        {
            p.Y += p.SpeedY * dt;
            double xOffset = 0;
            if (p.DriftAmplitude > 0)
            {
                p.DriftPhase += p.DriftFrequency * dt;
                xOffset = Math.Sin(p.DriftPhase) * p.DriftAmplitude * dt;
                p.X += xOffset;
                if (p.X < -p.Size) p.X = width;
                else if (p.X > width + p.Size) p.X = 0;
            }

            if (p.Y > height + 20)
            {
                p.Y = -20 - rng.NextDouble() * 40;
                p.X = rng.NextDouble() * width;
            }

            Canvas.SetLeft(p.Shape, p.X);
            Canvas.SetTop(p.Shape, p.Y);
        }
    }

    private void RefreshMessageOverlayNowPlaying()
    {
        if (MessageOverlayNowPlayingContainer == null)
            return;

        var shouldShow = MessageOverlay.Visibility == Visibility.Visible
            && _messageOverlayShowNowPlaying
            && _midiPlayerIsPlaying
            && !string.IsNullOrWhiteSpace(_midiPlayerCurrentTitle);

        if (!shouldShow)
        {
            MessageOverlayNowPlayingContainer.Visibility = Visibility.Collapsed;
            return;
        }

        MessageOverlayNowPlayingTitle.Text = _midiPlayerCurrentTitle ?? string.Empty;
        MessageOverlayNowPlayingMeta.Text = _midiPlayerCurrentGroup ?? string.Empty;
        MessageOverlayNowPlayingContainer.Visibility = Visibility.Visible;
    }

    private void StartMessageOverlayCountdown(int totalSeconds, Brush foreground)
    {
        StopMessageOverlayCountdown();
        StopMessageOverlayCountdownExpiredBlink();
        if (totalSeconds <= 0)
        {
            _messageOverlayCountdownInitialSeconds = 0;
            MessageOverlayCountdownContainer.Visibility = Visibility.Collapsed;
            return;
        }

        _messageOverlayCountdownInitialSeconds = totalSeconds;
        MessageOverlayCountdown.Foreground = foreground;
        MessageOverlayCountdownContainer.Visibility = Visibility.Visible;
        _messageOverlayCountdownEndUtc = DateTime.UtcNow.AddSeconds(totalSeconds);
        UpdateMessageOverlayCountdownText();
        _messageOverlayCountdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _messageOverlayCountdownTimer.Tick += (_, _) => UpdateMessageOverlayCountdownText();
        _messageOverlayCountdownTimer.Start();
    }

    private void ResetMessageOverlayCountdown()
    {
        if (_messageOverlayCountdownInitialSeconds <= 0)
            return;
        StartMessageOverlayCountdown(_messageOverlayCountdownInitialSeconds, MessageOverlayText.Foreground);
    }

    private void StartMessageOverlayCountdownExpiredBlink()
    {
        var fadeMs = NormalizeMessageOverlayTimingMs(_messageOverlayFadeMs, DefaultMessageOverlayFadeMs);
        var intervalMs = NormalizeMessageOverlayTimingMs(_messageOverlayBlinkIntervalMs, DefaultMessageOverlayBlinkIntervalMs);
        var holdMs = intervalMs;
        var cycleMs = holdMs + fadeMs;
        var halfFadeMs = fadeMs / 2.0;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(cycleMs),
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs + halfFadeMs))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(cycleMs))));
        MessageOverlayCountdown.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void StopMessageOverlayCountdownExpiredBlink()
    {
        MessageOverlayCountdown.BeginAnimation(UIElement.OpacityProperty, null);
        MessageOverlayCountdown.Opacity = 1.0;
    }

    private void StopMessageOverlayCountdown()
    {
        if (_messageOverlayCountdownTimer == null)
            return;
        _messageOverlayCountdownTimer.Stop();
        _messageOverlayCountdownTimer = null;
    }

    private void UpdateMessageOverlayCountdownText()
    {
        var remaining = _messageOverlayCountdownEndUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            MessageOverlayCountdown.Text = "00:00";
            StopMessageOverlayCountdown();
            StartMessageOverlayCountdownExpiredBlink();
            return;
        }
        var totalSec = (int)Math.Ceiling(remaining.TotalSeconds);
        var minutes = totalSec / 60;
        var seconds = totalSec % 60;
        MessageOverlayCountdown.Text = $"{minutes:D2}:{seconds:D2}";
    }

    private void SetMessageOverlayBlinking(bool enabled)
    {
        _isMessageOverlayBlinking = enabled;
        StopMessageOverlayCharacterBlinkTimer();

        if (enabled)
        {
            if (string.Equals(_messageOverlayActiveBlinkMode, BlinkModeCharacterSweep, StringComparison.OrdinalIgnoreCase))
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
        var blanking = false;
        MessageOverlayText.BeginAnimation(UIElement.OpacityProperty, null);
        MessageOverlayText.Opacity = 1.0;
        _messageOverlayCharacterVisibleChars = 0;
        RenderCharacterSweepText(0);

        _messageOverlayCharacterTimer.Tick += (_, _) =>
        {
            elapsedMs += (int)_messageOverlayCharacterTimer.Interval.TotalMilliseconds;
            if (blanking)
            {
                if (elapsedMs >= CharacterSweepBlankPauseMs)
                {
                    blanking = false;
                    elapsedMs = 0;
                }
                return;
            }

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
                blanking = true;
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
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
            && key == Key.O
            && MessageOverlay.Visibility != Visibility.Visible)
        {
            ShowQuickMessageOverlayDialog();
            e.Handled = true;
            return;
        }

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

        if (key == Key.Left || key == Key.Right)
        {
            CycleMessageOverlayText(key == Key.Right ? 1 : -1);
            e.Handled = true;
            return true;
        }

        if (key == Key.T)
        {
            ToggleMessageOverlayBlinkType();
            e.Handled = true;
            return true;
        }

        if (key == Key.B)
        {
            SetMessageOverlayBlinking(!_isMessageOverlayBlinking);
            e.Handled = true;
            return true;
        }

        if (key == Key.N && _midiPlayerNextAction != null)
        {
            _midiPlayerNextAction.Invoke();
            e.Handled = true;
            return true;
        }

        if (key == Key.M && _midiPlayerPauseToggleAction != null)
        {
            // Toggling pause from inside the overlay also turns on the
            // Now Playing pill so it follows the player state: visible
            // while audio is playing, hidden the moment we pause.
            _messageOverlayShowNowPlaying = true;
            _midiPlayerPauseToggleAction.Invoke();
            RefreshMessageOverlayNowPlaying();
            e.Handled = true;
            return true;
        }

        if (key == Key.R && _messageOverlayCountdownInitialSeconds > 0)
        {
            ResetMessageOverlayCountdown();
            e.Handled = true;
            return true;
        }

        if (key == Key.H)
        {
            ToggleMessageOverlayHelp();
            e.Handled = true;
            return true;
        }

        if (key == Key.E)
        {
            CycleMessageOverlayEffect();
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
        if (MessageOverlayCountdownContainer.Visibility == Visibility.Visible)
            MessageOverlayCountdown.Foreground = nextBrush;
        if (_isMessageOverlayBlinking
            && string.Equals(_messageOverlayActiveBlinkMode, BlinkModeCharacterSweep, StringComparison.OrdinalIgnoreCase))
        {
            RenderCharacterSweepText(_messageOverlayCharacterVisibleChars);
        }
    }

    private void ToggleMessageOverlayBlinkType()
    {
        _messageOverlayActiveBlinkMode =
            string.Equals(_messageOverlayActiveBlinkMode, BlinkModeCharacterSweep, StringComparison.OrdinalIgnoreCase)
                ? BlinkModeWholeText
                : BlinkModeCharacterSweep;

        if (!_isMessageOverlayBlinking)
            return;

        SetMessageOverlayBlinking(false);
        SetMessageOverlayBlinking(true);
    }

    private void CycleMessageOverlayText(int direction)
    {
        if (_messageOverlaySavedMessages.Count == 0)
            return;

        if (_messageOverlaySavedMessageIndex < 0)
            _messageOverlaySavedMessageIndex = 0;
        else
            _messageOverlaySavedMessageIndex =
                (_messageOverlaySavedMessageIndex + direction + _messageOverlaySavedMessages.Count) % _messageOverlaySavedMessages.Count;

        ApplyMessageOverlayText(_messageOverlaySavedMessages[_messageOverlaySavedMessageIndex]);
    }

    private void ApplyMessageOverlayText(string text)
    {
        _messageOverlayCharacterBaseText = text;
        _messageOverlayCharacterVisibleChars = 0;

        if (_isMessageOverlayBlinking)
        {
            SetMessageOverlayBlinking(false);
            SetMessageOverlayBlinking(true);
            return;
        }

        MessageOverlayText.BeginAnimation(UIElement.OpacityProperty, null);
        MessageOverlayText.Opacity = 1.0;
        MessageOverlayText.Text = text;
    }
}
