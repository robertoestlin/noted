using System;
using System.Collections.Generic;
using System.Globalization;
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
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MidiPlayerMciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool MidiPlayerMciGetErrorString(int errorCode, StringBuilder buffer, int bufferLength);

    // Master volume of the MIDI Mapper (device id 0). Directly scales the MIDI
    // synth output so it works for MCI sequencer playback without requiring us
    // to find any WASAPI session. Low word = left channel, high word = right.
    [DllImport("winmm.dll", EntryPoint = "midiOutSetVolume")]
    private static extern uint MidiPlayerMidiOutSetVolume(uint deviceId, uint dwVolume);

    [DllImport("winmm.dll", EntryPoint = "midiOutGetVolume")]
    private static extern uint MidiPlayerMidiOutGetVolume(uint deviceId, out uint pdwVolume);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

    private const uint SendInputKeyboard = 1;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkMediaPlayPause = 0xB3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, MidiSendInputRecord[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiMouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiKeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHardwareInput
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MidiInputUnion
    {
        [FieldOffset(0)] public MidiMouseInput mi;
        [FieldOffset(0)] public MidiKeybdInput ki;
        [FieldOffset(0)] public MidiHardwareInput hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiSendInputRecord
    {
        public uint type;
        public MidiInputUnion U;
    }

    /// <summary>
    /// Sends the system media Play/Pause key. Does not open Noted's MIDI player window.
    /// </summary>
    private static void TrySendGlobalMediaPlayPause()
    {
        try
        {
            var inputs = new MidiSendInputRecord[2];
            inputs[0].type = SendInputKeyboard;
            inputs[0].U.ki = new MidiKeybdInput
            {
                wVk = VkMediaPlayPause,
                wScan = 0,
                dwFlags = KeyeventfExtendedkey,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            inputs[1].type = SendInputKeyboard;
            inputs[1].U.ki = new MidiKeybdInput
            {
                wVk = VkMediaPlayPause,
                wScan = 0,
                dwFlags = KeyeventfExtendedkey | KeyeventfKeyup,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            SendInput(2, inputs, Marshal.SizeOf<MidiSendInputRecord>());
        }
        catch
        {
            /* best effort */
        }
    }

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
    private const string MidiPlaylistsFileName = "midi-playlists.json";
    private const string MidiPlaylistIdAll = "__all__";
    private const string MidiPlaylistIdClassical = "__classical__";
    private const string MidiPlaylistIdFocus = "__focus__";
    /// <summary>Bump when shipped defaults change (e.g. Brahms in Classical, Focus playlist).</summary>
    private const int MidiPlaylistStoreRevisionCurrent = 2;
    private const string MidiCustomGroupName = "Custom";
    private const string MidiOtherGroupName = "Other";

    private sealed class MidiPlaylistsFileDto
    {
        public string? SelectedPlaylistId { get; set; }
        public int? StoreRevision { get; set; }
        public List<string>? ClassicalPaths { get; set; }
        public List<string>? FocusPaths { get; set; }
        public List<MidiUserPlaylistDto>? UserPlaylists { get; set; }
    }

    private sealed class MidiUserPlaylistDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Paths { get; set; }
    }

    private Window? _midiPlayerWindow;
    private bool _midiPlayerDocked;
    private Action? _midiPlayerDockAction;
    private Action? _midiPlayerRestoreAction;
    private Action? _midiPlayerNextAction;
    private Action? _midiPlayerPauseToggleAction;
    private bool _midiPlayerIsPlaying;
    private string? _midiPlayerCurrentTitle;
    private string? _midiPlayerCurrentGroup;
    private string? _midiPlayerCurrentPlaylistName;

    private void UpdateMidiPlayerDockedIndicator()
    {
        if (MidiPlayerDockedIndicator == null)
            return;
        MidiPlayerDockedIndicator.Visibility = _midiPlayerDocked
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Opens the MIDI player when not running yet, or brings it back to the
    /// foreground when it has been docked. Used by the menu and as the
    /// "open/restore" half of the toggle.
    /// </summary>
    private void OpenOrRestoreMidiPlayer()
    {
        if (_midiPlayerWindow == null)
        {
            ShowMidiPlayerDialog();
            return;
        }

        _midiPlayerRestoreAction?.Invoke();
    }

    /// <summary>
    /// Toggles between visible and hidden. Opens the player if it is not
    /// already running. Hooked up to the configurable MIDI shortcut (default
    /// Ctrl+M). The "docked" indicator next to the version tag is only
    /// shown while playback is active; minimizing while idle simply hides
    /// the window.
    /// </summary>
    private void ToggleMidiPlayer()
    {
        if (_midiPlayerWindow == null)
        {
            ShowMidiPlayerDialog();
            return;
        }

        if (_midiPlayerWindow.IsVisible)
            _midiPlayerDockAction?.Invoke();
        else
            _midiPlayerRestoreAction?.Invoke();
    }

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
            WindowSettingsStore.WriteUtf8IfChanged(MidiCustomSongsPath(), json);
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

    private string MidiPlaylistsStorePath() => Path.Combine(_backupFolder, MidiPlaylistsFileName);

    private static bool IsClassicalComposerGroup(string group) =>
        group.Equals("Bach", StringComparison.OrdinalIgnoreCase)
        || group.Equals("Mozart", StringComparison.OrdinalIgnoreCase)
        || group.Equals("Albeniz", StringComparison.OrdinalIgnoreCase)
        || group.Equals("Brahms", StringComparison.OrdinalIgnoreCase);

    private static List<string> DefaultFocusPathsFromBundled(List<MidiSong> bundled)
        => bundled
            .Where(s => s.Group.Equals("Focus", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> DefaultClassicalPathsFromBundled(List<MidiSong> bundled)
        => bundled
            .Where(s => IsClassicalComposerGroup(s.Group))
            .Select(s => s.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<MidiSong> BuildBundledMidiSongsOnly()
    {
        var songs = new List<MidiSong>();
        var resourcesDir = MidiResourcesDirectory();
        if (!Directory.Exists(resourcesDir))
            return songs;
        foreach (var path in Directory
                     .EnumerateFiles(resourcesDir, "*.mid", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var (group, title) = SplitMidiTitle(name);
            songs.Add(new MidiSong { Title = title, Path = path, Group = group, IsCustom = false });
        }

        return songs;
    }

    private MidiSong CreateMidiSongFromResolvedPath(string path, HashSet<string> customPathSet)
    {
        if (customPathSet.Contains(path))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var (_, title) = SplitMidiTitle(name);
            return new MidiSong { Title = title, Path = path, Group = MidiCustomGroupName, IsCustom = true };
        }

        try
        {
            var resourcesDir = Path.GetFullPath(MidiResourcesDirectory());
            var full = Path.GetFullPath(path);
            var prefix = resourcesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, resourcesDir, StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var (group, title) = SplitMidiTitle(name);
                return new MidiSong { Title = title, Path = path, Group = group, IsCustom = false };
            }
        }
        catch
        {
            // Fall through to filename-only parsing.
        }

        var fn = Path.GetFileNameWithoutExtension(path);
        var (g, t) = SplitMidiTitle(fn);
        return new MidiSong { Title = t, Path = path, Group = g, IsCustom = false };
    }

    private List<MidiSong> BuildActiveMidiPlaylist(
        string playlistId,
        List<string> customPaths,
        MidiPlaylistsFileDto store,
        List<MidiSong> bundledOnlyCache)
    {
        var customSet = new HashSet<string>(customPaths, StringComparer.OrdinalIgnoreCase);
        switch (playlistId)
        {
            case MidiPlaylistIdAll:
                return bundledOnlyCache.ToList();
            case MidiPlaylistIdClassical:
                var classical = store.ClassicalPaths ?? new List<string>();
                return classical
                    .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    .Select(p => CreateMidiSongFromResolvedPath(p, customSet))
                    .ToList();
            case MidiPlaylistIdFocus:
                var focus = store.FocusPaths ?? new List<string>();
                return focus
                    .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    .Select(p => CreateMidiSongFromResolvedPath(p, customSet))
                    .ToList();
            default:
                var user = store.UserPlaylists?.FirstOrDefault(u => u.Id == playlistId);
                if (user?.Paths == null)
                    return new List<MidiSong>();
                return user.Paths
                    .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    .Select(p => CreateMidiSongFromResolvedPath(p, customSet))
                    .ToList();
        }
    }

    private static bool IsKnownMidiPlaylistId(string id, MidiPlaylistsFileDto store)
    {
        if (id == MidiPlaylistIdAll || id == MidiPlaylistIdClassical || id == MidiPlaylistIdFocus)
            return true;
        return store.UserPlaylists?.Any(u => u.Id == id) == true;
    }

    private MidiPlaylistsFileDto LoadMidiPlaylistsStoreOrCreate(List<MidiSong> bundledForDefaults)
    {
        var path = MidiPlaylistsStorePath();
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize<MidiPlaylistsFileDto>(json);
                if (dto != null)
                {
                    dto.UserPlaylists ??= new List<MidiUserPlaylistDto>();
                    foreach (var u in dto.UserPlaylists)
                        u.Paths ??= new List<string>();
                    if (dto.ClassicalPaths == null || dto.ClassicalPaths.Count == 0)
                        dto.ClassicalPaths = DefaultClassicalPathsFromBundled(bundledForDefaults);
                    ApplyMidiPlaylistStoreMigrations(dto, bundledForDefaults);
                    return dto;
                }
            }
        }
        catch
        {
            // Fall through to defaults.
        }

        var created = new MidiPlaylistsFileDto
        {
            SelectedPlaylistId = MidiPlaylistIdAll,
            StoreRevision = MidiPlaylistStoreRevisionCurrent,
            ClassicalPaths = DefaultClassicalPathsFromBundled(bundledForDefaults),
            FocusPaths = DefaultFocusPathsFromBundled(bundledForDefaults),
            UserPlaylists = new List<MidiUserPlaylistDto>()
        };
        SaveMidiPlaylistsStore(created);
        return created;
    }

    /// <summary>
    /// One-time updates for existing midi-playlists.json (e.g. seed Brahms into Classical, add Focus defaults).
    /// </summary>
    private void ApplyMidiPlaylistStoreMigrations(MidiPlaylistsFileDto dto, List<MidiSong> bundledForDefaults)
    {
        var rev = dto.StoreRevision ?? 0;
        if (rev >= MidiPlaylistStoreRevisionCurrent)
            return;

        dto.ClassicalPaths ??= new List<string>();
        var classicalSet = new HashSet<string>(dto.ClassicalPaths, StringComparer.OrdinalIgnoreCase);
        var brahmsMerged = false;
        foreach (var p in bundledForDefaults
                     .Where(s => s.Group.Equals("Brahms", StringComparison.OrdinalIgnoreCase))
                     .Select(s => s.Path))
        {
            if (classicalSet.Add(p))
            {
                dto.ClassicalPaths.Add(p);
                brahmsMerged = true;
            }
        }

        if (brahmsMerged)
        {
            dto.ClassicalPaths = dto.ClassicalPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (dto.FocusPaths == null || dto.FocusPaths.Count == 0)
            dto.FocusPaths = DefaultFocusPathsFromBundled(bundledForDefaults);

        dto.StoreRevision = MidiPlaylistStoreRevisionCurrent;
        SaveMidiPlaylistsStore(dto);
    }

    private void SaveMidiPlaylistsStore(MidiPlaylistsFileDto dto)
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            WindowSettingsStore.WriteUtf8IfChanged(MidiPlaylistsStorePath(), json);
        }
        catch
        {
            // Non-critical persistence.
        }
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

    private static string GetMidiMciPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        // MCI sequencer can report "file not found" for longer absolute paths.
        // Prefer the filesystem short path (8.3) when available. Some locked
        // down environments may restrict unmanaged calls, so this is best-effort
        // only and should never block playback.
        try
        {
            var shortPath = new StringBuilder(512);
            var written = GetShortPathName(path, shortPath, (uint)shortPath.Capacity);
            if (written > 0 && written < shortPath.Capacity)
                return shortPath.ToString();
        }
        catch
        {
            // Ignore and continue to managed-only fallbacks below.
        }

        // If short names are unavailable (e.g. 8.3 disabled), use a relative
        // path for files under the app base to keep the command short.
        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                var relative = Path.GetRelativePath(baseDir, path);
                if (!relative.StartsWith("..", StringComparison.Ordinal)
                    && !Path.IsPathRooted(relative))
                    return relative;
            }
        }
        catch
        {
            // Keep original path fallback below.
        }
        return path;
    }

    /// <summary>
    /// Dark chrome for playlist context menus to match MIDI player panels (#121D31 / #2D3F63).
    /// </summary>
    private static ResourceDictionary MidiPlayerContextMenuResources()
    {
        const string xaml =
"""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="MidiMenuHoverBrush" Color="#77294A7A"/>

  <Style TargetType="Separator">
    <Setter Property="Background" Value="#2D3F63"/>
    <Setter Property="Margin" Value="10,6"/>
    <Setter Property="Height" Value="1"/>
  </Style>

  <Style TargetType="ContextMenu">
    <Setter Property="Background" Value="#121D31"/>
    <Setter Property="BorderBrush" Value="#2D3F63"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontFamily" Value="Segoe UI, Segoe UI Variable"/>
    <Setter Property="FontSize" Value="14"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Border Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  CornerRadius="8"
                  Padding="4">
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="MenuItem">
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="12,9,14,9"/>
    <Setter Property="FontFamily" Value="Segoe UI, Segoe UI Variable"/>
    <Setter Property="FontSize" Value="14"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Border x:Name="templateRoot"
                  Background="{TemplateBinding Background}"
                  CornerRadius="4"
                  Padding="{TemplateBinding Padding}"
                  SnapsToDevicePixels="True">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
              </Grid.ColumnDefinitions>
              <ContentPresenter x:Name="headerPresenter"
                                Grid.Column="0"
                                ContentSource="Header"
                                RecognizesAccessKey="True"
                                VerticalAlignment="Center"
                                SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}"/>
              <Path x:Name="arrow"
                    Grid.Column="1"
                    Visibility="Collapsed"
                    VerticalAlignment="Center"
                    Margin="10,0,4,0"
                    Fill="White"
                    Data="M 0 0 L 4 3.5 L 0 7 Z"/>
              <Popup x:Name="PART_Popup"
                     AllowsTransparency="True"
                     Focusable="False"
                     Placement="Right"
                     HorizontalOffset="-4"
                     VerticalOffset="-4"
                     IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}"
                     PopupAnimation="Fade"
                     PlacementTarget="{Binding ElementName=templateRoot}">
                <Border Background="#121D31"
                        BorderBrush="#2D3F63"
                        BorderThickness="1"
                        CornerRadius="8"
                        Padding="4">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="templateRoot" Property="Background" Value="{StaticResource MidiMenuHoverBrush}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Foreground" Value="#6688AA"/>
            </Trigger>
            <Trigger Property="HasItems" Value="True">
              <Setter TargetName="arrow" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsSuspendingPopupAnimation" Value="True">
              <Setter TargetName="PART_Popup" Property="PopupAnimation" Value="None"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
""";
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private void ShowMidiPlayerDialog(bool startDockedHidden = false)
    {
        // Guard against double-open if invoked while a player is already alive.
        if (_midiPlayerWindow != null)
        {
            _midiPlayerRestoreAction?.Invoke();
            return;
        }

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
            ShowInTaskbar = true,
            Owner = this,
            ShowActivated = !startDockedHidden
        };

        var alias = "noted_midi_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var warmupAlias = "noted_midi_warm_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const long preloadLeadMs = 10_000;
        var isOpen = false;
        var isPlaying = false;
        var isPaused = false;
        var seekDragging = false;
        var hasDialogShown = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        long lengthMs = 0;
        long positionMs = 0;
        long pausedPositionMs = 0;
        int? preloadedNextIndex = null;
        string? preloadedNextPath = null;
        var preloadedNextDeviceOpen = false;
        int? preloadedStartIndex = null;
        string? preloadedStartPath = null;
        var startTrackPrimedSilently = false;

        var customPaths = LoadCustomMidiPaths();
        var bundledOnlyCache = BuildBundledMidiSongsOnly();
        var midiPlaylistStore = LoadMidiPlaylistsStoreOrCreate(bundledOnlyCache);
        var currentPlaylistId = midiPlaylistStore.SelectedPlaylistId ?? MidiPlaylistIdAll;
        if (!IsKnownMidiPlaylistId(currentPlaylistId, midiPlaylistStore))
            currentPlaylistId = MidiPlaylistIdAll;
        midiPlaylistStore.SelectedPlaylistId = currentPlaylistId;
        var playlist = BuildActiveMidiPlaylist(currentPlaylistId, customPaths, midiPlaylistStore, bundledOnlyCache);
        var playbackPlaylistId = currentPlaylistId;
        var playbackQueue = BuildActiveMidiPlaylist(playbackPlaylistId, customPaths, midiPlaylistStore, bundledOnlyCache);
        var currentIndex = -1;
        string? currentLoadedPath = null;
        var rng = new Random();
        var shuffleHistory = new List<int>();
        string? pendingPlayAfterCurrentPath = null;
        var playingQueuedInterstitial = false;
        var resumeIndexAfterQueuedTrack = -1;
        int? pendingContinuePlayHereViewIndex = null;

        void ClearPendingQueueInsert()
        {
            pendingPlayAfterCurrentPath = null;
            playingQueuedInterstitial = false;
            resumeIndexAfterQueuedTrack = -1;
        }

        void ClearPendingContinuePlayHere()
        {
            pendingContinuePlayHereViewIndex = null;
        }

        void ClearAllDeferredPlaybackIntent()
        {
            ClearPendingQueueInsert();
            ClearPendingContinuePlayHere();
        }

        void RebuildPlaybackQueueFromPlaybackId()
        {
            playbackQueue = BuildActiveMidiPlaylist(
                playbackPlaylistId,
                customPaths,
                midiPlaylistStore,
                bundledOnlyCache);
        }

        int ViewIndexOfPlayingTrack()
        {
            if (string.IsNullOrWhiteSpace(currentLoadedPath))
                return -1;
            return playlist.FindIndex(s =>
                string.Equals(s.Path, currentLoadedPath, StringComparison.OrdinalIgnoreCase));
        }

        string PlaylistDisplayName(string id)
        {
            return id switch
            {
                MidiPlaylistIdAll => "All",
                MidiPlaylistIdClassical => "Classical",
                MidiPlaylistIdFocus => "Focus",
                _ => midiPlaylistStore.UserPlaylists?.FirstOrDefault(u => u.Id == id)?.Name ?? "Playlist"
            };
        }

        var itemBordersByIndex = new Dictionary<int, Border>();
        var groupHeadersByName = new Dictionary<string, Border>(StringComparer.Ordinal);
        Border? currentHighlightedItem = null;
        var rowBaseBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x17, 0x24, 0x3B));
        var rowHighlightBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x9C, 0xFF));
        var rowSelectedFillBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0x4F, 0x9C, 0xFF));
        var rowHoverBrush = new SolidColorBrush(Color.FromArgb(0x77, 0x29, 0x4A, 0x7A));
        var preloadDotIdleBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x57, 0x70));
        var preloadDotReadyBrush = new SolidColorBrush(Color.FromRgb(0x63, 0xEA, 0xA0));

        dlg.Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0F, 0x1A));
        dlg.Resources.MergedDictionaries.Add(MidiPlayerContextMenuResources());
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
        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var lblHeaderViewPlaylist = new TextBlock
        {
            Text = PlaylistDisplayName(currentPlaylistId),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xD4, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            MaxWidth = 280,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = "Playlist shown in the list below"
        };
        headerButtons.Children.Add(lblHeaderViewPlaylist);
        var btnHeaderDock = new Button
        {
            Content = "\uE738",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 32,
            Height = 32,
            FontSize = 12,
            ToolTip = "Dock (keep playing in background)",
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0)
        };
        btnHeaderDock.Click += (_, _) => DockMidiPlayerWindow();
        headerButtons.Children.Add(btnHeaderDock);
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
        headerButtons.Children.Add(btnHeaderClose);
        Grid.SetColumn(headerButtons, 1);
        header.Children.Add(headerButtons);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Tags this process's render session(s) as "Noted" in Volume Mixer and
        // sets the MIDI Mapper master volume so MCI sequencer playback honours
        // _midiPlayerVolumePercent. midiOutSetVolume is what actually changes
        // the audible MIDI output level; the WASAPI session master is set in
        // parallel so the Volume Mixer "Noted" slider tracks the popup too.
        void NormalizeAudioSessionDisplayName()
        {
            _audioSessionSnapshotService.TrySetCurrentProcessSessionDisplayName("Noted");
            ApplyMidiOutVolume(_midiPlayerVolumePercent);
            var linear = Math.Clamp(_midiPlayerVolumePercent, 0, 100) / 100f;
            _audioSessionSnapshotService.TrySetCurrentProcessSessionsMasterVolume(linear);
        }

        static void ApplyMidiOutVolume(int percent)
        {
            var clamped = Math.Clamp(percent, 0, 100);
            // 0..100 % → 0..0xFFFF per channel, packed as right<<16 | left.
            var perChannel = (uint)Math.Round(clamped * 65535.0 / 100.0);
            var packed = (perChannel << 16) | perChannel;
            MidiPlayerMidiOutSetVolume(0, packed);
        }

        static int? ReadMidiOutVolumePercent()
        {
            if (MidiPlayerMidiOutGetVolume(0, out var packed) != 0)
                return null;
            var leftLevel = packed & 0xFFFFu;
            var rightLevel = (packed >> 16) & 0xFFFFu;
            var level = Math.Max(leftLevel, rightLevel);
            return (int)Math.Round(level * 100.0 / 65535.0);
        }

        // The "docked" indicator beside the version tag is only meaningful
        // while music is actually playing - hiding the window while idle
        // simply tucks it away without flagging it as docked.
        void RefreshDockedIndicator()
        {
            _midiPlayerDocked = !dlg.IsVisible && isPlaying;
            UpdateMidiPlayerDockedIndicator();
        }

        void DockMidiPlayerWindow()
        {
            dlg.Hide();
            dlg.WindowState = WindowState.Normal;
            RefreshDockedIndicator();
            NormalizeAudioSessionDisplayName();
            // Hide() does not reliably return keyboard focus to the owner; bring
            // Noted forward after dock (Ctrl+M, header dock, or system minimize).
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                Activate();
                var doc = CurrentDoc();
                if (doc != null)
                {
                    doc.Editor.Focus();
                    Keyboard.Focus(doc.Editor);
                }
            });
        }

        void RestoreMidiPlayerWindow()
        {
            if (!dlg.IsVisible)
                dlg.Show();
            if (dlg.WindowState == WindowState.Minimized)
                dlg.WindowState = WindowState.Normal;
            dlg.Activate();
            dlg.Focus();
            RefreshDockedIndicator();
            NormalizeAudioSessionDisplayName();
        }

        dlg.StateChanged += (_, _) =>
        {
            if (dlg.WindowState != WindowState.Minimized)
                return;
            DockMidiPlayerWindow();
        };

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

        var lblNowPlayingPlaylist = new TextBlock
        {
            Text = PlaylistDisplayName(playbackPlaylistId),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xE0, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0)
        };
        nowPlayingStack.Children.Add(lblNowPlayingPlaylist);
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
            ToolTip = "Add MIDI file(s)"
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

        // Same look as the playlist scrollbar, but Value increases upward (quiet
        // at bottom, loud at top) for use as a vertical volume control. Thumb is
        // larger than the playlist bar for easier dragging.
        var volumePopupBarStyle = (Style)XamlReader.Parse(
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="ScrollBar">
              <Setter Property="Width" Value="16"/>
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
                      <Track x:Name="PART_Track" IsDirectionReversed="False">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0"/>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb MinHeight="36" MinWidth="14">
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border x:Name="ThumbBorder"
                                        Background="#4A6A9D"
                                        CornerRadius="5"
                                        MinHeight="32"
                                        MinWidth="12"/>
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
        void RefreshPreloadAfterShuffleToggle()
        {
            // Run preload on the dispatcher after the clear-state repaint, so indicator
            // is visibly reset first and only turns ready once preload completes.
            dlg.Dispatcher.BeginInvoke(
                () =>
                {
                    if (isPlaying)
                        TryPreloadUpcomingTrack(force: true);
                    else
                        PrimeInitialPreload();
                },
                DispatcherPriority.Background);
        }
        var preloadDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = preloadDotIdleBrush,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Preload pending"
        };
        var volumeBarPopup = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Style = volumePopupBarStyle,
            Width = 16,
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(_midiPlayerVolumePercent, 0, 100),
            SmallChange = 2,
            LargeChange = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var btnVolume = new Button
        {
            Content = "\uE767",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 36,
            Height = 36,
            FontSize = 16,
            Style = iconButtonStyle,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Volume: {_midiPlayerVolumePercent}%"
        };
        var volumePopupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            SnapsToDevicePixels = true,
            Child = volumeBarPopup
        };
        var volumePopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Top,
            PopupAnimation = PopupAnimation.Fade,
            Child = volumePopupBorder
        };

        Action? rebuildPlaylistPickerUi = null;
        var playlistPickerList = new StackPanel();
        var playlistPickerScroll = new ScrollViewer
        {
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = playlistPickerList
        };
        var btnPlaylistAdd = new Button
        {
            Content = "\uE710",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0xEA, 0xA0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0x6A)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "New playlist"
        };
        var playlistPopupInner = new StackPanel();
        playlistPopupInner.Children.Add(playlistPickerScroll);
        playlistPopupInner.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = btnPlaylistAdd
        });
        var playlistPopupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            MinWidth = 220,
            MaxWidth = 360,
            SnapsToDevicePixels = true,
            Child = playlistPopupInner
        };
        // Popup content inherits from PlacementTarget (btnPlaylist uses Segoe MDL2 Assets).
        // Without a real text font, playlist names render as empty boxes.
        TextElement.SetFontFamily(playlistPopupBorder, new FontFamily("Segoe UI, Segoe UI Variable"));
        TextElement.SetFontSize(playlistPopupBorder, 14);
        var playlistPopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Top,
            // Instant dismiss when picking a playlist (fade kept click handler blocked ~200ms).
            PopupAnimation = PopupAnimation.None,
            Child = playlistPopupBorder
        };
        var btnPlaylist = new Button
        {
            Content = "\uE762",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 36,
            Height = 36,
            FontSize = 16,
            Style = iconButtonStyle,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Playlists"
        };
        playlistPopup.PlacementTarget = btnPlaylist;
        btnPlaylist.Click += (_, _) =>
        {
            volumePopup.IsOpen = false;
            playlistPopup.IsOpen = !playlistPopup.IsOpen;
            rebuildPlaylistPickerUi?.Invoke();
        };
        playlistPopup.Opened += (_, _) =>
        {
            rebuildPlaylistPickerUi?.Invoke();
            dlg.Dispatcher.BeginInvoke(
                () =>
                {
                    playlistPopup.HorizontalOffset =
                        -(playlistPopupBorder.ActualWidth > 1
                            ? playlistPopupBorder.ActualWidth
                            : 220) / 2
                        + btnPlaylist.ActualWidth / 2;
                },
                DispatcherPriority.Loaded);
        };

        void SyncMidiPlayerVolumeFromPopupUi()
        {
            var pct = (int)Math.Round(volumeBarPopup.Value);
            pct = Math.Clamp(pct, 0, 100);
            _midiPlayerVolumePercent = pct;
            btnVolume.ToolTip = $"Volume: {pct}%";
            NormalizeAudioSessionDisplayName();
        }

        // The MIDI Mapper master volume can be changed by other apps (or by the
        // user via Volume Mixer on some Windows versions). Re-read it here so
        // the popup reflects the actual playback level rather than stale JSON.
        void SyncMidiPlayerVolumeFromWindowsMixer()
        {
            _audioSessionSnapshotService.TrySetCurrentProcessSessionDisplayName("Noted");
            var current = ReadMidiOutVolumePercent();
            if (current is not int pct)
                return;
            pct = Math.Clamp(pct, 0, 100);
            _midiPlayerVolumePercent = pct;
            volumeBarPopup.Value = pct;
            btnVolume.ToolTip = $"Volume: {pct}%";
        }

        // Thumb drag captures the mouse on the thumb, not the ScrollBar. Tunnel
        // PreviewMouseUp on the border still runs on release so we always apply the
        // final level (in addition to ValueChanged while the thumb moves).
        volumePopupBorder.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!volumePopup.IsOpen)
                return;
            SyncMidiPlayerVolumeFromPopupUi();
        };

        volumeBarPopup.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject src)
                return;
            for (var n = src; n != null; n = VisualTreeHelper.GetParent(n))
            {
                if (n is Thumb)
                    return;
                if (ReferenceEquals(n, volumeBarPopup))
                    break;
            }

            var y = e.GetPosition(volumeBarPopup).Y;
            var h = volumeBarPopup.ActualHeight;
            if (h < 1)
                return;
            var ratio = (h - y) / h;
            var v = volumeBarPopup.Minimum + ratio * (volumeBarPopup.Maximum - volumeBarPopup.Minimum);
            volumeBarPopup.Value = Math.Clamp(v, volumeBarPopup.Minimum, volumeBarPopup.Maximum);
            SyncMidiPlayerVolumeFromPopupUi();
            e.Handled = true;
        };

        void SyncVolumePopupBarLength()
        {
            var listH = listScroll.ActualHeight;
            if (listH <= 1 && queueCard.ActualHeight > 1)
                listH = Math.Max(1, queueCard.ActualHeight - 80);
            var h = Math.Max(100, listH / 2.0);
            volumeBarPopup.Height = h;
        }
        var preloadVolumeHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        preloadVolumeHost.Children.Add(btnVolume);
        preloadVolumeHost.Children.Add(btnPlaylist);
        preloadVolumeHost.Children.Add(preloadDot);
        btnShuffle.Checked += (_, _) =>
        {
            ClearNextPreload();
            ClearStartPreload();
            RefreshPreloadAfterShuffleToggle();
        };
        btnShuffle.Unchecked += (_, _) =>
        {
            ClearNextPreload();
            ClearStartPreload();
            RefreshPreloadAfterShuffleToggle();
        };
        btnLoop.Checked += (_, _) => ClearNextPreload();
        btnLoop.Unchecked += (_, _) => ClearNextPreload();
        modeRow.Children.Add(btnShuffle);
        modeRow.Children.Add(btnLoop);
        modeRow.Children.Add(preloadVolumeHost);
        volumePopup.PlacementTarget = btnVolume;
        btnVolume.Click += (_, _) =>
        {
            playlistPopup.IsOpen = false;
            volumePopup.IsOpen = !volumePopup.IsOpen;
            if (volumePopup.IsOpen)
            {
                SyncMidiPlayerVolumeFromWindowsMixer();
                dlg.Dispatcher.BeginInvoke(
                    () =>
                    {
                        SyncVolumePopupBarLength();
                        volumePopup.HorizontalOffset =
                            -(volumePopupBorder.ActualWidth > 1
                                ? volumePopupBorder.ActualWidth
                                : 32) / 2
                            + btnVolume.ActualWidth / 2;
                    },
                    DispatcherPriority.Loaded);
            }
        };
        volumePopup.Opened += (_, _) =>
        {
            playlistPopup.IsOpen = false;
            SyncMidiPlayerVolumeFromWindowsMixer();
            SyncVolumePopupBarLength();
            volumePopup.HorizontalOffset =
                -(volumePopupBorder.ActualWidth > 1 ? volumePopupBorder.ActualWidth : 32) / 2
                + btnVolume.ActualWidth / 2;
        };
        listScroll.SizeChanged += (_, _) =>
        {
            if (volumePopup.IsOpen)
                SyncVolumePopupBarLength();
        };
        queueCard.SizeChanged += (_, _) =>
        {
            if (volumePopup.IsOpen)
                SyncVolumePopupBarLength();
        };
        volumeBarPopup.ValueChanged += (_, _) => SyncMidiPlayerVolumeFromPopupUi();
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
            var t = text.TrimEnd();
            t = t.TrimEnd('.');
            lblStatus.Text = t;
            lblStatus.Foreground = color ?? new SolidColorBrush(Color.FromRgb(0xC1, 0xCF, 0xEA));
        }

        static bool SongMatchesQuery(MidiSong song, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            var tokenSource = string.Create(
                CultureInfo.InvariantCulture,
                $"{song.Title} {song.Group} {Path.GetFileNameWithoutExtension(song.Path)}");
            var normalizedSource = tokenSource.ToLowerInvariant();
            var words = normalizedSource.Split(
                [' ', '-', '_', '.', '/', '\\', '(', ')', '[', ']', '{', '}', ',', ';', ':'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tokens = query.ToLowerInvariant().Split(
                [' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var token in tokens)
            {
                if (normalizedSource.Contains(token, StringComparison.Ordinal))
                    continue;
                if (words.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
                    continue;
                return false;
            }

            return true;
        }

        void UpdatePreloadIndicator()
        {
            var hasPreload =
                startTrackPrimedSilently
                || !string.IsNullOrWhiteSpace(preloadedStartPath)
                || (preloadedNextDeviceOpen && !string.IsNullOrWhiteSpace(preloadedNextPath));
            preloadDot.Background = hasPreload ? preloadDotReadyBrush : preloadDotIdleBrush;
            preloadDot.ToolTip = hasPreload ? "Preload ready" : "Preload pending";
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
            btnPlay.IsEnabled = playbackQueue.Count > 0 && !isPlaying;
            btnPause.IsEnabled = isOpen && isPlaying && !isPaused;
            btnStop.IsEnabled = isOpen && (isPlaying || isPaused);
            seekSlider.IsEnabled = isOpen && lengthMs > 0;
            btnPrev.IsEnabled = playbackQueue.Count > 0;
            btnNext.IsEnabled = playbackQueue.Count > 0;
            RefreshDockedIndicator();
            _midiPlayerIsPlaying = isPlaying;
            _midiPlayerCurrentTitle = isPlaying ? lblNowPlaying.Text : null;
            _midiPlayerCurrentGroup = isPlaying ? lblNowPlayingMeta.Text : null;
            var playingPlName = PlaylistDisplayName(playbackPlaylistId);
            lblNowPlayingPlaylist.Text = playingPlName;
            _midiPlayerCurrentPlaylistName = playingPlName;
            RefreshMessageOverlayNowPlaying();
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
                var sizeKb = info.FileSizeBytes / 1024.0;
                var sizeKbText = sizeKb.ToString("0.00", CultureInfo.InvariantCulture);
                sb.Append($"Size: {info.FileSizeBytes:N0} bytes ({sizeKbText} KB)");
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
            ClearStartPreload();
            ClearNextPreload(closeDevice: true);
        }

        void CenterItemInPlaylist(Border item)
        {
            // Defer until layout is ready so ActualHeight / ViewportHeight /
            // TransformToAncestor return meaningful values. Falls back to
            // BringIntoView if anything goes wrong (e.g. no scrollable area).
            void DoCenter()
            {
                try
                {
                    if (!item.IsLoaded
                        || item.ActualHeight <= 0
                        || listScroll.ViewportHeight <= 0)
                    {
                        listScroll.Dispatcher.BeginInvoke(
                            new Action(DoCenter),
                            DispatcherPriority.Loaded);
                        return;
                    }

                    var viewport = listScroll.ViewportHeight;
                    var maxOffset = listScroll.ScrollableHeight;

                    var itemTop = item
                        .TransformToAncestor(listStack)
                        .Transform(new Point(0, 0)).Y;
                    var itemBottom = itemTop + item.ActualHeight;

                    // Prefer pinning the group header at the top whenever the
                    // current song is close enough to the header to remain
                    // fully visible in the viewport. For songs near the top of
                    // a group this keeps the genre/instrument label on screen;
                    // for songs further down (where pinning the header would
                    // push the song off-screen) we fall through to centering.
                    var pinViewIdx = ViewIndexOfPlayingTrack();
                    if (pinViewIdx >= 0
                        && pinViewIdx < playlist.Count
                        && groupHeadersByName.TryGetValue(playlist[pinViewIdx].Group, out var headerBorder)
                        && headerBorder.IsLoaded
                        && headerBorder.ActualHeight > 0)
                    {
                        var headerTop = headerBorder
                            .TransformToAncestor(listStack)
                            .Transform(new Point(0, 0)).Y;
                        if (itemBottom - headerTop <= viewport)
                        {
                            var headerTarget = Math.Max(0, Math.Min(headerTop, maxOffset));
                            listScroll.ScrollToVerticalOffset(headerTarget);
                            return;
                        }
                    }

                    var target = itemTop - (viewport - item.ActualHeight) / 2.0;
                    target = Math.Max(0, Math.Min(target, maxOffset));
                    listScroll.ScrollToVerticalOffset(target);
                }
                catch
                {
                    item.BringIntoView();
                }
            }
            DoCenter();
        }

        void RemoveMidiPathFromStoredPlaylists(string path)
        {
            if (midiPlaylistStore.ClassicalPaths != null)
            {
                midiPlaylistStore.ClassicalPaths = midiPlaylistStore.ClassicalPaths
                    .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (midiPlaylistStore.FocusPaths != null)
            {
                midiPlaylistStore.FocusPaths = midiPlaylistStore.FocusPaths
                    .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            foreach (var u in midiPlaylistStore.UserPlaylists ?? new List<MidiUserPlaylistDto>())
            {
                if (u.Paths == null)
                    continue;
                u.Paths = u.Paths
                    .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        void ApplyPlaylistSelection(string newId)
        {
            if (newId == currentPlaylistId)
                return;

            currentPlaylistId = newId;
            midiPlaylistStore.SelectedPlaylistId = currentPlaylistId;
            ClearPendingContinuePlayHere();

            playlist = BuildActiveMidiPlaylist(currentPlaylistId, customPaths, midiPlaylistStore, bundledOnlyCache);
            lblHeaderViewPlaylist.Text = PlaylistDisplayName(currentPlaylistId);

            RebuildPlaylistUi();
            HighlightCurrent();
            UpdateButtons();

            rebuildPlaylistPickerUi?.Invoke();
            SetStatus(
                $"{PlaylistDisplayName(currentPlaylistId)} · {playlist.Count} track{(playlist.Count == 1 ? string.Empty : "s")}");

            UpdateAddButtonTooltip();

            // Persist, tear down warmup device, and restart preload after layout so the list repaints immediately.
            dlg.Dispatcher.BeginInvoke(
                () =>
                {
                    SaveMidiPlaylistsStore(midiPlaylistStore);
                    ClearStartPreload();
                    ClearNextPreload(closeDevice: true);
                    if (isPlaying)
                        TryPreloadUpcomingTrack(force: true);
                    else
                        PrimeInitialPreload();
                },
                DispatcherPriority.Loaded);
        }

        void UpdateAddButtonTooltip()
        {
            btnAdd.ToolTip = currentPlaylistId == MidiPlaylistIdAll
                ? "Add MIDI file(s) to your Custom library"
                : $"Add MIDI file(s) to {PlaylistDisplayName(currentPlaylistId)}";
        }

        void AfterPlaylistPathsMutated()
        {
            var rememberedPath = currentLoadedPath;
            playlist = BuildActiveMidiPlaylist(currentPlaylistId, customPaths, midiPlaylistStore, bundledOnlyCache);
            RebuildPlaybackQueueFromPlaybackId();
            shuffleHistory.Clear();
            ClearStartPreload();
            ClearNextPreload(closeDevice: true);

            if (rememberedPath != null
                && playbackQueue.Any(s => string.Equals(s.Path, rememberedPath, StringComparison.OrdinalIgnoreCase)))
            {
                currentIndex = playbackQueue.FindIndex(s =>
                    string.Equals(s.Path, rememberedPath, StringComparison.OrdinalIgnoreCase));
                RebuildPlaylistUi();
                HighlightCurrent();
                UpdateButtons();
                PrimeInitialPreload();
            }
            else
            {
                CloseDevice();
                RebuildPlaylistUi();
                ResetPlayerView();
            }
        }

        void AddSongToTargetPlaylist(string targetId, string songPath)
        {
            if (targetId == MidiPlaylistIdAll || string.IsNullOrWhiteSpace(songPath) || !File.Exists(songPath))
                return;

            if (targetId == MidiPlaylistIdClassical)
            {
                midiPlaylistStore.ClassicalPaths ??= new List<string>();
                if (midiPlaylistStore.ClassicalPaths.Any(p =>
                        string.Equals(p, songPath, StringComparison.OrdinalIgnoreCase)))
                {
                    SetStatus("Already in Classical.");
                    return;
                }

                midiPlaylistStore.ClassicalPaths.Add(songPath);
            }
            else if (targetId == MidiPlaylistIdFocus)
            {
                midiPlaylistStore.FocusPaths ??= new List<string>();
                if (midiPlaylistStore.FocusPaths.Any(p =>
                        string.Equals(p, songPath, StringComparison.OrdinalIgnoreCase)))
                {
                    SetStatus("Already in Focus.");
                    return;
                }

                midiPlaylistStore.FocusPaths.Add(songPath);
            }
            else
            {
                var user = midiPlaylistStore.UserPlaylists?.FirstOrDefault(u => u.Id == targetId);
                if (user == null)
                    return;
                user.Paths ??= new List<string>();
                if (user.Paths.Any(p => string.Equals(p, songPath, StringComparison.OrdinalIgnoreCase)))
                {
                    SetStatus($"Already in \"{user.Name}\".");
                    return;
                }

                user.Paths.Add(songPath);
            }

            SaveMidiPlaylistsStore(midiPlaylistStore);
            if (targetId == currentPlaylistId)
                AfterPlaylistPathsMutated();
            else
                SetStatus($"Added to {PlaylistDisplayName(targetId)}.");
        }

        void RemoveSongFromCurrentPlaylist(string songPath)
        {
            if (currentPlaylistId == MidiPlaylistIdAll || string.IsNullOrWhiteSpace(songPath))
                return;

            if (currentPlaylistId == MidiPlaylistIdClassical)
            {
                midiPlaylistStore.ClassicalPaths ??= new List<string>();
                midiPlaylistStore.ClassicalPaths = midiPlaylistStore.ClassicalPaths
                    .Where(p => !string.Equals(p, songPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (currentPlaylistId == MidiPlaylistIdFocus)
            {
                midiPlaylistStore.FocusPaths ??= new List<string>();
                midiPlaylistStore.FocusPaths = midiPlaylistStore.FocusPaths
                    .Where(p => !string.Equals(p, songPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                var user = midiPlaylistStore.UserPlaylists?.FirstOrDefault(u => u.Id == currentPlaylistId);
                if (user?.Paths == null)
                    return;
                user.Paths = user.Paths
                    .Where(p => !string.Equals(p, songPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            SaveMidiPlaylistsStore(midiPlaylistStore);
            AfterPlaylistPathsMutated();
        }

        string? PromptMidiPlayerText(string title, string label, string initialValue)
        {
            var prompt = new Window
            {
                Title = title,
                Width = 440,
                Height = 212,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x14, 0x24))
            };

            var shell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x0F, 0x17, 0x2A),
                    Color.FromRgb(0x0B, 0x14, 0x24),
                    90)
            };

            var root = new DockPanel();
            shell.Child = root;

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var okBtn = new Button
            {
                Content = "OK",
                MinWidth = 88,
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI, Segoe UI Variable"),
                FontSize = 14,
                Cursor = Cursors.Hand
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                MinWidth = 88,
                Padding = new Thickness(16, 8, 16, 8),
                IsCancel = true,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI, Segoe UI Variable"),
                FontSize = 14,
                Cursor = Cursors.Hand
            };
            buttonRow.Children.Add(okBtn);
            buttonRow.Children.Add(cancelBtn);
            DockPanel.SetDock(buttonRow, Dock.Bottom);
            root.Children.Add(buttonRow);

            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleBlock = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = new FontFamily("Segoe UI, Segoe UI Variable")
            };
            var titleDrag = new Border { Background = Brushes.Transparent, Child = titleBlock };
            titleDrag.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                    try { prompt.DragMove(); } catch { /* ignore */ }
            };
            Grid.SetColumn(titleDrag, 0);
            header.Children.Add(titleDrag);
            var btnPromptClose = new Button
            {
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Width = 32,
                Height = 32,
                FontSize = 12,
                Style = iconButtonStyle,
                ToolTip = "Close"
            };
            btnPromptClose.Click += (_, _) => prompt.Close();
            Grid.SetColumn(btnPromptClose, 1);
            header.Children.Add(btnPromptClose);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC1, 0xCF, 0xEA)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
                FontFamily = new FontFamily("Segoe UI, Segoe UI Variable"),
                TextWrapping = TextWrapping.Wrap
            };
            var txt = new TextBox
            {
                Text = initialValue,
                Padding = new Thickness(8, 6, 8, 6),
                FontFamily = new FontFamily("Segoe UI, Segoe UI Variable"),
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x59)),
                BorderThickness = new Thickness(1),
                CaretBrush = Brushes.White
            };

            var middle = new StackPanel();
            middle.Children.Add(lbl);
            middle.Children.Add(txt);
            root.Children.Add(middle);

            string? result = null;
            okBtn.Click += (_, _) =>
            {
                result = txt.Text ?? string.Empty;
                prompt.DialogResult = true;
            };

            prompt.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    prompt.Close();
                    e.Handled = true;
                }
            };

            prompt.Content = shell;
            prompt.Loaded += (_, _) =>
            {
                txt.Focus();
                Keyboard.Focus(txt);
                txt.SelectAll();
            };

            return prompt.ShowDialog() == true ? result : null;
        }

        void DuplicateViewPlaylistAsNewUserPlaylist()
        {
            if (playlist.Count == 0)
                return;

            var name = PromptMidiPlayerText("Duplicate playlist", "New playlist name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();

            var paths = playlist
                .Select(s => s.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            midiPlaylistStore.UserPlaylists ??= new List<MidiUserPlaylistDto>();
            midiPlaylistStore.UserPlaylists.Add(new MidiUserPlaylistDto
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Paths = paths
            });
            SaveMidiPlaylistsStore(midiPlaylistStore);
            rebuildPlaylistPickerUi?.Invoke();
            RebuildPlaylistUi();
            SetStatus($"Duplicated as \"{name}\" ({paths.Count} track{(paths.Count == 1 ? string.Empty : "s")}).");
        }

        void RebuildPlaylistPickerUiImpl()
        {
            playlistPickerList.Children.Clear();
            var selectedBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x4A, 0x78));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x12, 0x1D, 0x31));

            void AddPickerRow(string id, string label)
            {
                var bid = id;
                var row = new Button
                {
                    Content = label,
                    FontFamily = new FontFamily("Segoe UI, Segoe UI Variable"),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = string.Equals(bid, currentPlaylistId, StringComparison.OrdinalIgnoreCase)
                        ? selectedBg
                        : idleBg,
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x3F, 0x63)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                row.Click += (_, _) =>
                {
                    playlistPopup.IsOpen = false;
                    var id = bid;
                    // Let the popup actually collapse and paint before rebuilding the main playlist UI.
                    dlg.Dispatcher.BeginInvoke(
                        () => ApplyPlaylistSelection(id),
                        DispatcherPriority.ApplicationIdle);
                };
                playlistPickerList.Children.Add(row);
            }

            AddPickerRow(MidiPlaylistIdAll, "All");
            AddPickerRow(MidiPlaylistIdClassical, "Classical");
            AddPickerRow(MidiPlaylistIdFocus, "Focus");
            foreach (var u in (midiPlaylistStore.UserPlaylists ?? new List<MidiUserPlaylistDto>())
                         .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
                AddPickerRow(u.Id, u.Name);
        }

        rebuildPlaylistPickerUi = RebuildPlaylistPickerUiImpl;

        btnPlaylistAdd.Click += (_, _) =>
        {
            var name = PromptMidiPlayerText("New playlist", "Playlist name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            midiPlaylistStore.UserPlaylists ??= new List<MidiUserPlaylistDto>();
            midiPlaylistStore.UserPlaylists.Add(new MidiUserPlaylistDto
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Paths = new List<string>()
            });
            SaveMidiPlaylistsStore(midiPlaylistStore);
            rebuildPlaylistPickerUi?.Invoke();
            RebuildPlaylistUi();
            SetStatus($"Created playlist \"{name}\".");
        };

        void HighlightCurrent()
        {
            if (currentHighlightedItem is not null)
            {
                currentHighlightedItem.Background = rowBaseBrush;
                currentHighlightedItem.BorderBrush = Brushes.Transparent;
                currentHighlightedItem.BorderThickness = new Thickness(1);
            }
            currentHighlightedItem = null;

            var viewIdx = ViewIndexOfPlayingTrack();
            if (viewIdx >= 0 && itemBordersByIndex.TryGetValue(viewIdx, out var item))
            {
                item.Background = rowSelectedFillBrush;
                item.BorderBrush = rowHighlightBrush;
                item.BorderThickness = new Thickness(2);
                CenterItemInPlaylist(item);
                currentHighlightedItem = item;
            }
        }

        void ApplyNowPlayingLabelsForLoadedPath(string path)
        {
            var idx = playbackQueue.FindIndex(s =>
                string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var song = playbackQueue[idx];
                lblNowPlaying.Text = song.Title;
                lblNowPlayingMeta.Text = song.Group;
            }
            else
            {
                var (group, title) = SplitMidiTitle(Path.GetFileNameWithoutExtension(path));
                lblNowPlaying.Text = title;
                lblNowPlayingMeta.Text = group;
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

            var openPath = GetMidiMciPath(path);
            var openCommand = $"open \"{openPath}\" type sequencer alias {alias}";
            if (!TryMci(openCommand, null, out var err))
            {
                SetStatus($"Failed to open MIDI: {GetMciError(err)}", Brushes.IndianRed);
                return false;
            }

            isOpen = true;
            TryMci($"set {alias} time format milliseconds", null, out _);
            lengthMs = QueryStatusNumber("length");
            positionMs = 0;
            NormalizeAudioSessionDisplayName();

            seekSlider.Maximum = Math.Max(1, lengthMs);
            seekSlider.Value = 0;
            lblLength.Text = FormatTime(lengthMs);
            lblPosition.Text = "00:00";

            currentLoadedPath = path;
            var info = TryParseMidiHeader(path);
            UpdateInfo(path, info);
            ApplyNowPlayingLabelsForLoadedPath(path);

            UpdateButtons();
            HighlightCurrent();
            SetStatus($"Loaded {Path.GetFileName(path)} ({FormatTime(lengthMs)})");
            return true;
        }

        bool TryActivatePreloadedWarmupForQueuedPath(string insertPath)
        {
            if (!preloadedNextDeviceOpen
                || string.IsNullOrWhiteSpace(preloadedNextPath)
                || !string.Equals(preloadedNextPath, insertPath, StringComparison.OrdinalIgnoreCase))
                return false;

            timer.Stop();
            if (isOpen)
            {
                TryMci($"stop {alias}", null, out _);
                TryMci($"close {alias}", null, out _);
            }

            var previousAlias = alias;
            alias = warmupAlias;
            warmupAlias = previousAlias;
            preloadedNextDeviceOpen = false;
            preloadedNextIndex = null;
            preloadedNextPath = null;

            isOpen = true;
            isPlaying = false;
            isPaused = false;
            currentLoadedPath = insertPath;

            TryMci($"set {alias} time format milliseconds", null, out _);
            lengthMs = QueryStatusNumber("length");
            positionMs = 0;
            NormalizeAudioSessionDisplayName();

            seekSlider.Maximum = Math.Max(1, lengthMs);
            seekSlider.Value = 0;
            lblLength.Text = FormatTime(lengthMs);
            lblPosition.Text = "00:00";

            var info = TryParseMidiHeader(insertPath);
            UpdateInfo(insertPath, info);
            ApplyNowPlayingLabelsForLoadedPath(insertPath);

            UpdateButtons();
            HighlightCurrent();
            UpdatePreloadIndicator();
            SetStatus($"Loaded {Path.GetFileName(insertPath)} ({FormatTime(lengthMs)})");
            return true;
        }

        void Play()
        {
            if (!isOpen) return;
            if (startTrackPrimedSilently && currentIndex >= 0 && currentIndex < playbackQueue.Count)
            {
                var song = playbackQueue[currentIndex];
                seekSlider.Maximum = Math.Max(1, lengthMs);
                seekSlider.Value = 0;
                lblLength.Text = FormatTime(lengthMs);
                lblPosition.Text = "00:00";
                lblNowPlaying.Text = song.Title;
                lblNowPlayingMeta.Text = song.Group;
                var info = TryParseMidiHeader(song.Path);
                UpdateInfo(song.Path, info);
                startTrackPrimedSilently = false;
                UpdatePreloadIndicator();
                HighlightCurrent();
            }
            // MCI sequencer device does not support the "resume" command (only
            // waveaudio/avivideo do). Resuming from pause requires us to seek
            // back to the stored position because the sequencer's "pause"
            // command may behave like "stop" and lose the playhead.
            if (isPaused && pausedPositionMs > 0)
            {
                TryMci($"seek {alias} to {pausedPositionMs}", null, out _);
            }
            if (!TryMci($"play {alias}", null, out var err))
            {
                SetStatus($"Play failed: {GetMciError(err)}", Brushes.IndianRed);
                return;
            }
            isPlaying = true;
            isPaused = false;
            timer.Start();
            NormalizeAudioSessionDisplayName();
            UpdateButtons();
        }

        void ClosePreloadedNextDevice()
        {
            if (!preloadedNextDeviceOpen)
                return;
            TryMci($"stop {warmupAlias}", null, out _);
            TryMci($"close {warmupAlias}", null, out _);
            preloadedNextDeviceOpen = false;
        }

        void ClearNextPreload(bool closeDevice = true)
        {
            if (closeDevice)
                ClosePreloadedNextDevice();
            preloadedNextIndex = null;
            preloadedNextPath = null;
            UpdatePreloadIndicator();
        }

        void ClearStartPreload()
        {
            preloadedStartIndex = null;
            preloadedStartPath = null;
            startTrackPrimedSilently = false;
            UpdatePreloadIndicator();
        }

        static bool IsValidIndex(int index, int count) => index >= 0 && index < count;

        bool TryPreopenNextPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            ClosePreloadedNextDevice();
            var openPath = GetMidiMciPath(path);
            if (!TryMci($"open \"{openPath}\" type sequencer alias {warmupAlias}", null, out _))
                return false;

            TryMci($"set {warmupAlias} time format milliseconds", null, out _);
            TryMci($"seek {warmupAlias} to start", null, out _);
            preloadedNextDeviceOpen = true;
            NormalizeAudioSessionDisplayName();
            return true;
        }

        int ResolveInitialPlayIndex()
        {
            if (playbackQueue.Count == 0)
                return -1;
            if (currentIndex >= 0 && currentIndex < playbackQueue.Count)
                return currentIndex;
            if (btnShuffle.IsChecked == true)
                return rng.Next(playbackQueue.Count);
            return 0;
        }

        bool TryPrimeStartTrack(int index)
        {
            if (!IsValidIndex(index, playbackQueue.Count))
                return false;

            var song = playbackQueue[index];
            if (string.IsNullOrWhiteSpace(song.Path) || !File.Exists(song.Path))
                return false;

            if (isOpen && !isPlaying)
                CloseDevice();
            if (isPlaying)
                return false;

            var openPath = GetMidiMciPath(song.Path);
            var openCommand = $"open \"{openPath}\" type sequencer alias {alias}";
            if (!TryMci(openCommand, null, out _))
                return false;

            isOpen = true;
            isPlaying = false;
            isPaused = false;
            TryMci($"set {alias} time format milliseconds", null, out _);
            lengthMs = QueryStatusNumber("length");
            positionMs = 0;
            NormalizeAudioSessionDisplayName();
            currentIndex = index;
            currentLoadedPath = song.Path;
            preloadedStartIndex = index;
            preloadedStartPath = song.Path;
            startTrackPrimedSilently = true;
            UpdatePreloadIndicator();
            return true;
        }

        int ResolveStartPlayIndex()
        {
            if (playbackQueue.Count == 0)
                return -1;

            if (currentIndex >= 0 && currentIndex < playbackQueue.Count)
                return currentIndex;

            if (preloadedStartIndex.HasValue
                && IsValidIndex(preloadedStartIndex.Value, playbackQueue.Count)
                && string.Equals(playbackQueue[preloadedStartIndex.Value].Path, preloadedStartPath, StringComparison.OrdinalIgnoreCase))
            {
                return preloadedStartIndex.Value;
            }

            return ResolveInitialPlayIndex();
        }

        void PrimeInitialPreload()
        {
            if (!hasDialogShown || isPlaying || playbackQueue.Count == 0)
                return;
            var startIndex = ResolveInitialPlayIndex();
            if (!IsValidIndex(startIndex, playbackQueue.Count))
                return;
            var path = playbackQueue[startIndex].Path;
            if (string.Equals(path, preloadedStartPath, StringComparison.OrdinalIgnoreCase)
                && preloadedStartIndex == startIndex)
                return;
            if (!TryPrimeStartTrack(startIndex))
                ClearStartPreload();
        }

        int ResolveAutoNextIndex(bool allowShuffle)
        {
            if (playbackQueue.Count == 0)
                return -1;
            if (allowShuffle && btnShuffle.IsChecked == true)
                return PickShuffleIndex();
            if (currentIndex >= 0 && currentIndex + 1 < playbackQueue.Count)
                return currentIndex + 1;
            return -1;
        }

        void TryPreloadUpcomingTrack(bool force)
        {
            if (!isOpen || btnLoop.IsChecked == true || lengthMs <= 0)
                return;

            if (pendingPlayAfterCurrentPath != null
                && !string.IsNullOrWhiteSpace(pendingPlayAfterCurrentPath)
                && File.Exists(pendingPlayAfterCurrentPath))
            {
                var qPath = pendingPlayAfterCurrentPath;
                if (preloadedNextDeviceOpen
                    && string.Equals(qPath, preloadedNextPath, StringComparison.OrdinalIgnoreCase))
                    return;

                if (TryPreopenNextPath(qPath))
                {
                    preloadedNextIndex = null;
                    preloadedNextPath = qPath;
                    UpdatePreloadIndicator();
                }
                else
                    ClearNextPreload(closeDevice: true);
                return;
            }

            if (!isPlaying)
                return;

            if (!force)
            {
                var remainingMs = lengthMs - positionMs;
                if (remainingMs > preloadLeadMs)
                    return;
            }

            int? candidateIndex;
            if (playingQueuedInterstitial
                && resumeIndexAfterQueuedTrack >= 0
                && IsValidIndex(resumeIndexAfterQueuedTrack, playbackQueue.Count))
            {
                candidateIndex = resumeIndexAfterQueuedTrack;
            }
            else
            {
                candidateIndex = preloadedNextIndex;
                if (candidateIndex is null
                    || candidateIndex < 0
                    || candidateIndex >= playbackQueue.Count
                    || candidateIndex == currentIndex)
                {
                    var resolved = ResolveAutoNextIndex(allowShuffle: true);
                    if (resolved < 0)
                        return;
                    candidateIndex = resolved;
                }
            }

            var nextPath = playbackQueue[candidateIndex.Value].Path;
            if (string.IsNullOrWhiteSpace(nextPath) || !File.Exists(nextPath))
                return;
            if (preloadedNextDeviceOpen
                && string.Equals(nextPath, preloadedNextPath, StringComparison.OrdinalIgnoreCase)
                && preloadedNextIndex == candidateIndex)
                return;

            // Keep the upcoming track actively pre-opened on the secondary alias.
            if (TryPreopenNextPath(nextPath))
            {
                preloadedNextIndex = candidateIndex;
                preloadedNextPath = nextPath;
                UpdatePreloadIndicator();
            }
            else
            {
                ClearNextPreload(closeDevice: true);
            }
        }

        void Pause()
        {
            if (!isOpen || !isPlaying) return;
            // Capture the position before pausing. The MCI sequencer driver's
            // "pause" command is known to behave like "stop" on some Windows
            // configurations (mode becomes "stopped" and position may reset),
            // so we remember where we were and seek back when resuming.
            pausedPositionMs = QueryStatusNumber("position");
            if (!TryMci($"pause {alias}", null, out var err))
            {
                SetStatus($"Pause failed: {GetMciError(err)}", Brushes.IndianRed);
                return;
            }
            isPlaying = false;
            isPaused = true;
            timer.Stop();
            UpdateButtons();
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
            ClearAllDeferredPlaybackIntent();
            ClearNextPreload();
            UpdateButtons();
            SetStatus("Stopped.");
        }

        void ResetPlayerView()
        {
            ClearAllDeferredPlaybackIntent();
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
                    NormalizeAudioSessionDisplayName();
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

        void PlayIndex(int index, bool slotIsViewRow = true)
        {
            ClearPendingQueueInsert();
            if (slotIsViewRow)
            {
                ClearPendingContinuePlayHere();
                if (index < 0 || index >= playlist.Count)
                    return;
                playbackPlaylistId = currentPlaylistId;
                RebuildPlaybackQueueFromPlaybackId();
                currentIndex = index;
            }
            else
            {
                if (index < 0 || index >= playbackQueue.Count)
                    return;
                currentIndex = index;
            }

            ClearStartPreload();
            shuffleHistory.Add(currentIndex);
            var song = playbackQueue[currentIndex];
            if (LoadFile(song.Path))
            {
                Play();
                // Queue preload right after playback starts, without blocking the click path.
                dlg.Dispatcher.BeginInvoke(
                    () => TryPreloadUpcomingTrack(force: true),
                    DispatcherPriority.Background);
            }
        }

        bool TryPlayPreloadedNext()
        {
            if (!preloadedNextDeviceOpen
                || !preloadedNextIndex.HasValue
                || preloadedNextIndex.Value < 0
                || preloadedNextIndex.Value >= playbackQueue.Count
                || preloadedNextIndex.Value == currentIndex)
            {
                return false;
            }

            ClearPendingQueueInsert();
            var nextIndex = preloadedNextIndex.Value;
            var nextSong = playbackQueue[nextIndex];
            if (string.IsNullOrWhiteSpace(nextSong.Path) || !File.Exists(nextSong.Path))
            {
                ClearNextPreload(closeDevice: true);
                return false;
            }

            timer.Stop();
            if (isOpen)
            {
                TryMci($"stop {alias}", null, out _);
                TryMci($"close {alias}", null, out _);
            }

            var previousAlias = alias;
            alias = warmupAlias;
            warmupAlias = previousAlias;
            preloadedNextDeviceOpen = false;

            ClearStartPreload();
            preloadedNextIndex = null;
            preloadedNextPath = null;

            isOpen = true;
            isPlaying = false;
            isPaused = false;
            currentIndex = nextIndex;
            currentLoadedPath = nextSong.Path;
            shuffleHistory.Add(nextIndex);

            TryMci($"set {alias} time format milliseconds", null, out _);
            lengthMs = QueryStatusNumber("length");
            positionMs = 0;
            seekSlider.Maximum = Math.Max(1, lengthMs);
            seekSlider.Value = 0;
            lblLength.Text = FormatTime(lengthMs);
            lblPosition.Text = "00:00";
            lblNowPlaying.Text = nextSong.Title;
            lblNowPlayingMeta.Text = nextSong.Group;
            var info = TryParseMidiHeader(nextSong.Path);
            UpdateInfo(nextSong.Path, info);
            HighlightCurrent();
            UpdatePreloadIndicator();

            Play();
            dlg.Dispatcher.BeginInvoke(
                () => TryPreloadUpcomingTrack(force: true),
                DispatcherPriority.Background);
            return true;
        }

        int PickShuffleIndex()
        {
            if (playbackQueue.Count == 0)
                return -1;
            if (playbackQueue.Count == 1)
                return 0;
            for (var attempts = 0; attempts < 8; attempts++)
            {
                var pick = rng.Next(playbackQueue.Count);
                if (pick != currentIndex)
                    return pick;
            }
            return rng.Next(playbackQueue.Count);
        }

        int ComputeNextPlaybackIndexForQueueAdvance()
        {
            if (playbackQueue.Count == 0)
                return -1;
            if (btnShuffle.IsChecked == true)
                return PickShuffleIndex();
            return currentIndex < 0 ? 0 : (currentIndex + 1) % playbackQueue.Count;
        }

        /// <summary>
        /// Consumes <see cref="pendingPlayAfterCurrentPath"/> and plays it as a one-shot,
        /// with resume index set to what would play next in the playlist from the current slot.
        /// Used when the current track ends naturally or when the user skips forward (Next).
        /// </summary>
        bool TryStartPendingQueuedInterstitial()
        {
            if (pendingPlayAfterCurrentPath == null
                || string.IsNullOrWhiteSpace(pendingPlayAfterCurrentPath)
                || !File.Exists(pendingPlayAfterCurrentPath))
                return false;

            var insertPath = pendingPlayAfterCurrentPath;
            pendingPlayAfterCurrentPath = null;
            resumeIndexAfterQueuedTrack = ComputeNextPlaybackIndexForQueueAdvance();
            playingQueuedInterstitial = true;
            ClearStartPreload();

            if (TryActivatePreloadedWarmupForQueuedPath(insertPath))
            {
                Play();
                dlg.Dispatcher.BeginInvoke(
                    () => TryPreloadUpcomingTrack(force: true),
                    DispatcherPriority.Background);
                return true;
            }

            ClearNextPreload(closeDevice: true);
            if (!LoadFile(insertPath))
            {
                playingQueuedInterstitial = false;
                resumeIndexAfterQueuedTrack = -1;
                pendingPlayAfterCurrentPath = insertPath;
                return false;
            }

            Play();
            dlg.Dispatcher.BeginInvoke(
                () => TryPreloadUpcomingTrack(force: true),
                DispatcherPriority.Background);
            return true;
        }

        void AdvanceAfterNaturalTrackEnd()
        {
            if (btnShuffle.IsChecked == true && playbackQueue.Count > 0)
            {
                PlayNext();
                return;
            }
            if (currentIndex >= 0 && currentIndex + 1 < playbackQueue.Count)
            {
                PlayIndex(currentIndex + 1, slotIsViewRow: false);
                return;
            }
            CloseDevice();
            ResetPlayerView();
            SetStatus("Finished.");
        }

        void AddSongToQueueAfterCurrent(string path, string queuedTitle, string? queuedGroup)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            if (!isOpen || playingQueuedInterstitial || currentIndex < 0)
                return;
            pendingPlayAfterCurrentPath = path;
            var display = string.IsNullOrWhiteSpace(queuedGroup)
                ? queuedTitle
                : $"{queuedGroup} - {queuedTitle}";
            SetStatus($"Queued {display}");
            dlg.Dispatcher.BeginInvoke(
                () => TryPreloadUpcomingTrack(force: true),
                DispatcherPriority.Background);
        }

        void ContinuePlayHere(int viewIndex)
        {
            if (viewIndex < 0 || viewIndex >= playlist.Count)
                return;
            if (!isOpen || currentIndex < 0)
            {
                PlayIndex(viewIndex, slotIsViewRow: true);
                return;
            }

            // Only one deferred handoff: a new choice replaces any previous "Continue play here".
            var hadExistingDeferral = pendingContinuePlayHereViewIndex.HasValue;
            pendingContinuePlayHereViewIndex = viewIndex;
            var song = playlist[viewIndex];
            var label = string.IsNullOrWhiteSpace(song.Group)
                ? song.Title
                : $"{song.Group} - {song.Title}";
            SetStatus($"Will continue with {label} after current track…");
            if (hadExistingDeferral)
            {
                dlg.Dispatcher.BeginInvoke(
                    () => TryPreloadUpcomingTrack(force: true),
                    DispatcherPriority.Background);
            }
        }

        void PlayNext()
        {
            if (playbackQueue.Count == 0) return;

            if (TryStartPendingQueuedInterstitial())
                return;

            // Skip ahead (Next) should honor deferred "Continue play here", not playlist/preload advance.
            if (pendingContinuePlayHereViewIndex is int contIdx
                && contIdx >= 0 && contIdx < playlist.Count)
            {
                ClearNextPreload(closeDevice: true);
                PlayIndex(contIdx, slotIsViewRow: true);
                SetStatus("Continuing on playlist.");
                return;
            }

            if (TryPlayPreloadedNext())
                return;

            int next;
            if (btnShuffle.IsChecked == true)
                next = PickShuffleIndex();
            else
                next = currentIndex < 0 ? 0 : (currentIndex + 1) % playbackQueue.Count;
            ClearNextPreload(closeDevice: true);
            PlayIndex(next, slotIsViewRow: false);
        }

        void PlayPrev()
        {
            if (playbackQueue.Count == 0) return;
            int prev;
            if (btnShuffle.IsChecked == true && shuffleHistory.Count >= 2)
            {
                shuffleHistory.RemoveAt(shuffleHistory.Count - 1);
                prev = shuffleHistory[^1];
                shuffleHistory.RemoveAt(shuffleHistory.Count - 1);
            }
            else
            {
                prev = currentIndex < 0 ? 0 : (currentIndex - 1 + playbackQueue.Count) % playbackQueue.Count;
            }
            ClearNextPreload();
            PlayIndex(prev, slotIsViewRow: false);
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
            // Only treat "stopped" mode as end-of-song when the playhead is
            // actually near the end. Some MCI sequencer drivers report
            // "stopped" briefly during transitions (e.g. after pause/resume),
            // which previously caused the next song to start.
            var atEndOfSong = lengthMs > 0 && positionMs >= lengthMs - 250;
            if (isPlaying && mode == "stopped" && atEndOfSong)
            {
                isPlaying = false;
                isPaused = false;
                timer.Stop();
                UpdateButtons();

                if (playingQueuedInterstitial)
                {
                    ClearNextPreload(closeDevice: true);
                    playingQueuedInterstitial = false;
                    var resume = resumeIndexAfterQueuedTrack;
                    resumeIndexAfterQueuedTrack = -1;

                    if (pendingContinuePlayHereViewIndex is int continueIdx
                        && continueIdx >= 0 && continueIdx < playlist.Count)
                    {
                        pendingContinuePlayHereViewIndex = null;
                        PlayIndex(continueIdx, slotIsViewRow: true);
                        SetStatus("Continuing on playlist.");
                        return;
                    }

                    if (resume >= 0 && IsValidIndex(resume, playbackQueue.Count))
                    {
                        PlayIndex(resume, slotIsViewRow: false);
                        SetStatus("Resuming playlist.");
                    }
                    else
                    {
                        CloseDevice();
                        ResetPlayerView();
                        SetStatus("Finished.");
                    }
                    return;
                }

                if (btnLoop.IsChecked == true)
                {
                    TryMci($"seek {alias} to start", null, out _);
                    Play();
                    SetStatus("Looping.");
                    return;
                }

                if (TryStartPendingQueuedInterstitial())
                    return;

                if (pendingContinuePlayHereViewIndex is int idxHere
                    && idxHere >= 0 && idxHere < playlist.Count)
                {
                    pendingContinuePlayHereViewIndex = null;
                    PlayIndex(idxHere, slotIsViewRow: true);
                    SetStatus("Continuing on playlist.");
                    return;
                }

                AdvanceAfterNaturalTrackEnd();
            }
        }

        timer.Tick += (_, _) => OnTick();

        // ---- Build/Rebuild the playlist UI ----------------------------------------------
        void RebuildPlaylistUi()
        {
            listStack.Children.Clear();
            itemBordersByIndex.Clear();
            groupHeadersByName.Clear();
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

            var filteredIndexes = Enumerable
                .Range(0, playlist.Count)
                .ToList();

            var groupOrder = new List<string>();
            if (filteredIndexes.Any(i => playlist[i].Group == MidiCustomGroupName))
                groupOrder.Add(MidiCustomGroupName);
            foreach (var g in filteredIndexes
                         .Select(i => playlist[i])
                         .Where(s => s.Group != MidiCustomGroupName)
                         .Select(s => s.Group)
                         .Distinct()
                         .OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
                groupOrder.Add(g);

            for (var gi = 0; gi < groupOrder.Count; gi++)
            {
                var group = groupOrder[gi];
                var groupBrush = BrushForMidiGroup(group, gi, groupOrder.Count);

                var headerCount = filteredIndexes.Count(i => playlist[i].Group == group);
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
                groupHeadersByName[group] = headerPanel;

                foreach (var i in filteredIndexes)
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
                        if (ViewIndexOfPlayingTrack() != index)
                            itemBorder.Background = rowHoverBrush;
                    };
                    itemBorder.MouseLeave += (_, _) =>
                    {
                        if (ViewIndexOfPlayingTrack() != index)
                            itemBorder.Background = rowBaseBrush;
                    };
                    itemBorder.MouseLeftButtonDown += (_, e) =>
                    {
                        PlayIndex(index);
                        e.Handled = true;
                    };

                    var rowMenu = new ContextMenu();
                    var miContinueHere = new MenuItem { Header = "Continue play here" };
                    var pathForQueue = song.Path;
                    miContinueHere.Click += (_, e) =>
                    {
                        e.Handled = true;
                        ContinuePlayHere(index);
                    };
                    var miAddQueue = new MenuItem { Header = "Add to queue" };
                    miAddQueue.Click += (_, e) =>
                    {
                        e.Handled = true;
                        AddSongToQueueAfterCurrent(pathForQueue, song.Title, song.Group);
                    };
                    rowMenu.Opened += (_, _) =>
                    {
                        miAddQueue.IsEnabled = isOpen && !playingQueuedInterstitial && currentIndex >= 0;
                    };

                    rowMenu.Items.Add(miAddQueue);

                    var addMenu = new MenuItem { Header = "Add to playlist" };
                    if (currentPlaylistId != MidiPlaylistIdClassical)
                    {
                        var addClassical = new MenuItem { Header = "Classical" };
                        var pathC = song.Path;
                        addClassical.Click += (_, e) =>
                        {
                            e.Handled = true;
                            AddSongToTargetPlaylist(MidiPlaylistIdClassical, pathC);
                        };
                        addMenu.Items.Add(addClassical);
                    }

                    if (currentPlaylistId != MidiPlaylistIdFocus)
                    {
                        var addFocus = new MenuItem { Header = "Focus" };
                        var pathFo = song.Path;
                        addFocus.Click += (_, e) =>
                        {
                            e.Handled = true;
                            AddSongToTargetPlaylist(MidiPlaylistIdFocus, pathFo);
                        };
                        addMenu.Items.Add(addFocus);
                    }

                    foreach (var up in midiPlaylistStore.UserPlaylists
                                 ?? new List<MidiUserPlaylistDto>())
                    {
                        if (string.Equals(up.Id, currentPlaylistId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var pathU = song.Path;
                        var uid = up.Id;
                        var label = up.Name;
                        var mi = new MenuItem { Header = label };
                        mi.Click += (_, e) =>
                        {
                            e.Handled = true;
                            AddSongToTargetPlaylist(uid, pathU);
                        };
                        addMenu.Items.Add(mi);
                    }

                    if (addMenu.Items.Count > 0)
                        rowMenu.Items.Add(addMenu);

                    rowMenu.Items.Add(miContinueHere);

                    if (song.IsCustom)
                    {
                        var pathDropCustom = song.Path;
                        var removeCustomLib = new MenuItem { Header = "Remove from Custom library" };
                        removeCustomLib.Click += (_, e) =>
                        {
                            e.Handled = true;
                            customPaths = customPaths
                                .Where(p => !string.Equals(p, pathDropCustom, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            SaveCustomMidiPaths(customPaths);
                            RemoveMidiPathFromStoredPlaylists(pathDropCustom);
                            SaveMidiPlaylistsStore(midiPlaylistStore);
                            playlist = BuildActiveMidiPlaylist(
                                currentPlaylistId,
                                customPaths,
                                midiPlaylistStore,
                                bundledOnlyCache);
                            RebuildPlaybackQueueFromPlaybackId();
                            shuffleHistory.Clear();
                            ClearStartPreload();
                            ClearNextPreload(closeDevice: true);
                            var rememberedPath = currentLoadedPath;
                            if (rememberedPath != null
                                && playbackQueue.Any(s =>
                                    string.Equals(s.Path, rememberedPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                currentIndex = playbackQueue.FindIndex(s =>
                                    string.Equals(s.Path, rememberedPath, StringComparison.OrdinalIgnoreCase));
                                RebuildPlaylistUi();
                                HighlightCurrent();
                                UpdateButtons();
                                PrimeInitialPreload();
                            }
                            else
                            {
                                CloseDevice();
                                RebuildPlaylistUi();
                                ResetPlayerView();
                            }

                            SetStatus("Removed from Custom library.");
                        };
                        rowMenu.Items.Add(removeCustomLib);
                    }

                    if (currentPlaylistId != MidiPlaylistIdAll)
                    {
                        var removeFromPl = new MenuItem { Header = "Remove from playlist" };
                        var pathRm = song.Path;
                        removeFromPl.Click += (_, e) =>
                        {
                            e.Handled = true;
                            RemoveSongFromCurrentPlaylist(pathRm);
                            SetStatus("Removed from playlist.");
                        };
                        rowMenu.Items.Add(removeFromPl);
                    }

                    if (playlist.Count > 0)
                    {
                        var dupPlaylist = new MenuItem { Header = "Duplicate playlist…" };
                        dupPlaylist.Click += (_, e) =>
                        {
                            e.Handled = true;
                            DuplicateViewPlaylistAsNewUserPlaylist();
                        };
                        rowMenu.Items.Add(dupPlaylist);
                    }

                    if (rowMenu.Items.Count > 0)
                        itemBorder.ContextMenu = rowMenu;

                    itemBordersByIndex[index] = itemBorder;
                    listStack.Children.Add(itemBorder);
                }
            }

            HighlightCurrent();
            listScroll.Dispatcher.BeginInvoke(SyncPlaylistScrollBarFromViewer, DispatcherPriority.Background);
        }

        // ---- Add files (browse) ---------------------------------------------------------
        void ShowPlaylistFilterDialog()
        {
            var filterWindow = new Window
            {
                Title = "Filter Songs",
                Width = 520,
                Height = 430,
                MinWidth = 520,
                MinHeight = 430,
                MaxWidth = 720,
                MaxHeight = 560,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x14, 0x24))
            };

            var shell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x0F, 0x17, 0x2A),
                    Color.FromRgb(0x0B, 0x14, 0x24),
                    90)
            };

            var root = new DockPanel();
            shell.Child = root;
            var lblFindSong = new TextBlock
            {
                Text = "Find Song",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(lblFindSong, Dock.Top);
            root.Children.Add(lblFindSong);

            var txtFilter = new TextBox
            {
                Text = string.Empty,
                Padding = new Thickness(8, 5, 8, 5),
                FontFamily = new FontFamily("Consolas, Courier New"),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x59)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(txtFilter, Dock.Top);
            root.Children.Add(txtFilter);

            var resultList = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x59)),
                Foreground = Brushes.White
            };
            resultList.Resources[typeof(ScrollBar)] = playlistScrollBarStyle;
            resultList.Resources[SystemColors.ScrollBarBrushKey] = new SolidColorBrush(Color.FromRgb(0x0D, 0x16, 0x27));
            resultList.Resources[SystemColors.ControlBrushKey] = new SolidColorBrush(Color.FromRgb(0x0D, 0x16, 0x27));
            resultList.Resources[SystemColors.ControlLightBrushKey] = new SolidColorBrush(Color.FromRgb(0x1E, 0x2B, 0x45));
            resultList.Resources[SystemColors.ControlDarkBrushKey] = new SolidColorBrush(Color.FromRgb(0x1E, 0x2B, 0x45));
            root.Children.Add(resultList);

            List<int> resultIndexes = [];

            void RefreshResults()
            {
                var query = (txtFilter.Text ?? string.Empty).Trim();
                resultIndexes = Enumerable
                    .Range(0, playlist.Count)
                    .Where(i => SongMatchesQuery(playlist[i], query))
                    .ToList();

                resultList.Items.Clear();
                foreach (var idx in resultIndexes.Take(200))
                {
                    var song = playlist[idx];
                    var item = new ListBoxItem
                    {
                        Tag = idx,
                        Padding = new Thickness(6, 4, 6, 4),
                        Content = $"{song.Title}  ({song.Group})",
                        Foreground = Brushes.White
                    };
                    resultList.Items.Add(item);
                }

                if (resultList.Items.Count > 0)
                    resultList.SelectedIndex = 0;
            }

            void PlaySelectedAndClose()
            {
                if (resultList.SelectedItem is ListBoxItem selected
                    && selected.Tag is int index
                    && index >= 0
                    && index < playlist.Count)
                {
                    PlayIndex(index);
                    filterWindow.Close();
                }
            }

            void MoveResultSelection(int delta)
            {
                var n = resultList.Items.Count;
                if (n == 0)
                    return;
                var i = resultList.SelectedIndex;
                if (i < 0 || i >= n)
                    i = 0;
                i = Math.Clamp(i + delta, 0, n - 1);
                resultList.SelectedIndex = i;
                if (resultList.SelectedItem != null)
                    resultList.ScrollIntoView(resultList.SelectedItem);
            }

            void FocusResultList()
            {
                resultList.Focus();
                Keyboard.Focus(resultList);
            }

            txtFilter.TextChanged += (_, _) => RefreshResults();
            txtFilter.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    PlaySelectedAndClose();
                    e.Handled = true;
                    return;
                }

                if (resultList.Items.Count == 0)
                    return;

                if (e.Key == Key.Down)
                {
                    // First Down from the filter moves from the first match to the second
                    // (then focuses the list). Further Down keys are handled on the ListBox.
                    if (resultList.SelectedIndex < 0 || resultList.SelectedIndex >= resultList.Items.Count)
                        resultList.SelectedIndex = 0;
                    MoveResultSelection(1);
                    FocusResultList();
                    e.Handled = true;
                }
            };

            resultList.MouseDoubleClick += (_, _) => PlaySelectedAndClose();
            resultList.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    PlaySelectedAndClose();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    filterWindow.Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Down && resultList.Items.Count > 0)
                {
                    MoveResultSelection(1);
                    e.Handled = true;
                }
                else if (e.Key == Key.Up && resultList.Items.Count > 0)
                {
                    if (resultList.SelectedIndex <= 0)
                    {
                        txtFilter.Focus();
                        Keyboard.Focus(txtFilter);
                    }
                    else
                        MoveResultSelection(-1);
                    e.Handled = true;
                }
            };

            filterWindow.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    filterWindow.Close();
                    e.Handled = true;
                }
            };
            filterWindow.Loaded += (_, _) =>
            {
                txtFilter.Focus();
                txtFilter.SelectAll();
                RefreshResults();
            };

            filterWindow.Content = shell;
            filterWindow.ShowDialog();
        }

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

            var rememberedPath = currentLoadedPath;

            playlist = BuildActiveMidiPlaylist(currentPlaylistId, customPaths, midiPlaylistStore, bundledOnlyCache);
            RebuildPlaybackQueueFromPlaybackId();
            ClearStartPreload();
            ClearNextPreload();

            if (rememberedPath is not null)
                currentIndex = playbackQueue.FindIndex(s =>
                    string.Equals(s.Path, rememberedPath, StringComparison.OrdinalIgnoreCase));

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

        void AddMidiPathsToCurrentPlaylist(IEnumerable<string> paths, bool playFirstAdded)
        {
            var validPaths = new List<string>();
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                if (!File.Exists(p))
                    continue;
                var ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext != ".mid" && ext != ".midi" && ext != ".rmi")
                    continue;
                validPaths.Add(p);
            }

            if (validPaths.Count == 0)
            {
                SetStatus("No new MIDI files added.");
                return;
            }

            if (currentPlaylistId == MidiPlaylistIdAll)
            {
                AddCustomFiles(validPaths, playFirstAdded);
                return;
            }

            if (currentPlaylistId == MidiPlaylistIdClassical)
            {
                midiPlaylistStore.ClassicalPaths ??= new List<string>();
                var added = new List<string>();
                foreach (var p in validPaths)
                {
                    if (midiPlaylistStore.ClassicalPaths.Any(x =>
                            string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    midiPlaylistStore.ClassicalPaths.Add(p);
                    added.Add(p);
                }

                if (added.Count == 0)
                {
                    SetStatus("No new MIDI files added (all were already in Classical).");
                    return;
                }

                SaveMidiPlaylistsStore(midiPlaylistStore);
                AfterPlaylistPathsMutated();
                SetStatus($"Added {added.Count} track{(added.Count == 1 ? string.Empty : "s")} to Classical.");
                if (playFirstAdded)
                {
                    var idx = playlist.FindIndex(s =>
                        string.Equals(s.Path, added[0], StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                        PlayIndex(idx);
                }

                return;
            }

            if (currentPlaylistId == MidiPlaylistIdFocus)
            {
                midiPlaylistStore.FocusPaths ??= new List<string>();
                var added = new List<string>();
                foreach (var p in validPaths)
                {
                    if (midiPlaylistStore.FocusPaths.Any(x =>
                            string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    midiPlaylistStore.FocusPaths.Add(p);
                    added.Add(p);
                }

                if (added.Count == 0)
                {
                    SetStatus("No new MIDI files added (all were already in Focus).");
                    return;
                }

                SaveMidiPlaylistsStore(midiPlaylistStore);
                AfterPlaylistPathsMutated();
                SetStatus($"Added {added.Count} track{(added.Count == 1 ? string.Empty : "s")} to Focus.");
                if (playFirstAdded)
                {
                    var idx = playlist.FindIndex(s =>
                        string.Equals(s.Path, added[0], StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                        PlayIndex(idx);
                }

                return;
            }

            var user = midiPlaylistStore.UserPlaylists?.FirstOrDefault(u => u.Id == currentPlaylistId);
            if (user == null)
            {
                SetStatus("Could not add files — playlist not found.");
                return;
            }

            user.Paths ??= new List<string>();
            var addedUser = new List<string>();
            foreach (var p in validPaths)
            {
                if (user.Paths.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                    continue;
                user.Paths.Add(p);
                addedUser.Add(p);
            }

            if (addedUser.Count == 0)
            {
                SetStatus($"No new MIDI files added (all were already in \"{user.Name}\").");
                return;
            }

            SaveMidiPlaylistsStore(midiPlaylistStore);
            AfterPlaylistPathsMutated();
            SetStatus($"Added {addedUser.Count} track{(addedUser.Count == 1 ? string.Empty : "s")} to \"{user.Name}\".");
            if (playFirstAdded)
            {
                var idx = playlist.FindIndex(s =>
                    string.Equals(s.Path, addedUser[0], StringComparison.OrdinalIgnoreCase));
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
                AddMidiPathsToCurrentPlaylist(ofd.FileNames, playFirstAdded: true);
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
            if (!isOpen && playbackQueue.Count > 0)
            {
                var startIndex = ResolveStartPlayIndex();
                if (startIndex >= 0)
                    PlayIndex(startIndex, slotIsViewRow: false);
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
            // Ctrl+M docks the player while keeping playback alive. Handle it
            // before the per-key switch so playback shortcuts (e.g. Space) on
            // the dialog stay independent from the global toggle binding.
            if (e.Key == Key.M
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == ModifierKeys.None)
            {
                DockMidiPlayerWindow();
                e.Handled = true;
                return;
            }
            switch (e.Key)
            {
                case Key.Space:
                    if (isOpen)
                    {
                        if (isPlaying) Pause(); else Play();
                        e.Handled = true;
                    }
                    else if (playbackQueue.Count > 0)
                    {
                        var startIndex = ResolveStartPlayIndex();
                        if (startIndex >= 0)
                            PlayIndex(startIndex, slotIsViewRow: false);
                        e.Handled = true;
                    }
                    break;
                case Key.Right:
                    if (playbackQueue.Count > 0)
                    {
                        PlayNext();
                        e.Handled = true;
                    }
                    break;
                case Key.Left:
                    if (playbackQueue.Count > 0)
                    {
                        PlayPrev();
                        e.Handled = true;
                    }
                    break;
                case Key.S:
                    if (Keyboard.Modifiers == ModifierKeys.None)
                    {
                        btnShuffle.IsChecked = btnShuffle.IsChecked != true;
                        e.Handled = true;
                    }
                    break;
                case Key.F:
                    if (Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control)
                    {
                        ShowPlaylistFilterDialog();
                        e.Handled = true;
                    }
                    break;
            }
        };

        dlg.Closed += (_, _) =>
        {
            _midiPlayerVolumePercent = Math.Clamp(
                (int)Math.Round(volumeBarPopup.Value),
                0,
                100);
            SaveWindowSettings();
            ClearAllDeferredPlaybackIntent();
            CloseDevice();
            _midiPlayerWindow = null;
            _midiPlayerDockAction = null;
            _midiPlayerRestoreAction = null;
            _midiPlayerNextAction = null;
            _midiPlayerPauseToggleAction = null;
            _midiPlayerDocked = false;
            _midiPlayerIsPlaying = false;
            _midiPlayerCurrentTitle = null;
            _midiPlayerCurrentGroup = null;
            _midiPlayerCurrentPlaylistName = null;
            RefreshMessageOverlayNowPlaying();
            UpdateMidiPlayerDockedIndicator();
        };
        dlg.ContentRendered += (_, _) =>
        {
            if (hasDialogShown)
                return;
            hasDialogShown = true;
            SyncMidiPlayerVolumeFromWindowsMixer();
            PrimeInitialPreload();
        };

        RebuildPlaylistUi();
        UpdateButtons();
        if (playlist.Count == 0)
            SetStatus("No tracks loaded - click + to add songs.");
        else
            SetStatus($"{playlist.Count} tracks ready.");

        SyncPlaylistScrollBarFromViewer();
        UpdateAddButtonTooltip();
        dlg.Content = root;
        _midiPlayerWindow = dlg;
        _midiPlayerDocked = false;
        _midiPlayerDockAction = DockMidiPlayerWindow;
        _midiPlayerRestoreAction = RestoreMidiPlayerWindow;
        _midiPlayerNextAction = PlayNext;
        _midiPlayerPauseToggleAction = () =>
        {
            if (!isOpen)
            {
                if (playbackQueue.Count == 0)
                    return;
                var startIndex = ResolveStartPlayIndex();
                if (startIndex >= 0)
                    PlayIndex(startIndex, slotIsViewRow: false);
                return;
            }
            if (isPlaying)
                Pause();
            else
                Play();
        };
        UpdateMidiPlayerDockedIndicator();
        dlg.Show();
        if (startDockedHidden)
            DockMidiPlayerWindow();
    }
}
