using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Noted;

public partial class MainWindow
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MidiPlayerMciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool MidiPlayerMciGetErrorString(int errorCode, StringBuilder buffer, int bufferLength);

    private sealed class MidiHeaderInfo
    {
        public int FormatType { get; init; }
        public int TrackCount { get; init; }
        public int Division { get; init; }
        public bool IsSmpte { get; init; }
        public long FileSizeBytes { get; init; }
        public bool IsRiff { get; init; }
        public bool HasMthd { get; init; }
    }

    private sealed class MidiSong
    {
        public required string Title { get; init; }
        public required string Path { get; init; }
        public required string Group { get; init; }
        public bool IsCustom { get; init; }
    }

    private const string MidiCustomSongsFileName = "midi-custom-songs.json";
    private const string MidiCustomGroupName = "Custom";
    private const string MidiOtherGroupName = "Other";

    private static (string Group, string Title) SplitMidiTitle(string fileNameNoExt)
    {
        var idx = fileNameNoExt.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
        {
            var grp = fileNameNoExt[..idx].Trim();
            var ttl = fileNameNoExt[(idx + 3)..].Trim();
            if (grp.Length > 0 && ttl.Length > 0)
                return (grp, ttl);
        }
        return (MidiOtherGroupName, fileNameNoExt);
    }

    private string MidiCustomSongsPath() => Path.Combine(_backupFolder, MidiCustomSongsFileName);

    private List<string> LoadCustomMidiPaths()
    {
        try
        {
            var path = MidiCustomSongsPath();
            if (!File.Exists(path))
                return new List<string>();
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private void SaveCustomMidiPaths(IEnumerable<string> paths)
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            var ordered = paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MidiCustomSongsPath(), json);
        }
        catch
        {
            // Non-critical persistence.
        }
    }

    private static string MidiResourcesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "Plugins", "resources", "midi");

    private List<MidiSong> BuildMidiPlaylist(IEnumerable<string> customPaths)
    {
        var songs = new List<MidiSong>();

        foreach (var path in customPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var (_, title) = SplitMidiTitle(name);
            songs.Add(new MidiSong { Title = title, Path = path, Group = MidiCustomGroupName, IsCustom = true });
        }

        var resourcesDir = MidiResourcesDirectory();
        if (Directory.Exists(resourcesDir))
        {
            var bundled = Directory
                .EnumerateFiles(resourcesDir, "*.mid", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
            foreach (var path in bundled)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var (group, title) = SplitMidiTitle(name);
                songs.Add(new MidiSong { Title = title, Path = path, Group = group, IsCustom = false });
            }
        }

        return songs;
    }

    private static Brush BrushForMidiGroup(string group, int orderIndex, int totalGroups)
    {
        if (string.Equals(group, MidiCustomGroupName, StringComparison.Ordinal))
            return new SolidColorBrush(Color.FromRgb(0x55, 0x6B, 0x84));
        if (totalGroups <= 0)
            return new SolidColorBrush(Color.FromRgb(0x42, 0x6E, 0x9E));
        var hue = 300.0 * orderIndex / Math.Max(1, totalGroups);
        return new SolidColorBrush(HslToRgb(hue, 0.55, 0.45));
    }

    private void ShowMidiPlayerDialog()
    {
        var dlg = new Window
        {
            Title = "MIDI Player",
            Width = 1140,
            Height = 740,
            MinWidth = 920,
            MinHeight = 560,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var alias = "noted_midi_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var warmupAlias = "noted_midi_warm_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const long preloadLeadMs = 10_000;
        var isOpen = false;
        var isPlaying = false;
        var isPaused = false;
        var seekDragging = false;
        var hasDialogShown = false;
        long lengthMs = 0;
        long positionMs = 0;
        int? preloadedNextIndex = null;
        string? preloadedNextPath = null;
        int? preloadedStartIndex = null;
        string? preloadedStartPath = null;

        var customPaths = LoadCustomMidiPaths();
        var playlist = BuildMidiPlaylist(customPaths);
        var currentIndex = -1;
        string? currentLoadedPath = null;
        var rng = new Random();
        var shuffleHistory = new List<int>();

        var itemBordersByIndex = new Dictionary<int, Border>();
        Border? currentHighlightedItem = null;
        var rowBaseBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x17, 0x24, 0x3B));
        var rowHighlightBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x9C, 0xFF));
        var rowSelectedFillBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0x4F, 0x9C, 0xFF));
        var rowHoverBrush = new SolidColorBrush(Color.FromArgb(0x77, 0x29, 0x4A, 0x7A));

        dlg.Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0F, 0x1A));
        var root = new DockPanel { Margin = new Thickness(12) };

        // ---- Header ----------------------------------------------------------------------
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerTitle = new TextBlock
        {
            Text = "MIDI Player",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        var headerDragArea = new Border
        {
            Background = Brushes.Transparent,
            Child = headerTitle
        };
        headerDragArea.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { dlg.DragMove(); } catch { /* ignore */ }
            }
        };
        Grid.SetColumn(headerDragArea, 0);
        header.Children.Add(headerDragArea);
        var btnHeaderClose = new Button
        {
            Content = "\uE711",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 32,
            Height = 32,
            FontSize = 12,
            ToolTip = "Close",
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0)
        };
        btnHeaderClose.Click += (_, _) => dlg.Close();
        Grid.SetColumn(btnHeaderClose, 1);
        header.Children.Add(btnHeaderClose);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- Playlist panel (takes most of window) ---------------------------------------
        var queueCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x2D, 0x49)),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x0F, 0x17, 0x2A),
                Color.FromRgb(0x0B, 0x14, 0x24),
                90),
            Padding = new Thickness(10)
        };
        var queueDock = new DockPanel();
        queueCard.Child = queueDock;

        var queueTop = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        queueTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        queueTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconButtonStyle = new Style(typeof(Button));
        iconButtonStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31))));
        iconButtonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        iconButtonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63))));
        iconButtonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        iconButtonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        iconButtonStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        iconButtonStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        iconButtonStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var iconButtonTemplate = new ControlTemplate(typeof(Button));
        var iconButtonBorder = new FrameworkElementFactory(typeof(Border));
        iconButtonBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        iconButtonBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);
        iconButtonBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        iconButtonBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        iconButtonBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var iconButtonPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        iconButtonPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        iconButtonPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        iconButtonBorder.AppendChild(iconButtonPresenter);
        iconButtonTemplate.VisualTree = iconButtonBorder;
        iconButtonTemplate.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1D, 0x2D, 0x49))),
                new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x4A, 0x64, 0x95)))
            }
        });
        iconButtonTemplate.Triggers.Add(new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x0D, 0x17, 0x28)))
            }
        });
        iconButtonTemplate.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
            Setters =
            {
                new Setter(UIElement.OpacityProperty, 0.52),
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)))
            }
        });
        iconButtonStyle.Setters.Add(new Setter(Control.TemplateProperty, iconButtonTemplate));

        var iconToggleStyle = new Style(typeof(ToggleButton));
        iconToggleStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31))));
        iconToggleStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        iconToggleStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63))));
        iconToggleStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        iconToggleStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        iconToggleStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        iconToggleStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        iconToggleStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var iconToggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var iconToggleBorder = new FrameworkElementFactory(typeof(Border));
        iconToggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        iconToggleBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        iconToggleBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        iconToggleBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var iconTogglePresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        iconTogglePresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        iconTogglePresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        iconToggleBorder.AppendChild(iconTogglePresenter);
        iconToggleTemplate.VisualTree = iconToggleBorder;
        iconToggleTemplate.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1D, 0x2D, 0x49))),
                new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x4A, 0x64, 0x95)))
            }
        });
        iconToggleTemplate.Triggers.Add(new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x63, 0xEA, 0xA0))),
                new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0x6A))),
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x12, 0x2B, 0x27)))
            }
        });
        iconToggleTemplate.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
            Setters =
            {
                new Setter(UIElement.OpacityProperty, 0.52)
            }
        });
        iconToggleStyle.Setters.Add(new Setter(Control.TemplateProperty, iconToggleTemplate));

        var nowPlayingStack = new StackPanel { Margin = new Thickness(2, 0, 0, 0) };
        nowPlayingStack.Children.Add(new TextBlock
        {
            Text = "NOW PLAYING",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0xD4, 0xFF)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        var lblNowPlaying = new TextBlock
        {
            Text = "Nothing playing",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        nowPlayingStack.Children.Add(lblNowPlaying);
        var lblNowPlayingMeta = new TextBlock
        {
            Text = "Pick a song from the playlist",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB9, 0xD8)),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nowPlayingStack.Children.Add(lblNowPlayingMeta);

        Grid.SetColumn(nowPlayingStack, 0);
        queueTop.Children.Add(nowPlayingStack);

        var queueButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top
        };
        var btnAdd = new Button
        {
            Content = "\uE710",
            Width = 34,
            Height = 34,
            FontSize = 16,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Style = iconButtonStyle,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Add MIDI file(s) to Custom"
        };
        queueButtons.Children.Add(btnAdd);
        Grid.SetColumn(queueButtons, 1);
        queueTop.Children.Add(queueButtons);

        DockPanel.SetDock(queueTop, Dock.Top);
        queueDock.Children.Add(queueTop);

        var playlistScrollBarStyle = (Style)XamlReader.Parse(
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="ScrollBar">
              <Setter Property="Width" Value="12"/>
              <Setter Property="Background" Value="#0D1627"/>
              <Setter Property="BorderBrush" Value="#1F2D49"/>
              <Setter Property="BorderThickness" Value="1"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="2">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0"/>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb>
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border x:Name="ThumbBorder" Background="#4A6A9D" CornerRadius="4"/>
                                <ControlTemplate.Triggers>
                                  <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="ThumbBorder" Property="Background" Value="#5E83BF"/>
                                  </Trigger>
                                  <Trigger Property="IsDragging" Value="True">
                                    <Setter TargetName="ThumbBorder" Property="Background" Value="#73A0E5"/>
                                  </Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0"/>
                        </Track.IncreaseRepeatButton>
                      </Track>
                    </Border>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);

        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x2D, 0x49)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0)
        };
        var playlistScrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Minimum = 0,
            Maximum = 0,
            Value = 0,
            SmallChange = 24,
            Visibility = Visibility.Collapsed,
            Style = playlistScrollBarStyle
        };

        var listArea = new Grid();
        listArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(listScroll, 0);
        Grid.SetColumn(playlistScrollBar, 1);
        listArea.Children.Add(listScroll);
        listArea.Children.Add(playlistScrollBar);
        var listStack = new StackPanel();
        listScroll.Content = listStack;
        queueDock.Children.Add(listArea);

        Grid.SetRow(queueCard, 0);
        layout.Children.Add(queueCard);

        // ---- Player controls panel (under list) ------------------------------------------
        var controlCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x2D, 0x49)),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x0E, 0x15, 0x25),
                Color.FromRgb(0x0A, 0x10, 0x1C),
                90),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var controlsRoot = new StackPanel();
        controlCard.Child = controlsRoot;

        var seekRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lblPosition = new TextBlock
        {
            Text = "00:00",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = Brushes.White
        };
        Grid.SetColumn(lblPosition, 0);
        var seekSlider = new Slider
        {
            Minimum = 0,
            Maximum = 0,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        Grid.SetColumn(seekSlider, 1);
        var lblLength = new TextBlock
        {
            Text = "00:00",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = Brushes.White
        };
        Grid.SetColumn(lblLength, 2);
        seekRow.Children.Add(lblPosition);
        seekRow.Children.Add(seekSlider);
        seekRow.Children.Add(lblLength);
        controlsRoot.Children.Add(seekRow);

        var transportRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var btnPrev = new Button
        {
            Content = "⏮",
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Width = 44,
            Height = 44,
            Style = iconButtonStyle,
            IsEnabled = false
        };
        var btnPlay = new Button
        {
            Content = "▶",
            Padding = new Thickness(0, -1, 0, 0),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Width = 52,
            Height = 52,
            Margin = new Thickness(10, 0, 10, 0),
            Style = iconButtonStyle,
            IsEnabled = false
        };
        var btnPause = new Button
        {
            Content = "⏸",
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Width = 44,
            Height = 44,
            Style = iconButtonStyle,
            IsEnabled = false
        };
        var btnNext = new Button
        {
            Content = "⏭",
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Width = 44,
            Height = 44,
            Margin = new Thickness(10, 0, 10, 0),
            Style = iconButtonStyle,
            IsEnabled = false
        };
        var btnStop = new Button
        {
            Content = "⏹",
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Width = 44,
            Height = 44,
            Style = iconButtonStyle,
            IsEnabled = false
        };
        transportRow.Children.Add(btnPrev);
        transportRow.Children.Add(btnPlay);
        transportRow.Children.Add(btnPause);
        transportRow.Children.Add(btnNext);
        transportRow.Children.Add(btnStop);
        controlsRoot.Children.Add(transportRow);

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var btnShuffle = new ToggleButton
        {
            Content = "🔀",
            Width = 36,
            Height = 36,
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Style = iconToggleStyle,
            Margin = new Thickness(0, 0, 12, 0),
            IsChecked = true,
            ToolTip = "Shuffle"
        };
        var btnLoop = new ToggleButton
        {
            Content = "🔁",
            Width = 36,
            Height = 36,
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Style = iconToggleStyle,
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "Loop current track"
        };
        btnShuffle.Checked += (_, _) =>
        {
            ClearNextPreload();
            ClearStartPreload();
            PrimeInitialPreload();
        };
        btnShuffle.Unchecked += (_, _) =>
        {
            ClearNextPreload();
            ClearStartPreload();
            PrimeInitialPreload();
        };
        btnLoop.Checked += (_, _) => ClearNextPreload();
        btnLoop.Unchecked += (_, _) => ClearNextPreload();
        modeRow.Children.Add(btnShuffle);
        modeRow.Children.Add(btnLoop);
        controlsRoot.Children.Add(modeRow);

        var lblStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB9, 0xD8)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        controlsRoot.Children.Add(lblStatus);

        var txtInfo = new TextBlock
        {
            Text = "No file loaded.",
            FontFamily = new FontFamily("Consolas, Courier New"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x86, 0xB0)),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        controlsRoot.Children.Add(txtInfo);

        Grid.SetRow(controlCard, 1);
        layout.Children.Add(controlCard);

        root.Children.Add(layout);

        // ---- Helpers -----------------------------------------------------------------
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var syncingPlaylistScroll = false;

        void SyncPlaylistScrollBarFromViewer()
        {
            if (syncingPlaylistScroll)
                return;
            syncingPlaylistScroll = true;
            try
            {
                var canScroll = listScroll.ScrollableHeight > 0;
                playlistScrollBar.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
                playlistScrollBar.Minimum = 0;
                playlistScrollBar.Maximum = Math.Max(0, listScroll.ScrollableHeight);
                playlistScrollBar.LargeChange = Math.Max(1, listScroll.ViewportHeight);
                playlistScrollBar.SmallChange = 24;
                playlistScrollBar.Value = Math.Max(0, Math.Min(listScroll.VerticalOffset, playlistScrollBar.Maximum));
            }
            finally
            {
                syncingPlaylistScroll = false;
            }
        }

        void SetStatus(string text, Brush? color = null)
        {
            lblStatus.Text = text;
            lblStatus.Foreground = color ?? new SolidColorBrush(Color.FromRgb(0xC1, 0xCF, 0xEA));
        }

        static string FormatTime(long ms)
        {
            if (ms < 0)
                ms = 0;
            var totalSeconds = ms / 1000;
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        bool TryMci(string command, StringBuilder? returnValue, out int errorCode)
        {
            errorCode = MidiPlayerMciSendString(command, returnValue, returnValue?.Capacity ?? 0, IntPtr.Zero);
            return errorCode == 0;
        }

        string GetMciError(int errorCode)
        {
            var sb = new StringBuilder(256);
            MidiPlayerMciGetErrorString(errorCode, sb, sb.Capacity);
            return sb.ToString();
        }

        long QueryStatusNumber(string what)
        {
            var sb = new StringBuilder(64);
            if (!TryMci($"status {alias} {what}", sb, out _))
                return 0;
            return long.TryParse(sb.ToString().Trim(), out var value) ? value : 0;
        }

        string QueryStatusText(string what)
        {
            var sb = new StringBuilder(64);
            return TryMci($"status {alias} {what}", sb, out _) ? sb.ToString().Trim() : string.Empty;
        }

        static int SwapEndian32(int value)
        {
            var u = (uint)value;
            return (int)(((u & 0x000000FFu) << 24) |
                         ((u & 0x0000FF00u) << 8) |
                         ((u & 0x00FF0000u) >> 8) |
                         ((u & 0xFF000000u) >> 24));
        }

        static int SwapEndian16(ushort value)
            => ((value & 0xFF) << 8) | ((value & 0xFF00) >> 8);

        MidiHeaderInfo? TryParseMidiHeader(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);
                if (fs.Length < 14)
                    return new MidiHeaderInfo { FileSizeBytes = fi.Length };

                var firstFour = br.ReadBytes(4);
                var isRiff = firstFour.Length == 4 && firstFour[0] == 'R' && firstFour[1] == 'I' && firstFour[2] == 'F' && firstFour[3] == 'F';

                if (isRiff)
                {
                    br.ReadInt32();
                    br.ReadBytes(4);
                    var foundData = false;
                    while (fs.Position + 8 <= fs.Length)
                    {
                        var chunkId = br.ReadBytes(4);
                        var chunkSize = br.ReadInt32();
                        if (chunkId.Length < 4)
                            break;
                        if (chunkId[0] == 'd' && chunkId[1] == 'a' && chunkId[2] == 't' && chunkId[3] == 'a')
                        {
                            firstFour = br.ReadBytes(4);
                            foundData = true;
                            break;
                        }
                        var skip = chunkSize + (chunkSize % 2);
                        if (skip < 0 || fs.Position + skip > fs.Length)
                            break;
                        fs.Seek(skip, SeekOrigin.Current);
                    }

                    if (!foundData)
                        return new MidiHeaderInfo { FileSizeBytes = fi.Length, IsRiff = true };
                }

                if (firstFour.Length < 4 || firstFour[0] != 'M' || firstFour[1] != 'T' || firstFour[2] != 'h' || firstFour[3] != 'd')
                    return new MidiHeaderInfo { FileSizeBytes = fi.Length, IsRiff = isRiff };

                var headerLength = SwapEndian32(br.ReadInt32());
                if (headerLength < 6 || fs.Position + 6 > fs.Length)
                    return new MidiHeaderInfo { FileSizeBytes = fi.Length, IsRiff = isRiff, HasMthd = true };
                var format = SwapEndian16(br.ReadUInt16());
                var tracks = SwapEndian16(br.ReadUInt16());
                var division = SwapEndian16(br.ReadUInt16());
                var isSmpte = (division & 0x8000) != 0;

                return new MidiHeaderInfo
                {
                    FormatType = format,
                    TrackCount = tracks,
                    Division = division,
                    IsSmpte = isSmpte,
                    FileSizeBytes = fi.Length,
                    IsRiff = isRiff,
                    HasMthd = true
                };
            }
            catch
            {
                return null;
            }
        }

        void UpdateButtons()
        {
            btnPlay.Content = "▶";
            btnPlay.ToolTip = isPaused ? "Resume" : "Play";
            btnPlay.IsEnabled = playlist.Count > 0 && !isPlaying;
            btnPause.IsEnabled = isOpen && isPlaying && !isPaused;
            btnStop.IsEnabled = isOpen && (isPlaying || isPaused);
            seekSlider.IsEnabled = isOpen && lengthMs > 0;
            btnPrev.IsEnabled = playlist.Count > 0;
            btnNext.IsEnabled = playlist.Count > 0;
        }

        void UpdateInfo(string path, MidiHeaderInfo? info)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"File: {Path.GetFileName(path)}");
            if (info == null)
            {
                sb.Append("(unable to read MIDI file)");
            }
            else
            {
                sb.Append($"Size: {info.FileSizeBytes:N0} bytes");
                if (info.IsRiff)
                    sb.Append("  (RIFF MIDI)");
                sb.AppendLine();
                if (info.HasMthd)
                {
                    var formatLabel = info.FormatType switch
                    {
                        0 => "0 (single track)",
                        1 => "1 (multi-track, simultaneous)",
                        2 => "2 (multi-song / multi-sequence)",
                        _ => info.FormatType.ToString()
                    };
                    sb.AppendLine($"Format: {formatLabel}");
                    sb.AppendLine($"Tracks: {info.TrackCount}");
                    if (info.IsSmpte)
                    {
                        var negFps = -(sbyte)((info.Division >> 8) & 0xFF);
                        var subFrame = info.Division & 0xFF;
                        sb.Append($"Division: SMPTE {negFps} fps, {subFrame} ticks/frame");
                    }
                    else
                    {
                        sb.Append($"Division: {info.Division} ticks per quarter note (PPQN)");
                    }
                }
                else
                {
                    sb.Append("(no MThd header found)");
                }
            }
            txtInfo.Text = sb.ToString().TrimEnd();
            txtInfo.Foreground = Brushes.White;
        }

        void CloseDevice()
        {
            timer.Stop();
            if (isOpen)
            {
                TryMci($"stop {alias}", null, out _);
                TryMci($"close {alias}", null, out _);
            }
            isOpen = false;
            isPlaying = false;
            isPaused = false;
            currentLoadedPath = null;
            ClearNextPreload();
        }

        void HighlightCurrent()
        {
            if (currentHighlightedItem is not null)
            {
                currentHighlightedItem.Background = rowBaseBrush;
                currentHighlightedItem.BorderBrush = Brushes.Transparent;
                currentHighlightedItem.BorderThickness = new Thickness(1);
            }
            currentHighlightedItem = null;

            if (currentIndex >= 0 && itemBordersByIndex.TryGetValue(currentIndex, out var item))
            {
                item.Background = rowSelectedFillBrush;
                item.BorderBrush = rowHighlightBrush;
                item.BorderThickness = new Thickness(2);
                item.BringIntoView();
                currentHighlightedItem = item;
            }
        }

        bool LoadFile(string path)
        {
            ClearStartPreload();
            ClearNextPreload();
            CloseDevice();
            if (!File.Exists(path))
            {
                SetStatus($"File not found: {path}", Brushes.IndianRed);
                return false;
            }

            var openCommand = $"open \"{path}\" type sequencer alias {alias}";
            if (!TryMci(openCommand, null, out var err))
            {
                SetStatus($"Failed to open MIDI: {GetMciError(err)}", Brushes.IndianRed);
                return false;
            }

            isOpen = true;
            TryMci($"set {alias} time format milliseconds", null, out _);
            lengthMs = QueryStatusNumber("length");
            positionMs = 0;

            seekSlider.Maximum = Math.Max(1, lengthMs);
            seekSlider.Value = 0;
            lblLength.Text = FormatTime(lengthMs);
            lblPosition.Text = "00:00";

            currentLoadedPath = path;
            var info = TryParseMidiHeader(path);
            UpdateInfo(path, info);

            if (currentIndex >= 0 && currentIndex < playlist.Count)
            {
                var song = playlist[currentIndex];
                lblNowPlaying.Text = song.Title;
                lblNowPlayingMeta.Text = song.Group;
            }
            else
            {
                var (group, title) = SplitMidiTitle(Path.GetFileNameWithoutExtension(path));
                lblNowPlaying.Text = title;
                lblNowPlayingMeta.Text = group;
            }

            UpdateButtons();
            HighlightCurrent();
            SetStatus($"Loaded {Path.GetFileName(path)} ({FormatTime(lengthMs)})");
            return true;
        }

        void Play()
        {
            if (!isOpen) return;
            var command = isPaused ? $"resume {alias}" : $"play {alias}";
            if (!TryMci(command, null, out var err))
            {
                SetStatus($"Play failed: {GetMciError(err)}", Brushes.IndianRed);
                return;
            }
            isPlaying = true;
            isPaused = false;
            timer.Start();
            UpdateButtons();
        }

        void ClearNextPreload()
        {
            preloadedNextIndex = null;
            preloadedNextPath = null;
        }

        void ClearStartPreload()
        {
            preloadedStartIndex = null;
            preloadedStartPath = null;
        }

        static bool IsValidIndex(int index, int count) => index >= 0 && index < count;

        bool TryWarmPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;
            if (!TryMci($"open \"{path}\" type sequencer alias {warmupAlias}", null, out _))
                return false;
            TryMci($"set {warmupAlias} time format milliseconds", null, out _);
            TryMci($"close {warmupAlias}", null, out _);
            return true;
        }

        int ResolveInitialPlayIndex()
        {
            if (playlist.Count == 0)
                return -1;
            if (currentIndex >= 0 && currentIndex < playlist.Count)
                return currentIndex;
            if (btnShuffle.IsChecked == true)
                return rng.Next(playlist.Count);
            return 0;
        }

        int ResolveStartPlayIndex()
        {
            if (playlist.Count == 0)
                return -1;

            if (currentIndex >= 0 && currentIndex < playlist.Count)
                return currentIndex;

            if (preloadedStartIndex.HasValue
                && IsValidIndex(preloadedStartIndex.Value, playlist.Count)
                && string.Equals(playlist[preloadedStartIndex.Value].Path, preloadedStartPath, StringComparison.OrdinalIgnoreCase))
            {
                return preloadedStartIndex.Value;
            }

            return ResolveInitialPlayIndex();
        }

        void PrimeInitialPreload()
        {
            if (!hasDialogShown || isOpen || playlist.Count == 0)
                return;
            var startIndex = ResolveInitialPlayIndex();
            if (!IsValidIndex(startIndex, playlist.Count))
                return;
            var path = playlist[startIndex].Path;
            if (string.Equals(path, preloadedStartPath, StringComparison.OrdinalIgnoreCase)
                && preloadedStartIndex == startIndex)
                return;
            if (TryWarmPath(path))
            {
                preloadedStartIndex = startIndex;
                preloadedStartPath = path;
            }
        }

        int ResolveAutoNextIndex(bool allowShuffle)
        {
            if (playlist.Count == 0)
                return -1;
            if (allowShuffle && btnShuffle.IsChecked == true)
                return PickShuffleIndex();
            if (currentIndex >= 0 && currentIndex + 1 < playlist.Count)
                return currentIndex + 1;
            return -1;
        }

        void TryPreloadUpcomingTrack(bool force)
        {
            if (!isOpen || !isPlaying || btnLoop.IsChecked == true || lengthMs <= 0)
                return;

            if (!force)
            {
                var remainingMs = lengthMs - positionMs;
                if (remainingMs > preloadLeadMs)
                    return;
            }

            var candidateIndex = preloadedNextIndex;
            if (candidateIndex is null
                || candidateIndex < 0
                || candidateIndex >= playlist.Count
                || candidateIndex == currentIndex)
            {
                var resolved = ResolveAutoNextIndex(allowShuffle: true);
                if (resolved < 0)
                    return;
                candidateIndex = resolved;
            }

            var nextPath = playlist[candidateIndex.Value].Path;
            if (string.IsNullOrWhiteSpace(nextPath) || !File.Exists(nextPath))
                return;
            if (string.Equals(nextPath, preloadedNextPath, StringComparison.OrdinalIgnoreCase))
                return;

            // Pre-open and close once to warm decoder/device for the specific upcoming file.
            if (TryWarmPath(nextPath))
            {
                preloadedNextIndex = candidateIndex;
                preloadedNextPath = nextPath;
            }
        }

        void Pause()
        {
            if (!isOpen || !isPlaying) return;
            if (!TryMci($"pause {alias}", null, out var err))
            {
                SetStatus($"Pause failed: {GetMciError(err)}", Brushes.IndianRed);
                return;
            }
            isPlaying = false;
            isPaused = true;
            timer.Stop();
            UpdateButtons();
            SetStatus("Paused.");
        }

        void Stop()
        {
            if (!isOpen) return;
            TryMci($"stop {alias}", null, out _);
            TryMci($"seek {alias} to start", null, out _);
            isPlaying = false;
            isPaused = false;
            timer.Stop();
            positionMs = 0;
            seekSlider.Value = 0;
            lblPosition.Text = "00:00";
            ClearNextPreload();
            UpdateButtons();
            SetStatus("Stopped.");
        }

        void ResetPlayerView()
        {
            currentIndex = -1;
            lengthMs = 0;
            positionMs = 0;
            seekSlider.Maximum = 0;
            seekSlider.Value = 0;
            currentLoadedPath = null;
            lblNowPlaying.Text = "Nothing playing";
            lblNowPlayingMeta.Text = "Pick a song from the playlist";
            lblLength.Text = "00:00";
            lblPosition.Text = "00:00";
            txtInfo.Text = "No file loaded.";
            txtInfo.Foreground = new SolidColorBrush(Color.FromRgb(0xC1, 0xCF, 0xEA));
            ClearStartPreload();
            ClearNextPreload();
            HighlightCurrent();
            UpdateButtons();
            PrimeInitialPreload();
        }

        void PerformSeek(long target)
        {
            if (!isOpen) return;
            if (target < 0) target = 0;
            if (lengthMs > 0 && target > lengthMs) target = lengthMs;

            var wasPlaying = isPlaying;
            TryMci($"stop {alias}", null, out _);
            TryMci($"seek {alias} to {target}", null, out _);
            if (wasPlaying)
            {
                if (TryMci($"play {alias}", null, out _))
                {
                    isPlaying = true;
                    isPaused = false;
                    timer.Start();
                }
            }
            else
            {
                isPlaying = false;
                isPaused = false;
                positionMs = target;
                lblPosition.Text = FormatTime(target);
            }
            UpdateButtons();
        }

        void PlayIndex(int index)
        {
            if (index < 0 || index >= playlist.Count)
                return;
            ClearStartPreload();
            currentIndex = index;
            shuffleHistory.Add(index);
            var song = playlist[index];
            if (LoadFile(song.Path))
            {
                Play();
                // Queue preload right after playback starts, without blocking the click path.
                dlg.Dispatcher.BeginInvoke(
                    () => TryPreloadUpcomingTrack(force: true),
                    DispatcherPriority.Background);
            }
        }

        int PickShuffleIndex()
        {
            if (playlist.Count == 0)
                return -1;
            if (playlist.Count == 1)
                return 0;
            for (var attempts = 0; attempts < 8; attempts++)
            {
                var pick = rng.Next(playlist.Count);
                if (pick != currentIndex)
                    return pick;
            }
            return rng.Next(playlist.Count);
        }

        void PlayNext()
        {
            if (playlist.Count == 0) return;
            int next;
            if (preloadedNextIndex.HasValue
                && preloadedNextIndex.Value >= 0
                && preloadedNextIndex.Value < playlist.Count
                && preloadedNextIndex.Value != currentIndex)
            {
                next = preloadedNextIndex.Value;
            }
            else if (btnShuffle.IsChecked == true)
            {
                next = PickShuffleIndex();
            }
            else
                next = currentIndex < 0 ? 0 : (currentIndex + 1) % playlist.Count;
            ClearNextPreload();
            PlayIndex(next);
        }

        void PlayPrev()
        {
            if (playlist.Count == 0) return;
            int prev;
            if (btnShuffle.IsChecked == true && shuffleHistory.Count >= 2)
            {
                shuffleHistory.RemoveAt(shuffleHistory.Count - 1);
                prev = shuffleHistory[^1];
                shuffleHistory.RemoveAt(shuffleHistory.Count - 1);
            }
            else
            {
                prev = currentIndex < 0 ? 0 : (currentIndex - 1 + playlist.Count) % playlist.Count;
            }
            ClearNextPreload();
            PlayIndex(prev);
        }

        void OnTick()
        {
            if (!isOpen)
                return;

            positionMs = QueryStatusNumber("position");
            if (!seekDragging)
            {
                seekSlider.Value = Math.Min(positionMs, seekSlider.Maximum);
                lblPosition.Text = FormatTime(positionMs);
            }

            TryPreloadUpcomingTrack(force: false);

            var mode = QueryStatusText("mode");
            if (isPlaying && mode == "stopped")
            {
                isPlaying = false;
                isPaused = false;
                timer.Stop();
                UpdateButtons();

                if (btnLoop.IsChecked == true)
                {
                    TryMci($"seek {alias} to start", null, out _);
                    Play();
                    SetStatus("Looping.");
                }
                else if (btnShuffle.IsChecked == true && playlist.Count > 0)
                {
                    PlayNext();
                }
                else if (currentIndex >= 0 && currentIndex + 1 < playlist.Count)
                {
                    PlayIndex(currentIndex + 1);
                }
                else
                {
                    CloseDevice();
                    ResetPlayerView();
                    SetStatus("Finished.");
                }
            }
        }

        timer.Tick += (_, _) => OnTick();

        // ---- Build/Rebuild the playlist UI ----------------------------------------------
        void RebuildPlaylistUi()
        {
            listStack.Children.Clear();
            itemBordersByIndex.Clear();
            currentHighlightedItem = null;

            if (playlist.Count == 0)
            {
                listStack.Children.Add(new TextBlock
                {
                    Text = "No tracks found.\nClick + to add MIDI files.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB0, 0xD0)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 24, 8, 8)
                });
                return;
            }

            var groupOrder = new List<string>();
            if (playlist.Any(s => s.Group == MidiCustomGroupName))
                groupOrder.Add(MidiCustomGroupName);
            foreach (var g in playlist
                         .Where(s => s.Group != MidiCustomGroupName)
                         .Select(s => s.Group)
                         .Distinct()
                         .OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
                groupOrder.Add(g);

            for (var gi = 0; gi < groupOrder.Count; gi++)
            {
                var group = groupOrder[gi];
                var groupBrush = BrushForMidiGroup(group, gi, groupOrder.Count);

                var headerCount = playlist.Count(s => s.Group == group);
                var headerPanel = new Border
                {
                    Background = groupBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 8, 0, 4),
                    Child = new TextBlock
                    {
                        Text = $"{group}  ({headerCount})",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White
                    }
                };
                listStack.Children.Add(headerPanel);

                for (var i = 0; i < playlist.Count; i++)
                {
                    if (playlist[i].Group != group)
                        continue;
                    var index = i;
                    var song = playlist[i];

                    var line = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    };
                    line.Inlines.Add(new Run
                    {
                        Text = song.Title,
                        FontWeight = FontWeights.SemiBold
                    });

                    var itemBorder = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 4, 6, 4),
                        Margin = new Thickness(4, 1, 4, 1),
                        Background = rowBaseBrush,
                        BorderBrush = Brushes.Transparent,
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        Child = line
                    };
                    itemBorder.MouseEnter += (_, _) =>
                    {
                        if (currentIndex != index)
                            itemBorder.Background = rowHoverBrush;
                    };
                    itemBorder.MouseLeave += (_, _) =>
                    {
                        if (currentIndex != index)
                            itemBorder.Background = rowBaseBrush;
                    };
                    itemBorder.MouseLeftButtonDown += (_, e) =>
                    {
                        PlayIndex(index);
                        e.Handled = true;
                    };

                    if (song.IsCustom)
                    {
                        var pathToRemove = song.Path;
                        var removeMenu = new MenuItem { Header = "Remove from playlist" };
                        removeMenu.Click += (_, e) =>
                        {
                            e.Handled = true;
                            customPaths = customPaths
                                .Where(p => !string.Equals(p, pathToRemove, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            SaveCustomMidiPaths(customPaths);
                            var wasCurrent = currentIndex == index;
                            if (wasCurrent)
                            {
                                CloseDevice();
                                ResetPlayerView();
                            }
                            playlist = BuildMidiPlaylist(customPaths);
                            ClearStartPreload();
                            ClearNextPreload();
                            if (!wasCurrent && currentIndex >= 0)
                            {
                                var stillPlayingPath = currentLoadedPath;
                                currentIndex = playlist.FindIndex(s => string.Equals(s.Path, stillPlayingPath, StringComparison.OrdinalIgnoreCase));
                            }
                            RebuildPlaylistUi();
                            UpdateButtons();
                            PrimeInitialPreload();
                            SetStatus("Removed from playlist.");
                        };
                        itemBorder.ContextMenu = new ContextMenu();
                        itemBorder.ContextMenu.Items.Add(removeMenu);
                    }

                    itemBordersByIndex[index] = itemBorder;
                    listStack.Children.Add(itemBorder);
                }
            }

            HighlightCurrent();
            listScroll.Dispatcher.BeginInvoke(SyncPlaylistScrollBarFromViewer, DispatcherPriority.Background);
        }

        // ---- Add files (browse) ---------------------------------------------------------
        void AddCustomFiles(IEnumerable<string> paths, bool playFirstAdded)
        {
            var added = new List<string>();
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (!File.Exists(p)) continue;
                var ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext != ".mid" && ext != ".midi" && ext != ".rmi") continue;
                if (customPaths.Any(c => string.Equals(c, p, StringComparison.OrdinalIgnoreCase)))
                    continue;
                customPaths.Add(p);
                added.Add(p);
            }
            if (added.Count == 0)
            {
                SetStatus("No new MIDI files added.");
                return;
            }
            SaveCustomMidiPaths(customPaths);

            string? rememberedPath = null;
            if (currentIndex >= 0 && currentIndex < playlist.Count)
                rememberedPath = playlist[currentIndex].Path;

            playlist = BuildMidiPlaylist(customPaths);
            ClearStartPreload();
            ClearNextPreload();

            if (rememberedPath is not null)
                currentIndex = playlist.FindIndex(s => string.Equals(s.Path, rememberedPath, StringComparison.OrdinalIgnoreCase));

            RebuildPlaylistUi();
            UpdateButtons();
            PrimeInitialPreload();
            SetStatus($"Added {added.Count} track{(added.Count == 1 ? string.Empty : "s")} to Custom.");

            if (playFirstAdded)
            {
                var idx = playlist.FindIndex(s => string.Equals(s.Path, added[0], StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    PlayIndex(idx);
            }
        }

        // ---- Wire up events --------------------------------------------------------------
        btnAdd.Click += (_, _) =>
        {
            var ofd = new OpenFileDialog
            {
                Title = "Add MIDI files",
                Filter = "MIDI files (*.mid;*.midi;*.rmi)|*.mid;*.midi;*.rmi|All files (*.*)|*.*",
                Multiselect = true
            };
            if (ofd.ShowDialog(dlg) == true)
                AddCustomFiles(ofd.FileNames, playFirstAdded: true);
        };

        listScroll.ScrollChanged += (_, _) => SyncPlaylistScrollBarFromViewer();
        listScroll.SizeChanged += (_, _) => SyncPlaylistScrollBarFromViewer();
        playlistScrollBar.ValueChanged += (_, _) =>
        {
            if (syncingPlaylistScroll)
                return;
            syncingPlaylistScroll = true;
            try
            {
                listScroll.ScrollToVerticalOffset(playlistScrollBar.Value);
            }
            finally
            {
                syncingPlaylistScroll = false;
            }
        };

        btnPlay.Click += (_, _) =>
        {
            if (!isOpen && playlist.Count > 0)
            {
                var startIndex = ResolveStartPlayIndex();
                if (startIndex >= 0)
                    PlayIndex(startIndex);
                return;
            }
            Play();
        };
        btnPause.Click += (_, _) => Pause();
        btnStop.Click += (_, _) => Stop();
        btnNext.Click += (_, _) => PlayNext();
        btnPrev.Click += (_, _) => PlayPrev();

        seekSlider.PreviewMouseDown += (_, _) =>
        {
            if (isOpen && lengthMs > 0)
                seekDragging = true;
        };
        seekSlider.PreviewMouseUp += (_, _) =>
        {
            if (!seekDragging)
                return;
            seekDragging = false;
            PerformSeek((long)seekSlider.Value);
        };
        seekSlider.LostMouseCapture += (_, _) =>
        {
            if (!seekDragging)
                return;
            seekDragging = false;
            PerformSeek((long)seekSlider.Value);
        };
        seekSlider.ValueChanged += (_, _) =>
        {
            if (seekDragging)
                lblPosition.Text = FormatTime((long)seekSlider.Value);
        };

        dlg.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is TextBox)
                return;
            switch (e.Key)
            {
                case Key.Space:
                    if (isOpen)
                    {
                        if (isPlaying) Pause(); else Play();
                        e.Handled = true;
                    }
                    else if (playlist.Count > 0)
                    {
                        var startIndex = ResolveStartPlayIndex();
                        if (startIndex >= 0)
                            PlayIndex(startIndex);
                        e.Handled = true;
                    }
                    break;
                case Key.Right:
                    if (playlist.Count > 0)
                    {
                        PlayNext();
                        e.Handled = true;
                    }
                    break;
                case Key.Left:
                    if (playlist.Count > 0)
                    {
                        PlayPrev();
                        e.Handled = true;
                    }
                    break;
            }
        };

        dlg.Closed += (_, _) => CloseDevice();
        dlg.ContentRendered += (_, _) =>
        {
            if (hasDialogShown)
                return;
            hasDialogShown = true;
            PrimeInitialPreload();
        };

        RebuildPlaylistUi();
        UpdateButtons();
        if (playlist.Count == 0)
            SetStatus("No tracks loaded - click + to add songs.");
        else
            SetStatus($"{playlist.Count} tracks ready.");

        SyncPlaylistScrollBarFromViewer();
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
