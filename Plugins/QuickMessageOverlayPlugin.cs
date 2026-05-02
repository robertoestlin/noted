using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    private const string MessageOverlayEffectSunny = "sunny";
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
    private bool _messageOverlayCountdownTurnedOff;
    private bool _messageOverlayShowNowPlaying;
    private bool _messageOverlayEffectEnabled;
    private string _messageOverlayEffect = DefaultMessageOverlayEffect;
    private DispatcherTimer? _messageOverlayEffectTimer;
    private readonly List<MessageOverlayEffectParticle> _messageOverlayEffectParticles = [];
    private DateTime _messageOverlayEffectLastTickUtc;
    private RotateTransform? _messageOverlaySunRayRotation;
    private double _messageOverlaySunRayAngle;
    private const int MaxMessageOverlayGnomeProbabilityPercent = 100;
    private const int MessageOverlayGnomeProbabilityWindowSeconds = 300;
    private int _messageOverlayGnomeProbabilityPercent;
    private DispatcherTimer? _messageOverlayGnomeTimer;
    private static BitmapImage? _messageOverlayGnomeBitmap;

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
        ("Snowy", MessageOverlayEffectSnow),
        ("Rainy", MessageOverlayEffectRain),
        ("Sunny", MessageOverlayEffectSunny)
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
        _messageOverlayGnomeProbabilityPercent = 0;
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

    private void ApplyQuickMessagePresetsFromPluginState(MessageOverlayPluginState state)
    {
        var presets = NormalizeQuickMessagePresets(state.QuickMessagePresets);
        _quickMessagePresets = presets.Count == 0 ? [.. DefaultQuickMessagePresets] : presets;
        _quickMessageCustom = (state.QuickMessageCustom ?? string.Empty).Trim();
        _quickMessageColorHex = NormalizeQuickMessageColorHex(state.QuickMessageColor);
    }

    private void ApplyQuickMessageOverlayPluginSettings(MessageOverlayPluginState? state)
    {
        if (state == null)
            return;
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
        _messageOverlayGnomeProbabilityPercent = NormalizeMessageOverlayGnomeProbabilityPercent(
            state.MessageOverlayGnomeProbabilityPercent);
    }

    private static int NormalizeMessageOverlayGnomeProbabilityPercent(int? value)
    {
        if (value is null) return 0;
        if (value < 0) return 0;
        if (value > MaxMessageOverlayGnomeProbabilityPercent) return MaxMessageOverlayGnomeProbabilityPercent;
        return value.Value;
    }

    private static string NormalizeMessageOverlayEffect(string? value)
    {
        if (string.Equals(value, MessageOverlayEffectRain, StringComparison.OrdinalIgnoreCase))
            return MessageOverlayEffectRain;
        if (string.Equals(value, MessageOverlayEffectSunny, StringComparison.OrdinalIgnoreCase))
            return MessageOverlayEffectSunny;
        return MessageOverlayEffectSnow;
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
            Height = 580,
            MinWidth = 380,
            MinHeight = 460,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // Layout: list on the left, icon button column on the right, input at bottom
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

        var gnomeGroup = new GroupBox
        {
            Header = "Gnome peek",
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(gnomeGroup, 3);
        Grid.SetColumn(gnomeGroup, 0);
        Grid.SetColumnSpan(gnomeGroup, 2);

        var gnomeGrid = new Grid { Margin = new Thickness(8) };
        gnomeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gnomeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gnomeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gnomeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblGnomePercent = new TextBlock
        {
            Text = "Chance per 5 minutes (%)",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        gnomeGrid.Children.Add(lblGnomePercent);

        var txtGnomePercent = new TextBox
        {
            Text = _messageOverlayGnomeProbabilityPercent.ToString(CultureInfo.InvariantCulture),
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(txtGnomePercent, 1);
        gnomeGrid.Children.Add(txtGnomePercent);

        var lblGnomeHint = new TextBlock
        {
            Text = "0 = never, 100 = certain. Sets how likely it is a tiny gnome peeks behind a letter within 5 minutes.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(lblGnomeHint, 1);
        Grid.SetColumnSpan(lblGnomeHint, 2);
        gnomeGrid.Children.Add(lblGnomeHint);

        gnomeGroup.Content = gnomeGrid;
        root.Children.Add(gnomeGroup);

        void CommitGnomeSettings()
        {
            var raw = (txtGnomePercent.Text ?? string.Empty).Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                var clamped = Math.Clamp(parsed, 0, MaxMessageOverlayGnomeProbabilityPercent);
                if (clamped != parsed)
                    txtGnomePercent.Text = clamped.ToString(CultureInfo.InvariantCulture);
                _messageOverlayGnomeProbabilityPercent = clamped;
            }
            else
            {
                txtGnomePercent.Text = _messageOverlayGnomeProbabilityPercent.ToString(CultureInfo.InvariantCulture);
            }
            SaveWindowSettings();
            if (MessageOverlay.Visibility == Visibility.Visible)
            {
                StopMessageOverlayGnomeTimer();
                StartMessageOverlayGnomeTimer();
            }
        }

        txtGnomePercent.LostFocus += (_, _) => CommitGnomeSettings();
        txtGnomePercent.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            CommitGnomeSettings();
        };

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
            CommitGnomeSettings();
            dlg.Close();
        };

        dlg.Loaded += (_, _) =>
        {
            txtMessage.Focus();
            RefreshButtonState();
        };
        dlg.Closing += (_, _) =>
        {
            CommitBlinkBehaviorSettings();
            CommitGnomeSettings();
        };

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
        StartMessageOverlayGnomeTimer();
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
        _messageOverlayCountdownTurnedOff = false;
        StopMessageOverlayEffect();
        StopMessageOverlayGnomeTimer();
        MessageOverlayTextEffectCanvas?.Children.Clear();
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
        _messageOverlaySunRayRotation = null;
        _messageOverlaySunRayAngle = 0;
    }

    private void SpawnMessageOverlayEffectParticles(double width, double height)
    {
        MessageOverlayEffectCanvas.Children.Clear();
        _messageOverlayEffectParticles.Clear();
        _messageOverlaySunRayRotation = null;

        if (string.Equals(_messageOverlayEffect, MessageOverlayEffectSunny, StringComparison.OrdinalIgnoreCase))
        {
            BuildMessageOverlaySun(width, height);
            return;
        }

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

    private void BuildMessageOverlaySun(double width, double height)
    {
        // Sun center is tucked just outside the upper-left so only a wedge of
        // the disc is visible — like the sun peeking into the corner.
        var sunCenterX = -40.0;
        var sunCenterY = -30.0;
        var coreRadius = 150.0;
        var rayLength = Math.Max(width, height) + 200;
        var rayFieldSize = rayLength * 2;
        var haloRadius = Math.Min(Math.Max(width, height) * 0.9, 1200);

        var halo = new System.Windows.Shapes.Ellipse
        {
            Width = haloRadius * 2,
            Height = haloRadius * 2,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(150, 255, 225, 150), 0.0),
                    new GradientStop(Color.FromArgb(80, 255, 195, 90), 0.35),
                    new GradientStop(Color.FromArgb(30, 255, 170, 60), 0.7),
                    new GradientStop(Color.FromArgb(0, 255, 150, 40), 1.0)
                }
            }
        };
        Canvas.SetLeft(halo, sunCenterX - haloRadius);
        Canvas.SetTop(halo, sunCenterY - haloRadius);
        MessageOverlayEffectCanvas.Children.Add(halo);

        var rayCanvas = new Canvas
        {
            Width = rayFieldSize,
            Height = rayFieldSize,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rayCanvas, sunCenterX - rayLength);
        Canvas.SetTop(rayCanvas, sunCenterY - rayLength);
        var rotation = new RotateTransform(0, rayLength, rayLength);
        rayCanvas.RenderTransform = rotation;
        _messageOverlaySunRayRotation = rotation;
        _messageOverlaySunRayAngle = 0;

        // Aim the beams from the corner toward the text in the center of the
        // overlay, fanning out in a wedge across that direction.
        var aimDeg = Math.Atan2(height / 2.0 - sunCenterY, width / 2.0 - sunCenterX) * 180.0 / Math.PI;
        const double rayArcSpan = 75.0;
        const int rayCount = 9;
        var rayBrush = new SolidColorBrush(Color.FromArgb(150, 255, 225, 130));
        var shortRayBrush = new SolidColorBrush(Color.FromArgb(110, 255, 210, 110));
        for (var i = 0; i < rayCount; i++)
        {
            var t = rayCount == 1 ? 0.5 : (double)i / (rayCount - 1);
            var angleDeg = aimDeg - rayArcSpan / 2 + t * rayArcSpan;
            var isLong = i % 2 == 0;
            var thisLength = isLong ? rayLength : rayLength * 0.55;
            var halfWidth = isLong ? 8.0 : 5.0;
            var ray = new System.Windows.Shapes.Polygon
            {
                Points =
                {
                    new Point(coreRadius * 0.9, -halfWidth),
                    new Point(coreRadius * 0.9, halfWidth),
                    new Point(thisLength, 0)
                },
                Fill = isLong ? rayBrush : shortRayBrush,
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(angleDeg)
            };
            Canvas.SetLeft(ray, rayLength);
            Canvas.SetTop(ray, rayLength);
            rayCanvas.Children.Add(ray);
        }
        MessageOverlayEffectCanvas.Children.Add(rayCanvas);

        var coreGlow = new System.Windows.Shapes.Ellipse
        {
            Width = coreRadius * 2.6,
            Height = coreRadius * 2.6,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(180, 255, 240, 170), 0.0),
                    new GradientStop(Color.FromArgb(80, 255, 210, 110), 0.55),
                    new GradientStop(Color.FromArgb(0, 255, 180, 60), 1.0)
                }
            }
        };
        Canvas.SetLeft(coreGlow, sunCenterX - coreRadius * 1.3);
        Canvas.SetTop(coreGlow, sunCenterY - coreRadius * 1.3);
        MessageOverlayEffectCanvas.Children.Add(coreGlow);

        var core = new System.Windows.Shapes.Ellipse
        {
            Width = coreRadius * 2,
            Height = coreRadius * 2,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.4, 0.35),
                Center = new Point(0.4, 0.35),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(255, 255, 255, 240), 0.0),
                    new GradientStop(Color.FromArgb(255, 255, 235, 140), 0.45),
                    new GradientStop(Color.FromArgb(255, 255, 175, 70), 0.85),
                    new GradientStop(Color.FromArgb(255, 250, 140, 40), 1.0)
                }
            }
        };
        Canvas.SetLeft(core, sunCenterX - coreRadius);
        Canvas.SetTop(core, sunCenterY - coreRadius);
        MessageOverlayEffectCanvas.Children.Add(core);
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

        if (_messageOverlaySunRayRotation != null)
        {
            // Gentle shimmer: oscillate a few degrees side-to-side instead of spinning.
            _messageOverlaySunRayAngle += dt;
            const double swayDegrees = 4.0;
            const double swaySpeed = 0.9;
            _messageOverlaySunRayRotation.Angle = Math.Sin(_messageOverlaySunRayAngle * swaySpeed) * swayDegrees;
            return;
        }

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
        if (MessageOverlayNowPlayingPlaylist != null)
        {
            var pl = _midiPlayerCurrentPlaylistName ?? string.Empty;
            MessageOverlayNowPlayingPlaylist.Text = pl;
            MessageOverlayNowPlayingPlaylist.Visibility = string.IsNullOrWhiteSpace(pl)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        MessageOverlayNowPlayingContainer.Visibility = Visibility.Visible;
    }

    private void SyncMessageOverlayCountdownContainerVisibility()
    {
        if (_messageOverlayCountdownInitialSeconds <= 0)
            return;
        MessageOverlayCountdownContainer.Visibility = _messageOverlayCountdownTurnedOff
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void StartMessageOverlayCountdown(int totalSeconds, Brush foreground, bool updateResetBaseline = true)
    {
        StopMessageOverlayCountdown();
        StopMessageOverlayCountdownExpiredBlink();

        if (updateResetBaseline)
        {
            if (totalSeconds <= 0)
                _messageOverlayCountdownInitialSeconds = 0;
            else
                _messageOverlayCountdownInitialSeconds = totalSeconds;
        }

        if (updateResetBaseline && totalSeconds > 0)
            _messageOverlayCountdownTurnedOff = false;

        if (totalSeconds <= 0)
        {
            if (_messageOverlayCountdownInitialSeconds <= 0)
            {
                MessageOverlayCountdownContainer.Visibility = Visibility.Collapsed;
                return;
            }

            MessageOverlayCountdown.Foreground = foreground;
            MessageOverlayCountdown.Text = "00:00";
            SyncMessageOverlayCountdownContainerVisibility();
            if (!_messageOverlayCountdownTurnedOff)
                StartMessageOverlayCountdownExpiredBlink();
            return;
        }

        MessageOverlayCountdown.Foreground = foreground;
        _messageOverlayCountdownEndUtc = DateTime.UtcNow.AddSeconds(totalSeconds);
        UpdateMessageOverlayCountdownText();
        SyncMessageOverlayCountdownContainerVisibility();
        _messageOverlayCountdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _messageOverlayCountdownTimer.Tick += (_, _) => UpdateMessageOverlayCountdownText();
        _messageOverlayCountdownTimer.Start();
    }

    private static int MessageOverlayCountdownMaxTotalSeconds()
        => MaxMessageOverlayCountdownMinutes * 60 + MaxMessageOverlayCountdownSeconds;

    private int GetMessageOverlayCountdownRemainingSeconds()
    {
        if (_messageOverlayCountdownTimer != null)
            return Math.Max(0, (int)Math.Ceiling((_messageOverlayCountdownEndUtc - DateTime.UtcNow).TotalSeconds));
        return 0;
    }

    private bool MessageOverlayCountdownHasExpired()
    {
        return _messageOverlayCountdownInitialSeconds > 0
            && _messageOverlayCountdownTimer == null
            && DateTime.UtcNow >= _messageOverlayCountdownEndUtc;
    }

    private void AdjustMessageOverlayCountdownByMinutes(int deltaMinutes)
    {
        if (_messageOverlayCountdownInitialSeconds <= 0 || _messageOverlayCountdownTurnedOff)
            return;

        var maxSec = MessageOverlayCountdownMaxTotalSeconds();
        var rem = GetMessageOverlayCountdownRemainingSeconds();
        if (_messageOverlayCountdownTimer == null && MessageOverlayCountdownHasExpired())
            rem = 0;
        var newRem = Math.Clamp(rem + deltaMinutes * 60, 0, maxSec);

        StartMessageOverlayCountdown(newRem, MessageOverlayText.Foreground, updateResetBaseline: false);
    }

    private int MessageOverlayCountdownDurationFromSettings()
    {
        var minutes = ClampMessageOverlayCountdown(_messageOverlayCountdownMinutes, MaxMessageOverlayCountdownMinutes);
        var seconds = ClampMessageOverlayCountdown(_messageOverlayCountdownSeconds, MaxMessageOverlayCountdownSeconds);
        var total = minutes * 60 + seconds;
        return total <= 0 ? 60 : total;
    }

    private void ToggleMessageOverlayCountdownOnOff()
    {
        if (_messageOverlayCountdownInitialSeconds > 0)
        {
            if (!_messageOverlayCountdownTurnedOff)
            {
                _messageOverlayCountdownTurnedOff = true;
                StopMessageOverlayCountdown();
                StopMessageOverlayCountdownExpiredBlink();
                MessageOverlayCountdownContainer.Visibility = Visibility.Collapsed;
                return;
            }

            _messageOverlayCountdownTurnedOff = false;
            StartMessageOverlayCountdown(_messageOverlayCountdownInitialSeconds, MessageOverlayText.Foreground, updateResetBaseline: false);
            return;
        }

        StartMessageOverlayCountdown(
            MessageOverlayCountdownDurationFromSettings(),
            MessageOverlayText.Foreground,
            updateResetBaseline: true);
    }

    private void ResetMessageOverlayCountdown()
    {
        if (_messageOverlayCountdownInitialSeconds <= 0)
            return;
        _messageOverlayCountdownTurnedOff = false;
        StartMessageOverlayCountdown(_messageOverlayCountdownInitialSeconds, MessageOverlayText.Foreground, updateResetBaseline: false);
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
            SyncMessageOverlayCountdownContainerVisibility();
            if (!_messageOverlayCountdownTurnedOff)
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

        if (key == Key.OemComma)
        {
            AdjustMessageOverlayCountdownByMinutes(-1);
            e.Handled = true;
            return true;
        }

        if (key == Key.OemPeriod)
        {
            AdjustMessageOverlayCountdownByMinutes(1);
            e.Handled = true;
            return true;
        }

        if (key == Key.OemMinus || key == Key.Subtract)
        {
            ToggleMessageOverlayCountdownOnOff();
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

    private void StartMessageOverlayGnomeTimer()
    {
        StopMessageOverlayGnomeTimer();
        if (_messageOverlayGnomeProbabilityPercent <= 0)
            return;
        _messageOverlayGnomeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _messageOverlayGnomeTimer.Tick += MessageOverlayGnomeTimer_Tick;
        _messageOverlayGnomeTimer.Start();
    }

    private void StopMessageOverlayGnomeTimer()
    {
        if (_messageOverlayGnomeTimer == null)
            return;
        _messageOverlayGnomeTimer.Stop();
        _messageOverlayGnomeTimer.Tick -= MessageOverlayGnomeTimer_Tick;
        _messageOverlayGnomeTimer = null;
    }

    private void MessageOverlayGnomeTimer_Tick(object? sender, EventArgs e)
    {
        if (MessageOverlay.Visibility != Visibility.Visible)
        {
            StopMessageOverlayGnomeTimer();
            return;
        }
        if (_messageOverlayGnomeProbabilityPercent <= 0)
        {
            StopMessageOverlayGnomeTimer();
            return;
        }

        var perFiveMinutes = Math.Clamp(_messageOverlayGnomeProbabilityPercent, 0, 100) / 100.0;
        var perSecond = perFiveMinutes >= 1.0
            ? 1.0
            : 1.0 - Math.Pow(1.0 - perFiveMinutes, 1.0 / MessageOverlayGnomeProbabilityWindowSeconds);
        if (Random.Shared.NextDouble() < perSecond)
            ShowMessageOverlayGnomePeek();
    }

    private static void AttachMessageOverlayGnomeRotateThenTranslate(
        FrameworkElement gnome,
        TranslateTransform translate,
        double tiltDegrees,
        double gnomeWidth,
        double gnomeHeight)
    {
        var rot = new RotateTransform(tiltDegrees, gnomeWidth * 0.5, gnomeHeight);
        var group = new TransformGroup();
        group.Children.Add(rot);
        group.Children.Add(translate);
        gnome.RenderTransform = group;
    }

    /// <summary>
    /// Element-space transform applied before <see cref="Canvas.SetLeft"/> / <see cref="Canvas.SetTop"/> (rotate around feet, then peek/hide translation).
    /// </summary>
    private static Matrix MessageOverlayGnomeElementPoseMatrix(double gw, double gh, double tiltDeg, double transX, double transY)
    {
        var rot = new RotateTransform(tiltDeg, gw * 0.5, gh);
        var mat = rot.Value;
        mat.Translate(transX, transY);
        return mat;
    }

    private static bool MessageOverlayTryCanvasPointToInkPixel(
        GeneralTransform textToEffect,
        Rect inkLocalRect,
        int rasterScale,
        int pw,
        int ph,
        Point canvasPt,
        out int ix,
        out int iy)
    {
        ix = 0;
        iy = 0;
        if (!textToEffect.Inverse.TryTransform(canvasPt, out var localPt))
            return false;
        var px = (localPt.X - inkLocalRect.Left) * rasterScale;
        var py = (localPt.Y - inkLocalRect.Top) * rasterScale;
        ix = (int)Math.Floor(px + 1e-4);
        iy = (int)Math.Floor(py + 1e-4);
        return ix >= 0 && iy >= 0 && ix < pw && iy < ph;
    }

    /// <summary>
    /// True if every sample on the gnome bitmap (including corners after rotation) maps to solid ink.
    /// </summary>
    private static bool MessageOverlayGnomePoseFullyOverSolidInk(
        bool[,] solid,
        int pw,
        int ph,
        int rasterScale,
        Rect inkLocalRect,
        GeneralTransform textToEffect,
        double canvasLeft,
        double canvasTop,
        double gw,
        double gh,
        double tiltDeg,
        double transX,
        double transY,
        int grid)
    {
        if (grid < 2)
            grid = 2;
        var m = MessageOverlayGnomeElementPoseMatrix(gw, gh, tiltDeg, transX, transY);
        for (var iu = 0; iu < grid; iu++)
        {
            for (var iv = 0; iv < grid; iv++)
            {
                var ex = iu * gw / (grid - 1);
                var ey = iv * gh / (grid - 1);
                var p = m.Transform(new Point(ex, ey));
                var canvasPt = new Point(canvasLeft + p.X, canvasTop + p.Y);
                if (!MessageOverlayTryCanvasPointToInkPixel(textToEffect, inkLocalRect, rasterScale, pw, ph, canvasPt, out var ix, out var iy))
                    return false;
                if (!solid[iy, ix])
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Unit vector from feet toward head after <see cref="RotateTransform.Angle"/> clockwise (WPF),
    /// in device coords (+X right, +Y down).
    /// </summary>
    private static Vector TransformMessageOverlayDirection(GeneralTransform t, Vector v)
    {
        var o = t.Transform(new Point(0, 0));
        var p = t.Transform(new Point(v.X, v.Y));
        var r = new Vector(p.X - o.X, p.Y - o.Y);
        if (r.LengthSquared < 1e-12)
            return v;
        r.Normalize();
        return r;
    }

    /// <summary>
    /// Unit peek/travel directions from horizontal-left through straight-up to horizontal-right.
    /// Uses φ ∈ [-π, 0] so sin φ ≤ 0 (+Y is down): never aims straight down or into the lower half-plane.
    /// </summary>
    private static Vector SampleMessageOverlayGnomePeekDirectionUpperSemicircle()
    {
        var phi = -Random.Shared.NextDouble() * Math.PI;
        var u = new Vector(Math.Cos(phi), Math.Sin(phi));
        u.Normalize();
        return u;
    }

    /// <summary>
    /// On the left–up–right arc, picks the direction best aligned with <paramref name="target"/> (e.g. ink outward normal).
    /// </summary>
    private static Vector MessageOverlayGnomeBestDirectionUpperArcToward(Vector target)
    {
        if (target.LengthSquared < 1e-12)
            target = new Vector(0, -1);
        else
            target.Normalize();

        Vector best = new Vector(0, -1);
        var bestDot = Vector.Multiply(best, target);
        const int steps = 96;
        for (var k = 1; k < steps; k++)
        {
            var phi = -Math.PI * k / steps;
            var u = new Vector(Math.Cos(phi), Math.Sin(phi));
            var d = Vector.Multiply(u, target);
            if (d > bestDot)
            {
                bestDot = d;
                best = u;
            }
        }
        best.Normalize();
        return best;
    }

    private void ShowMessageOverlayGnomePeek()
    {
        if (MessageOverlayTextEffectCanvas == null || MessageOverlayText == null)
            return;
        var text = MessageOverlayText.Text ?? string.Empty;
        if (text.Length == 0)
            return;

        var indices = new List<int>();
        for (var i = 0; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i]))
                indices.Add(i);
        if (indices.Count == 0)
            return;

        const double peekOutMs = 2200;
        const double holdMsFull = 3600;
        var holdMs = holdMsFull / 3.0;
        var peekBackMs = peekOutMs / 4.0;
        var totalMs = peekOutMs + holdMs + peekBackMs;
        var holdEndMs = peekOutMs + holdMs;
        const double peekTravelScale = 1.0 / 3.0;
        const double minGnomeScale = 0.34;
        const double gnomeScaleStep = 0.065;
        const int inkSampleGrid = 9;

        foreach (var idx in indices.OrderBy(_ => Random.Shared.Next()))
        {
            if (!TryRasterMessageOverlayCharInkSolid(text, idx, out var solid, out var pw, out var ph, out var rs, out var inkLocalRect, out var textToEffect, out var lineH))
                continue;

            for (var pickAttempt = 0; pickAttempt < 14; pickAttempt++)
            {
                if (!TryPickMessageOverlayGnomeInkPeekOrigin(solid, pw, ph, rs, inkLocalRect, textToEffect, out var anchorCanvas, out var outwardCanvas))
                    break;

                var nInk = outwardCanvas;
                if (nInk.LengthSquared < 1e-12)
                    nInk = new Vector(0, -1);
                else
                    nInk.Normalize();

                var headDir = MessageOverlayGnomeBestDirectionUpperArcToward(nInk);
                for (var attempt = 0; attempt < 72; attempt++)
                {
                    var u = SampleMessageOverlayGnomePeekDirectionUpperSemicircle();
                    if (Vector.Multiply(u, nInk) >= 0.015)
                    {
                        headDir = u;
                        break;
                    }
                }

                headDir.Normalize();
                if (headDir.Y > 1e-6 || Vector.Multiply(headDir, nInk) < -0.02)
                    headDir = MessageOverlayGnomeBestDirectionUpperArcToward(nInk);

                headDir.Normalize();
                var tiltDeg = Math.Atan2(headDir.X, -headDir.Y) * 180.0 / Math.PI;

                for (var gnomeScale = 1.0; gnomeScale >= minGnomeScale - 1e-6; gnomeScale -= gnomeScaleStep)
                {
                    var gnome = BuildMessageOverlayGnomeVisual(lineH * gnomeScale);
                    var gw = gnome.Width;
                    var gh = gnome.Height;

                    var maxOutX = gw * 0.45 * peekTravelScale;
                    var maxOutY = gh * 0.42 * peekTravelScale;
                    var inset = Math.Max(1.5, Math.Min(gw, gh) * 0.07);

                    var magPeek = Math.Sqrt(maxOutX * maxOutX + maxOutY * maxOutY);
                    var maxPeekForSixtyPercentShown = Math.Max(gw, gh) * 0.6;
                    magPeek = Math.Min(magPeek, maxPeekForSixtyPercentShown);
                    var magHide = magPeek + inset;

                    var inwardDepth = Math.Min(gw, gh) * 0.26;
                    var centerCanvas = anchorCanvas - headDir * inwardDepth;

                    var canvasLeft = centerCanvas.X - gw / 2.0;
                    var canvasTop = centerCanvas.Y - gh / 2.0;

                    var peekX = magPeek * headDir.X;
                    var peekY = magPeek * headDir.Y;
                    var hidX = -magHide * headDir.X;
                    var hidY = -magHide * headDir.Y;

                    if (!MessageOverlayGnomePoseFullyOverSolidInk(
                            solid, pw, ph, rs, inkLocalRect, textToEffect, canvasLeft, canvasTop, gw, gh, tiltDeg, hidX, hidY, inkSampleGrid))
                        continue;

                    var translate = new TranslateTransform();
                    AttachMessageOverlayGnomeRotateThenTranslate(gnome, translate, tiltDeg, gw, gh);

                    translate.X = hidX;
                    translate.Y = hidY;
                    Canvas.SetLeft(gnome, canvasLeft);
                    Canvas.SetTop(gnome, canvasTop);
                    gnome.Opacity = 1;
                    MessageOverlayTextEffectCanvas.Children.Add(gnome);

                    var xAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
                    xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(hidX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(peekX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(peekOutMs))));
                    xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(peekX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdEndMs))));
                    xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(hidX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs))));

                    var yAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs) };
                    yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(hidY, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(peekY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(peekOutMs))));
                    yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(peekY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdEndMs))));
                    yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(hidY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs))));
                    yAnim.Completed += (_, _) =>
                    {
                        if (MessageOverlayTextEffectCanvas != null && MessageOverlayTextEffectCanvas.Children.Contains(gnome))
                            MessageOverlayTextEffectCanvas.Children.Remove(gnome);
                    };

                    translate.BeginAnimation(TranslateTransform.XProperty, xAnim);
                    translate.BeginAnimation(TranslateTransform.YProperty, yAnim);
                    return;
                }
            }
        }
    }

    private static BitmapImage MessageOverlayGnomeBitmap
    {
        get
        {
            if (_messageOverlayGnomeBitmap != null)
                return _messageOverlayGnomeBitmap;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Assets/MessageOverlayGnome.png", UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _messageOverlayGnomeBitmap = bmp;
            return bmp;
        }
    }

    private static FrameworkElement BuildMessageOverlayGnomeVisual(double letterHeight)
    {
        var src = MessageOverlayGnomeBitmap;
        var aspect = src.PixelWidth / (double)src.PixelHeight;
        var totalHeight = Math.Clamp(letterHeight * 0.08, 3.0, 74.0);
        var width = totalHeight * aspect;

        return new Image
        {
            Source = src,
            Width = width,
            Height = totalHeight,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
    }

    private bool TryGetMessageOverlayCharLocalMetrics(
        string text,
        int charIndex,
        out Rect localRect,
        out GeneralTransform textToCanvas,
        out double lineHeight,
        out FormattedText charFormatted)
    {
        localRect = default;
        textToCanvas = default!;
        lineHeight = 0;
        charFormatted = new FormattedText(
            " ",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            Brushes.Black,
            1.0);
        if (MessageOverlayText == null || MessageOverlayTextEffectCanvas == null)
            return false;
        if (string.IsNullOrEmpty(text) || charIndex < 0 || charIndex >= text.Length)
            return false;

        var typeface = new Typeface(
            MessageOverlayText.FontFamily,
            MessageOverlayText.FontStyle,
            MessageOverlayText.FontWeight,
            MessageOverlayText.FontStretch);
        double dpi;
        try
        {
            dpi = VisualTreeHelper.GetDpi(MessageOverlayText).PixelsPerDip;
        }
        catch
        {
            dpi = 1.0;
        }
        var fontSize = MessageOverlayText.FontSize;

        var normalized = text.Replace("\r", string.Empty);
        var lines = normalized.Split('\n');
        var lineWidths = new double[lines.Length];
        var lineHeights = new double[lines.Length];
        var maxWidth = 0.0;
        for (var i = 0; i < lines.Length; i++)
        {
            var ft = new FormattedText(
                lines[i].Length == 0 ? " " : lines[i],
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                dpi);
            lineWidths[i] = ft.WidthIncludingTrailingWhitespace;
            lineHeights[i] = ft.Height;
            if (lineWidths[i] > maxWidth)
                maxWidth = lineWidths[i];
        }

        var runIdx = charIndex;
        var lineNum = 0;
        while (lineNum < lines.Length)
        {
            if (runIdx <= lines[lineNum].Length)
                break;
            runIdx -= lines[lineNum].Length + 1;
            lineNum++;
        }
        if (lineNum >= lines.Length || runIdx < 0 || runIdx >= lines[lineNum].Length)
            return false;

        var lineText = lines[lineNum];
        double prefixWidth = 0;
        if (runIdx > 0)
        {
            var prefixFt = new FormattedText(
                lineText.Substring(0, runIdx),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                dpi);
            prefixWidth = prefixFt.WidthIncludingTrailingWhitespace;
        }
        charFormatted = new FormattedText(
            lineText[runIdx].ToString(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            dpi);
        var charWidth = charFormatted.WidthIncludingTrailingWhitespace;
        lineHeight = lineHeights[lineNum];

        var centerOffset = (maxWidth - lineWidths[lineNum]) / 2.0;
        double y = 0;
        for (var i = 0; i < lineNum; i++)
            y += lineHeights[i];

        localRect = new Rect(prefixWidth + centerOffset, y, charWidth, lineHeight);

        try
        {
            textToCanvas = MessageOverlayText.TransformToVisual(MessageOverlayTextEffectCanvas);
        }
        catch
        {
            return false;
        }
        return true;
    }

    private bool TryGetMessageOverlayCharRect(string text, int charIndex, out Rect canvasRect)
    {
        canvasRect = default;
        if (!TryGetMessageOverlayCharLocalMetrics(text, charIndex, out var localRect, out var t, out _, out _))
            return false;
        canvasRect = t.TransformBounds(localRect);
        return true;
    }

    private bool TryRasterMessageOverlayCharInkSolid(
        string text,
        int charIndex,
        out bool[,] solid,
        out int pw,
        out int ph,
        out int rasterScale,
        out Rect inkLocalRect,
        out GeneralTransform textToEffect,
        out double lineHeight)
    {
        solid = null!;
        pw = 0;
        ph = 0;
        rasterScale = 4;
        inkLocalRect = default;
        textToEffect = default!;
        lineHeight = 0;
        if (!TryGetMessageOverlayCharLocalMetrics(text, charIndex, out inkLocalRect, out textToEffect, out lineHeight, out var charFt))
            return false;

        rasterScale = 4;
        pw = Math.Max(8, (int)Math.Ceiling(charFt.WidthIncludingTrailingWhitespace * rasterScale));
        ph = Math.Max(8, (int)Math.Ceiling(charFt.Height * rasterScale));

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, pw, ph));
            dc.PushTransform(new ScaleTransform(rasterScale, rasterScale));
            dc.DrawText(charFt, new Point(0, 0));
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var stride = pw * 4;
        var pixels = new byte[stride * ph];
        rtb.CopyPixels(new Int32Rect(0, 0, pw, ph), pixels, stride, 0);

        solid = new bool[ph, pw];
        var inkCount = 0;
        for (var py = 0; py < ph; py++)
        {
            var row = py * stride;
            for (var px = 0; px < pw; px++)
            {
                var a = pixels[row + px * 4 + 3];
                if (a > 140)
                {
                    solid[py, px] = true;
                    inkCount++;
                }
            }
        }

        return inkCount > 0;
    }

    private bool TryPickMessageOverlayGnomeInkPeekOrigin(
        bool[,] solid,
        int pw,
        int ph,
        int rasterScale,
        Rect inkLocalRect,
        GeneralTransform textToEffect,
        out Point anchorCanvas,
        out Vector outwardCanvas)
    {
        anchorCanvas = default;
        outwardCanvas = new Vector(0, -1);

        var exterior = new bool[ph, pw];
        var q = new Queue<(int x, int y)>();

        void TryNeighbor(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= pw || ny >= ph)
                return;
            if (solid[ny, nx] || exterior[ny, nx])
                return;
            exterior[ny, nx] = true;
            q.Enqueue((nx, ny));
        }

        for (var x = 0; x < pw; x++)
        {
            if (!solid[0, x])
            {
                exterior[0, x] = true;
                q.Enqueue((x, 0));
            }
            if (!solid[ph - 1, x])
            {
                exterior[ph - 1, x] = true;
                q.Enqueue((x, ph - 1));
            }
        }
        for (var y = 0; y < ph; y++)
        {
            if (!solid[y, 0])
            {
                exterior[y, 0] = true;
                q.Enqueue((0, y));
            }
            if (!solid[y, pw - 1])
            {
                exterior[y, pw - 1] = true;
                q.Enqueue((pw - 1, y));
            }
        }

        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            TryNeighbor(x + 1, y);
            TryNeighbor(x - 1, y);
            TryNeighbor(x, y + 1);
            TryNeighbor(x, y - 1);
        }

        var hole = new bool[ph, pw];
        for (var py = 0; py < ph; py++)
        {
            for (var px = 0; px < pw; px++)
                hole[py, px] = !solid[py, px] && !exterior[py, px];
        }

        bool TouchesHole(int px, int py)
        {
            if (px + 1 < pw && hole[py, px + 1])
                return true;
            if (px > 0 && hole[py, px - 1])
                return true;
            if (py + 1 < ph && hole[py + 1, px])
                return true;
            if (py > 0 && hole[py - 1, px])
                return true;
            return false;
        }

        bool IsExtNeighbor(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= pw || ny >= ph)
                return true;
            return exterior[ny, nx];
        }

        var boundary = new List<(int bx, int by)>();
        for (var py = 0; py < ph; py++)
        {
            for (var px = 0; px < pw; px++)
            {
                if (!solid[py, px])
                    continue;
                if (TouchesHole(px, py))
                    continue;
                if (IsExtNeighbor(px + 1, py) || IsExtNeighbor(px - 1, py) ||
                    IsExtNeighbor(px, py + 1) || IsExtNeighbor(px, py - 1))
                    boundary.Add((px, py));
            }
        }

        if (boundary.Count == 0)
            return false;

        var pick = boundary[Random.Shared.Next(boundary.Count)];
        var bx = pick.bx;
        var by = pick.by;

        double ox = 0, oy = 0;
        void AddOut(int nx, int ny)
        {
            if (!IsExtNeighbor(nx, ny))
                return;
            ox += nx - bx;
            oy += ny - by;
        }
        AddOut(bx + 1, by);
        AddOut(bx - 1, by);
        AddOut(bx, by + 1);
        AddOut(bx, by - 1);

        var dirBmp = new Vector(ox, oy);
        if (dirBmp.LengthSquared < 1e-6)
            dirBmp = new Vector(0, -1);
        else
            dirBmp.Normalize();

        var vLocal = new Vector(dirBmp.X / rasterScale, dirBmp.Y / rasterScale);
        vLocal.Normalize();
        outwardCanvas = TransformMessageOverlayDirection(textToEffect, vLocal);

        var ptLocal = new Point(inkLocalRect.X + (bx + 0.5) / rasterScale, inkLocalRect.Y + (by + 0.5) / rasterScale);
        anchorCanvas = textToEffect.Transform(ptLocal);

        return true;
    }
}
