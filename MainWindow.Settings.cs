using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;
using Noted.Services;

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
                state,
                _backupFolder,
                DefaultBackupFolder(),
                SettingsFileName,
                opts);
            SaveTimeReports(opts);
            SaveProjectLineCounterState(opts);
            SaveTaskPanelPluginState(opts);
            SaveAlarmsPluginState(opts);
            SaveStandupPluginState(opts);
            SaveComputerStatisticsPluginState(opts);
            SaveMessageOverlayPluginState(opts);
            SaveSearchFilesHistory(opts);
            SaveTodoItems(opts);
            SaveSafePasteData(opts);
            SaveSafePasteKeys(opts);
            SaveSessionState(opts);
            SaveStandupNotes(opts);
        }
        catch { /* non-critical */ }
    }

    private WindowSettings CreateWindowSettingsSnapshot()
        => new()
        {
            LastNotedVersion = _persistedLastNotedVersionForJson,
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
            ShortcutToggleCriticalHighlight = _shortcutToggleCriticalHighlight,
            ShortcutGoToLine = _shortcutGoToLine,
            ShortcutGoToTab = _shortcutGoToTab,
            ShortcutMidiPlayer = _shortcutMidiPlayer,
            MidiPlayerVolumePercent = _midiPlayerVolumePercent,
            SelectedLineColor = ColorToHex(_selectedLineColor),
            HighlightedLineColor = ColorToHex(_highlightedLineColor),
            SelectedHighlightedLineColor = ColorToHex(_selectedHighlightedLineColor),
            CriticalHighlightedLineColor = ColorToHex(_criticalHighlightedLineColor),
            SelectedCriticalHighlightedLineColor = ColorToHex(_selectedCriticalHighlightedLineColor),
            BackupFolder = _backupFolder,
            CloudBackupFolder = _cloudBackupFolder,
            CloudSyncTabsPlainTextEnabled = _cloudSyncTabsPlainTextEnabled,
            CloudSyncTabsPlainTextFolder = string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextFolder)
                ? null
                : _cloudSyncTabsPlainTextFolder,
            CloudSyncTabsPlainTextAlsoDuringCloudSave = _cloudSyncTabsPlainTextAlsoDuringCloudSave,
            CloudSyncTabsPlainTextSyncHours = _cloudSyncTabsPlainTextSyncHours,
            CloudSyncTabsPlainTextSyncMinutes = _cloudSyncTabsPlainTextSyncMinutes,
            CloudSyncTabsPlainTextOutstreamHours = null,
            CloudSyncTabsPlainTextOutstreamMinutes = null,
            CloudSyncTabsPlainTextInFolder = string.IsNullOrWhiteSpace(_cloudSyncTabsPlainTextInFolder)
                ? null
                : _cloudSyncTabsPlainTextInFolder,
            CloudSyncTabsPlainTextInstreamEnabled = _cloudSyncTabsPlainTextInstreamEnabled,
            CloudSyncTabsPlainTextInstreamHours = null,
            CloudSyncTabsPlainTextInstreamMinutes = null,
            BackupAdditionalSettingsFile = _backupAdditionalIncludeSettingsFile,
            BackupAdditionalAppLog = _backupAdditionalIncludeAppLog,
            BackupAdditionalHeartbeatLogs = _backupAdditionalIncludeHeartbeatLogs,
            BackupAdditionalTodoItems = _backupAdditionalIncludeTodoItems,
            BackupAdditionalStateConfig = _backupAdditionalIncludeStateConfig,
            BackupAdditionalSessionState = _backupAdditionalIncludeSessionState,
            BackupAdditionalSafePaste = _backupAdditionalIncludeSafePaste,
            BackupAdditionalTimeReports = _backupAdditionalIncludeTimeReports,
            BackupAdditionalProjectLineCounter = _backupAdditionalIncludeProjectLineCounter,
            BackupAdditionalTaskPanel = _backupAdditionalIncludeTaskPanel,
            BackupAdditionalAlarms = _backupAdditionalIncludeAlarms,
            BackupAdditionalStandup = _backupAdditionalIncludeStandup,
            BackupAdditionalMessageOverlay = _backupAdditionalIncludeMessageOverlay,
            BackupAdditionalMidiCustomSongs = _backupAdditionalIncludeMidiCustomSongs,
            BackupAdditionalImages = _backupAdditionalIncludeImages,
            CloudSaveHours = _cloudSaveIntervalHours,
            CloudSaveMinutes = _cloudSaveIntervalMinutes,
            FridayFeelingEnabled = _isFridayFeelingEnabled,
            FancyBulletsEnabled = _fancyBulletsEnabled,
            WrapLongLinesVisually = _wrapLongLinesVisually,
            VisualLineWrapColumn = _visualLineWrapColumn,
            ShowSmileys = _showSmileys,
            RenderStyledTags = _renderStyledTags,
            ShowLineAssignments = _showLineAssignments,
            ShowBulletHoverTooltips = _showBulletHoverTooltips,
            ShowHorizontalRuler = _showHorizontalRuler,
            ShowInlineImages = _showInlineImages,
            FancyBulletStyle = FancyBulletStyleToSetting(_fancyBulletStyle),
            UptimeHeartbeatSeconds = _uptimeHeartbeatSeconds,
            WriteUptimeHeartbeatInNoted = _writeUptimeHeartbeatInNoted,
            UseStandaloneHeartbeatApp = _useStandaloneHeartbeatApp,
            Users = _users.Select(user => user.Name).ToList(),
            UserProfiles = NormalizeUsers(_users),
            SearchFilesHistoryLimit = _searchFilesHistoryLimit,
            TabCleanupStaleDays = _tabCleanupStaleDays,
            ClosedTabsMaxCount = _closedTabsMaxCount,
            ClosedTabsRetentionDays = _closedTabsRetentionDays,
            SaveBulletsAs = _saveBulletsAsMarker == '*' ? "*" : "-",
            ExternalBrowserForLinks = _externalBrowserForLinks
        };

    private NotedSessionState CreateNotedSessionStateSnapshot()
        => new()
        {
            Left = WindowState == WindowState.Normal ? Left : RestoreBounds.Left,
            Top = WindowState == WindowState.Normal ? Top : RestoreBounds.Top,
            Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
            Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height,
            Maximized = WindowState == WindowState.Maximized,
            ActiveTabIndex = MainTabControl.SelectedIndex,
            LastCloudCopyUtc = _lastCloudSaveUtc == DateTime.MinValue ? null : _lastCloudSaveUtc,
            LastPlainTabsFolderSyncUtc = _lastPlainTabsFolderSyncDisplayUtc == DateTime.MinValue
                ? null
                : _lastPlainTabsFolderSyncDisplayUtc,
            AlarmPopupLeft = _alarmPopupLeft,
            AlarmPopupTop = _alarmPopupTop,
            PluginAlarmsSnoozedUntilLocal = _pluginAlarmsSnoozedUntilLocal,
            StandupWindowLeft = _standupWindowLeft,
            StandupWindowTop = _standupWindowTop,
            StandupWindowWidth = _standupWindowWidth,
            StandupWindowHeight = _standupWindowHeight,
            StandupWindowMaximized = _standupWindowMaximized
        };

    private void SaveSessionState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, SessionStateFileName);
        _windowSettingsStore.Save(path, CreateNotedSessionStateSnapshot(), options);
    }

    private void SaveProjectLineCounterState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, ProjectLineCounterStateFileName);
        var payload = new ProjectLineCounterPluginState
        {
            ProjectLineCounterProjects = BuildProjectLineCounterProjectsSnapshot(),
            ProjectLineCounterTypes = BuildProjectLineCounterTypesSnapshot(),
            ProjectLineCounterIgnoredFileTypes = BuildProjectLineCounterIgnoredFileTypesSnapshot()
        };
        _windowSettingsStore.Save(path, payload, options);
    }

    private void SaveTaskPanelPluginState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, TaskPanelPluginStateFileName);
        var payload = new TaskPanelPluginState
        {
            TaskPanelTitle = _taskPanelTitle,
            TaskAreas = BuildTaskAreasSnapshot(),
            CurrentTaskAreaId = _currentTaskAreaId
        };
        _windowSettingsStore.Save(path, payload, options);
    }

    private void SaveAlarmsPluginState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, AlarmsPluginStateFileName);
        var payload = new AlarmsPluginState
        {
            PluginAlarms = BuildPluginAlarmsSnapshot(),
            PluginAlarmsEnabled = _pluginAlarmsEnabled
        };
        _windowSettingsStore.Save(path, payload, options);
    }

    private void SaveStandupPluginState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, StandupPluginStateFileName);
        var snap = BuildStandupPreferencesSnapshot();
        var payload = new StandupPluginState
        {
            WeekdayTimes = snap.WeekdayTimes,
            RetentionDays = snap.RetentionDays
        };
        _windowSettingsStore.Save(path, payload, options);
    }

    private void SaveComputerStatisticsPluginState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, ComputerStatisticsPluginStateFileName);
        var payload = new ComputerStatisticsPluginState
        {
            IdleThresholdSeconds = _computerStatisticsIdleThresholdSeconds,
            PassiveProgramKeys = _computerStatisticsPassiveProgramKeys.ToList()
        };
        _windowSettingsStore.Save(path, payload, options);
    }

    private void LoadComputerStatisticsPluginState()
    {
        var path = Path.Combine(_backupFolder, ComputerStatisticsPluginStateFileName);
        if (!File.Exists(path))
        {
            ApplyComputerStatisticsSettings(null);
            return;
        }

        var fromDisk = _windowSettingsStore.Load<ComputerStatisticsPluginState>(path);
        ApplyComputerStatisticsSettings(fromDisk);
    }

    private void SaveMessageOverlayPluginState(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, MessageOverlayPluginStateFileName);
        var payload = new MessageOverlayPluginState
        {
            MessageOverlayBlinkIntervalMs = _messageOverlayBlinkIntervalMs,
            MessageOverlayFadeMs = _messageOverlayFadeMs,
            MessageOverlayBlinkMode = _messageOverlayBlinkMode,
            MessageOverlayCountdownMinutes = _messageOverlayCountdownMinutes,
            MessageOverlayCountdownSeconds = _messageOverlayCountdownSeconds,
            MessageOverlayEffectEnabled = _messageOverlayEffectEnabled,
            MessageOverlayEffect = _messageOverlayEffect,
            MessageOverlayGnomeProbabilityPercent = _messageOverlayGnomeProbabilityPercent,
            QuickMessagePresets = BuildQuickMessagePresetsSnapshot(),
            QuickMessageColor = _quickMessageColorHex,
            QuickMessageCustom = _quickMessageCustom,
            QuickMessageLastUsed = _quickMessageLastUsed,
            QuickMessageLastUsedColorHex = string.IsNullOrEmpty(_quickMessageLastUsedColorHex)
                ? null
                : _quickMessageLastUsedColorHex,
            QuickMessageLastUsedCountdownSeconds = _quickMessageLastUsedCountdownSeconds,
            QuickMessageLastUsedEffectEnabled = _quickMessageLastUsedEffectEnabled,
            QuickMessageLastUsedEffect = _quickMessageLastUsedEffect,
            QuickMessageLastUsedShowNowPlaying = _quickMessageLastUsedShowNowPlaying
        };
        _windowSettingsStore.Save(path, payload, options);
    }

    private void LoadProjectLineCounterState(string effectiveSettingsJsonPath)
    {
        var path = Path.Combine(_backupFolder, ProjectLineCounterStateFileName);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        if (File.Exists(path))
        {
            var fromDisk = _windowSettingsStore.Load<ProjectLineCounterPluginState>(path);
            if (fromDisk != null)
            {
                ApplyProjectLineCounterSettings(
                    fromDisk.ProjectLineCounterProjects,
                    fromDisk.ProjectLineCounterTypes,
                    fromDisk.ProjectLineCounterIgnoredFileTypes);
                return;
            }
        }

        var legacy = TryReadProjectLineCounterFromSettingsJson(effectiveSettingsJsonPath);
        if (legacy != null && LineCounterJsonMentionedAnyList(legacy))
        {
            ApplyProjectLineCounterSettings(
                legacy.ProjectLineCounterProjects,
                legacy.ProjectLineCounterTypes,
                legacy.ProjectLineCounterIgnoredFileTypes);
            SaveProjectLineCounterState(opts);
            return;
        }

        ApplyProjectLineCounterSettings(null, null, null);
    }

    private void LoadTaskPanelPluginState(string effectiveSettingsJsonPath)
    {
        var path = Path.Combine(_backupFolder, TaskPanelPluginStateFileName);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        if (File.Exists(path))
        {
            var fromDisk = _windowSettingsStore.Load<TaskPanelPluginState>(path);
            if (fromDisk != null)
            {
                ApplyTaskPanelSettings(fromDisk);
                return;
            }
        }

        if (!SettingsJsonDeclaresTaskPanelKeys(effectiveSettingsJsonPath))
            return;

        var legacy = TryReadTaskPanelFromSettingsJson(effectiveSettingsJsonPath);
        if (legacy != null)
        {
            ApplyTaskPanelSettings(legacy);
            SaveTaskPanelPluginState(opts);
        }
    }

    private void LoadAlarmsPluginState(string effectiveSettingsJsonPath)
    {
        var path = Path.Combine(_backupFolder, AlarmsPluginStateFileName);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        if (File.Exists(path))
        {
            var fromDisk = _windowSettingsStore.Load<AlarmsPluginState>(path);
            if (fromDisk != null)
            {
                ApplyPluginAlarmSettings(fromDisk.PluginAlarms);
                _pluginAlarmsEnabled = fromDisk.PluginAlarmsEnabled;
                return;
            }
        }

        if (!SettingsJsonDeclaresAlarmsKeys(effectiveSettingsJsonPath))
            return;

        var legacy = TryReadAlarmsPayloadFromSettingsJson(effectiveSettingsJsonPath);
        if (legacy == null)
            return;
        ApplyPluginAlarmSettings(legacy.PluginAlarms);
        _pluginAlarmsEnabled = legacy.PluginAlarmsEnabled ?? true;
        SaveAlarmsPluginState(opts);
    }

    private void LoadStandupPluginState(string effectiveSettingsJsonPath)
    {
        var path = Path.Combine(_backupFolder, StandupPluginStateFileName);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        if (File.Exists(path))
        {
            var fromDisk = _windowSettingsStore.Load<StandupPluginState>(path);
            if (fromDisk != null)
            {
                ApplyStandupSettings(fromDisk);
                return;
            }
        }

        if (!SettingsJsonDeclaresStandupKey(effectiveSettingsJsonPath))
            return;

        var legacy = TryReadLegacyStandupFromSettingsJson(effectiveSettingsJsonPath);
        if (legacy?.Standup == null)
            return;
        ApplyStandupSettings(legacy.Standup);
        SaveStandupPluginState(opts);
    }

    private void LoadMessageOverlayPluginState(string effectiveSettingsJsonPath)
    {
        var path = Path.Combine(_backupFolder, MessageOverlayPluginStateFileName);
        var opts = new JsonSerializerOptions { WriteIndented = true };

        LegacyQuickMessagePayload? legacyQuick = SettingsJsonDeclaresQuickMessageKeys(effectiveSettingsJsonPath)
            ? TryReadLegacyQuickMessageFromSettingsJson(effectiveSettingsJsonPath)
            : null;

        MessageOverlayPluginState? combined = null;
        var migratedFullFromSettingsJson = false;

        if (File.Exists(path))
        {
            var fromDisk = _windowSettingsStore.Load<MessageOverlayPluginState>(path);
            if (fromDisk != null)
                combined = MergeOverlayPluginWithLegacyQuickMessages(fromDisk, legacyQuick);
        }

        var declaresLegacyInSettings =
            SettingsJsonDeclaresMessageOverlayKeys(effectiveSettingsJsonPath)
            || SettingsJsonDeclaresQuickMessageKeys(effectiveSettingsJsonPath);

        if (combined == null && declaresLegacyInSettings)
        {
            var fromSettings =
                TryReadMessageOverlayFromSettingsJson(effectiveSettingsJsonPath)
                ?? new MessageOverlayPluginState();
            combined = MergeOverlayPluginWithLegacyQuickMessages(fromSettings, legacyQuick);
            migratedFullFromSettingsJson = true;
        }

        if (combined == null && legacyQuick != null)
        {
            combined = MergeOverlayPluginWithLegacyQuickMessages(new MessageOverlayPluginState(), legacyQuick);
            migratedFullFromSettingsJson = true;
        }

        if (combined == null)
            return;

        ApplyQuickMessageOverlayPluginSettings(combined);
        ApplyQuickMessagePresetsFromPluginState(combined);

        if (migratedFullFromSettingsJson)
            SaveMessageOverlayPluginState(opts);
    }

    private sealed class LegacyQuickMessagePayload
    {
        public List<string>? QuickMessagePresets { get; set; }
        public string? QuickMessageColor { get; set; }
        public string? QuickMessageCustom { get; set; }
    }

    private static MessageOverlayPluginState MergeOverlayPluginWithLegacyQuickMessages(
        MessageOverlayPluginState plugin,
        LegacyQuickMessagePayload? legacyQuick)
    {
        if (legacyQuick == null)
            return plugin;

        return new MessageOverlayPluginState
        {
            MessageOverlayBlinkIntervalMs = plugin.MessageOverlayBlinkIntervalMs,
            MessageOverlayFadeMs = plugin.MessageOverlayFadeMs,
            MessageOverlayBlinkPhaseMs = plugin.MessageOverlayBlinkPhaseMs,
            MessageOverlayHoldMs = plugin.MessageOverlayHoldMs,
            MessageOverlayBlinkMode = plugin.MessageOverlayBlinkMode,
            MessageOverlayCountdownMinutes = plugin.MessageOverlayCountdownMinutes,
            MessageOverlayCountdownSeconds = plugin.MessageOverlayCountdownSeconds,
            MessageOverlayEffectEnabled = plugin.MessageOverlayEffectEnabled,
            MessageOverlayEffect = plugin.MessageOverlayEffect,
            MessageOverlayGnomeProbabilityPercent = plugin.MessageOverlayGnomeProbabilityPercent,
            QuickMessagePresets = plugin.QuickMessagePresets ?? legacyQuick.QuickMessagePresets,
            QuickMessageColor = plugin.QuickMessageColor ?? legacyQuick.QuickMessageColor,
            QuickMessageCustom = plugin.QuickMessageCustom ?? legacyQuick.QuickMessageCustom,
            QuickMessageLastUsed = plugin.QuickMessageLastUsed,
            QuickMessageLastUsedColorHex = plugin.QuickMessageLastUsedColorHex,
            QuickMessageLastUsedCountdownSeconds = plugin.QuickMessageLastUsedCountdownSeconds,
            QuickMessageLastUsedEffectEnabled = plugin.QuickMessageLastUsedEffectEnabled,
            QuickMessageLastUsedEffect = plugin.QuickMessageLastUsedEffect,
            QuickMessageLastUsedShowNowPlaying = plugin.QuickMessageLastUsedShowNowPlaying
        };
    }

    private static LegacyQuickMessagePayload? TryReadLegacyQuickMessageFromSettingsJson(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return null;
            return JsonSerializer.Deserialize<LegacyQuickMessagePayload>(
                File.ReadAllText(settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static bool SettingsJsonDeclaresQuickMessageKeys(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var root = doc.RootElement;
            return root.TryGetProperty("QuickMessagePresets", out _)
                   || root.TryGetProperty("QuickMessageColor", out _)
                   || root.TryGetProperty("QuickMessageCustom", out _);
        }
        catch
        {
            return false;
        }
    }

    private static MessageOverlayPluginState? TryReadMessageOverlayFromSettingsJson(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return null;
            return JsonSerializer.Deserialize<MessageOverlayPluginState>(
                File.ReadAllText(settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private sealed class LegacyStandupRoot
    {
        public StandupPluginState? Standup { get; set; }
    }

    private sealed class LegacyAlarmsPayload
    {
        public List<PluginAlarmSettings>? PluginAlarms { get; set; }
        public bool? PluginAlarmsEnabled { get; set; }
    }

    private static TaskPanelPluginState? TryReadTaskPanelFromSettingsJson(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return null;
            return JsonSerializer.Deserialize<TaskPanelPluginState>(
                File.ReadAllText(settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static LegacyAlarmsPayload? TryReadAlarmsPayloadFromSettingsJson(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return null;
            return JsonSerializer.Deserialize<LegacyAlarmsPayload>(
                File.ReadAllText(settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static LegacyStandupRoot? TryReadLegacyStandupFromSettingsJson(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return null;
            return JsonSerializer.Deserialize<LegacyStandupRoot>(
                File.ReadAllText(settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static bool SettingsJsonDeclaresTaskPanelKeys(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            return doc.RootElement.TryGetProperty("TaskPanelTitle", out _)
                   || doc.RootElement.TryGetProperty("TaskAreas", out _)
                   || doc.RootElement.TryGetProperty("CurrentTaskAreaId", out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool SettingsJsonDeclaresAlarmsKeys(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            return doc.RootElement.TryGetProperty("PluginAlarms", out _)
                   || doc.RootElement.TryGetProperty("PluginAlarmsEnabled", out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool SettingsJsonDeclaresStandupKey(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("Standup", out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool SettingsJsonDeclaresMessageOverlayKeys(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var root = doc.RootElement;
            return root.TryGetProperty("MessageOverlayBlinkIntervalMs", out _)
                   || root.TryGetProperty("MessageOverlayFadeMs", out _)
                   || root.TryGetProperty("MessageOverlayBlinkPhaseMs", out _)
                   || root.TryGetProperty("MessageOverlayHoldMs", out _)
                   || root.TryGetProperty("MessageOverlayBlinkMode", out _)
                   || root.TryGetProperty("MessageOverlayCountdownMinutes", out _)
                   || root.TryGetProperty("MessageOverlayCountdownSeconds", out _)
                   || root.TryGetProperty("MessageOverlayEffectEnabled", out _)
                   || root.TryGetProperty("MessageOverlayEffect", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects split-plugin keys still present in <c>settings.json</c> so we can rewrite without those keys.
    /// </summary>
    private static bool SettingsJsonContainsLegacyPluginSplitKeys(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var root = doc.RootElement;
            return root.TryGetProperty("PluginAlarms", out _)
                   || root.TryGetProperty("PluginAlarmsEnabled", out _)
                   || root.TryGetProperty("TaskPanelTitle", out _)
                   || root.TryGetProperty("TaskAreas", out _)
                   || root.TryGetProperty("CurrentTaskAreaId", out _)
                   || root.TryGetProperty("Standup", out _)
                   || root.TryGetProperty("MessageOverlayBlinkIntervalMs", out _)
                   || root.TryGetProperty("MessageOverlayFadeMs", out _)
                   || root.TryGetProperty("MessageOverlayBlinkPhaseMs", out _)
                   || root.TryGetProperty("MessageOverlayHoldMs", out _)
                   || root.TryGetProperty("MessageOverlayBlinkMode", out _)
                   || root.TryGetProperty("MessageOverlayCountdownMinutes", out _)
                   || root.TryGetProperty("MessageOverlayCountdownSeconds", out _)
                   || root.TryGetProperty("MessageOverlayEffectEnabled", out _)
                   || root.TryGetProperty("MessageOverlayEffect", out _)
                   || root.TryGetProperty("QuickMessagePresets", out _)
                   || root.TryGetProperty("QuickMessageColor", out _)
                   || root.TryGetProperty("QuickMessageCustom", out _);
        }
        catch
        {
            return false;
        }
    }

    private static ProjectLineCounterPluginState? TryReadProjectLineCounterFromSettingsJson(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return null;
            return JsonSerializer.Deserialize<ProjectLineCounterPluginState>(
                File.ReadAllText(settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects pre-split line-counter lists still present in <c>settings.json</c> so we can rewrite without those keys.
    /// </summary>
    private static bool SettingsJsonContainsLegacyProjectLineCounterKeys(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return false;
            using var doc = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            return doc.RootElement.TryGetProperty("ProjectLineCounterProjects", out _)
                   || doc.RootElement.TryGetProperty("ProjectLineCounterTypes", out _)
                   || doc.RootElement.TryGetProperty("ProjectLineCounterIgnoredFileTypes", out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool LineCounterJsonMentionedAnyList(ProjectLineCounterPluginState s)
        => s.ProjectLineCounterProjects != null
           || s.ProjectLineCounterTypes != null
           || s.ProjectLineCounterIgnoredFileTypes != null;

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
            _persistedLastNotedVersionForJson = loaded.EffectiveSettings.LastNotedVersion;

            LoadProjectLineCounterState(loaded.EffectiveSettingsJsonPath);
            LoadTaskPanelPluginState(loaded.EffectiveSettingsJsonPath);
            LoadAlarmsPluginState(loaded.EffectiveSettingsJsonPath);
            LoadStandupPluginState(loaded.EffectiveSettingsJsonPath);
            LoadComputerStatisticsPluginState();
            LoadMessageOverlayPluginState(loaded.EffectiveSettingsJsonPath);

            var sessionPath = Path.Combine(_backupFolder, SessionStateFileName);
            LegacyCombinedSettings? legacyCombined = null;
            if (SessionStateMigration.JsonLooksLikeLegacyCombinedSettings(loaded.EffectiveSettingsJsonPath))
                legacyCombined = SessionStateMigration.TryReadLegacyCombinedSettings(loaded.EffectiveSettingsJsonPath);

            var session = _windowSettingsStore.Load<NotedSessionState>(sessionPath);
            if (session == null && legacyCombined != null)
                session = SessionStateMigration.FromLegacy(legacyCombined);

            ApplyEffectiveSessionState(session);
            LoadSafePasteData(legacyCombined);
            LoadTimeReports();
            LoadSearchFilesHistory();
            LoadTodoItems();
            LoadStandupNotes();
            LoadStateConfig();
            if (_lastCloudSaveUtc == DateTime.MinValue)
                _lastCloudSaveUtc = GetLatestBackupWriteUtcOrMin(_cloudBackupFolder);
            ApplyColorThemeToOpenEditors();
            ApplyFridayFeelingToOpenEditors();

            // Line counter / task panel / alarms / standup / message overlay moved to plugin-*.json; stale keys are ignored on deserialize
            // and would never be removed if LastNotedVersion already matches (MaybeStamp skips SaveWindowSettings).
            if (SettingsJsonContainsLegacyProjectLineCounterKeys(loaded.EffectiveSettingsJsonPath)
                || SettingsJsonContainsLegacyPluginSplitKeys(loaded.EffectiveSettingsJsonPath))
                SaveWindowSettings();
        }
        catch { /* ignore corrupt settings */ }
    }

    private void ApplyEffectiveSessionState(NotedSessionState? session)
    {
        var s = session ?? new NotedSessionState();
        Left = s.Left;
        Top = s.Top;
        Width = s.Width;
        Height = s.Height;
        if (_windowSettingsService.TryGetNormalizedUtc(s.LastCloudCopyUtc, out var cloudCopyUtc))
            _lastCloudSaveUtc = cloudCopyUtc;
        if (_windowSettingsService.TryGetNormalizedUtc(s.LastPlainTabsFolderSyncUtc, out var plainTabsSyncUtc))
        {
            _lastPlainTabsFolderSyncDisplayUtc = plainTabsSyncUtc;
            _lastPlainTabsFolderSyncPassUtc = plainTabsSyncUtc;
        }
        if (s.ActiveTabIndex >= 0)
            _activeTabIndex = s.ActiveTabIndex;
        _pluginAlarmsSnoozedUntilLocal = s.PluginAlarmsSnoozedUntilLocal;
        if (_pluginAlarmsSnoozedUntilLocal is DateTime snoozedUntil
            && snoozedUntil <= DateTime.Now)
        {
            _pluginAlarmsSnoozedUntilLocal = null;
        }
        UpdateAlarmSnoozeStatus();
        if (s.AlarmPopupLeft is double popupLeft
            && !double.IsNaN(popupLeft)
            && !double.IsInfinity(popupLeft))
        {
            _alarmPopupLeft = popupLeft;
        }
        if (s.AlarmPopupTop is double popupTop
            && !double.IsNaN(popupTop)
            && !double.IsInfinity(popupTop))
        {
            _alarmPopupTop = popupTop;
        }
        ApplyStandupWindowFromSession(s);
        _startMaximized = s.Maximized;
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

    private NotedStateConfig CreateStateConfigSnapshot()
        => new()
        {
            TaskPanelOpen = _todoPanelVisible,
            LineAssigneeUsageCounts = _lineAssigneeUsageCounts.Count > 0
                ? new Dictionary<string, int>(_lineAssigneeUsageCounts, StringComparer.OrdinalIgnoreCase)
                : null,
            LastLineAssignee = string.IsNullOrWhiteSpace(_lastLineAssignee) ? null : _lastLineAssignee
        };

    private void SaveStateConfig(JsonSerializerOptions options)
    {
        var path = Path.Combine(_backupFolder, StateConfigFileName);
        _windowSettingsStore.Save(path, CreateStateConfigSnapshot(), options);
    }

    /// <summary>Writes <see cref="StateConfigFileName"/> only on exit (not on every <see cref="SaveWindowSettings"/>).</summary>
    private void SaveStateConfigOnExit()
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            SaveStateConfig(new JsonSerializerOptions { WriteIndented = true });
        }
        catch { /* non-critical */ }
    }

    private void LoadStateConfig()
    {
        var path = Path.Combine(_backupFolder, StateConfigFileName);
        var loaded = _windowSettingsStore.Load<NotedStateConfig>(path);
        if (loaded != null)
        {
            _todoPanelVisible = loaded.TaskPanelOpen;
            _lineAssigneeUsageCounts.Clear();
            if (loaded.LineAssigneeUsageCounts != null)
            {
                foreach (var kvp in loaded.LineAssigneeUsageCounts)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value <= 0)
                        continue;
                    _lineAssigneeUsageCounts[kvp.Key] = kvp.Value;
                }
            }
            _lastLineAssignee = string.IsNullOrWhiteSpace(loaded.LastLineAssignee) ? null : loaded.LastLineAssignee;
        }

        UpdateTodoPanelVisibility();
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
        _persistedLastNotedVersionForJson = null;
        _backupFolder = DefaultBackupFolder();
        _cloudBackupFolder = DefaultCloudBackupFolder();
        _cloudSyncTabsPlainTextEnabled = false;
        _cloudSyncTabsPlainTextFolder = string.Empty;
        _cloudSyncTabsPlainTextAlsoDuringCloudSave = false;
        _cloudSyncTabsPlainTextSyncHours = 0;
        _cloudSyncTabsPlainTextSyncMinutes = 5;
        _lastPlainTabsFolderSyncPassUtc = DateTime.MinValue;
        _lastPlainTabsFolderSyncDisplayUtc = DateTime.MinValue;
        _cloudSyncTabsPlainTextInstreamEnabled = false;
        _cloudSyncTabsPlainTextInFolder = string.Empty;
        _selectedLineColor = DefaultSelectedLineColor;
        _highlightedLineColor = DefaultHighlightedLineColor;
        _selectedHighlightedLineColor = DefaultSelectedHighlightedLineColor;
        _criticalHighlightedLineColor = DefaultCriticalHighlightedLineColor;
        _selectedCriticalHighlightedLineColor = DefaultSelectedCriticalHighlightedLineColor;
        _shortcutNewPrimary = DefaultShortcutNewPrimary;
        _shortcutNewSecondary = DefaultShortcutNewSecondary;
        _shortcutCloseTab = DefaultShortcutCloseTab;
        _shortcutRenameTab = DefaultShortcutRenameTab;
        _shortcutAddBlankLines = DefaultShortcutAddBlankLines;
        _shortcutTrimTrailingEmptyLines = DefaultShortcutTrimTrailingEmptyLines;
        _shortcutToggleHighlight = DefaultShortcutToggleHighlight;
        _shortcutToggleCriticalHighlight = DefaultShortcutToggleCriticalHighlight;
        _shortcutGoToLine = DefaultShortcutGoToLine;
        _shortcutGoToTab = DefaultShortcutGoToTab;
        _shortcutMidiPlayer = DefaultShortcutMidiPlayer;
        _midiPlayerVolumePercent = DefaultMidiPlayerVolumePercent;
        _uptimeHeartbeatSeconds = DefaultUptimeHeartbeatSeconds;
        _writeUptimeHeartbeatInNoted = true;
        _useStandaloneHeartbeatApp = false;
        _isFridayFeelingEnabled = true;
        _fancyBulletsEnabled = false;
        _wrapLongLinesVisually = true;
        _visualLineWrapColumn = DefaultVisualLineWrapColumn;
        _showSmileys = true;
        _renderStyledTags = true;
        _showLineAssignments = true;
        _showBulletHoverTooltips = true;
        _showHorizontalRuler = true;
        _showInlineImages = true;
        _externalBrowserForLinks = ExternalBrowserChoice.Default;
        _fancyBulletStyle = FancyBulletStyle.Dot;
        _isFredagspartySessionEnabled = false;
        _users = [];
        _timeReports.Clear();
        _pluginAlarms = [];
        _pluginAlarmsEnabled = true;
        _pluginAlarmsSnoozedUntilLocal = null;
        _alarmPopupLeft = null;
        _alarmPopupTop = null;
        _projectLineCounterProjects = [];
        _projectLineCounterTypes = [];
        _projectLineCounterIgnoredFileTypes = [];
        _searchFilesHistory = [];
        _searchFilesHistoryLimit = DefaultSearchFilesHistoryLimit;
        _tabCleanupStaleDays = DefaultTabCleanupStaleDays;
        _closedTabsMaxCount = DefaultClosedTabsMaxCount;
        _closedTabsRetentionDays = DefaultClosedTabsRetentionDays;
        _saveBulletsAsMarker = '-';
        _todoItems.Clear();
        _taskPanelTitle = DefaultTaskPanelTitle;
        _taskAreas = BuildDefaultTaskAreas();
        _currentTaskAreaId = DefaultTaskAreaId;
        _safePasteSavedEntries.Clear();
        ResetStandupSettingsToDefaults();
        ResetComputerStatisticsSettingsToDefaults();
        _todoPanelVisible = false;
        _lineAssigneeUsageCounts.Clear();
        _lastLineAssignee = null;
        _backupAdditionalIncludeSettingsFile = true;
        _backupAdditionalIncludeAppLog = true;
        _backupAdditionalIncludeHeartbeatLogs = true;
        _backupAdditionalIncludeTodoItems = true;
        _backupAdditionalIncludeStateConfig = true;
        _backupAdditionalIncludeSessionState = true;
        _backupAdditionalIncludeSafePaste = false;
        _backupAdditionalIncludeTimeReports = true;
        _backupAdditionalIncludeProjectLineCounter = true;
        _backupAdditionalIncludeTaskPanel = true;
        _backupAdditionalIncludeAlarms = true;
        _backupAdditionalIncludeStandup = true;
        _backupAdditionalIncludeMessageOverlay = true;
        _backupAdditionalIncludeMidiCustomSongs = false;
        _backupAdditionalIncludeImages = true;
        ResetQuickMessageOverlaySettings();
    }

    private void ApplyBootstrapSettings(string backupFolder, string cloudBackupFolder, WindowSettings bootstrap)
    {
        _backupFolder = backupFolder;
        _cloudBackupFolder = cloudBackupFolder;
        _cloudSyncTabsPlainTextEnabled = bootstrap.CloudSyncTabsPlainTextEnabled ?? false;
        if (!string.IsNullOrWhiteSpace(bootstrap.CloudSyncTabsPlainTextFolder))
        {
            try
            {
                _cloudSyncTabsPlainTextFolder = Path.GetFullPath(bootstrap.CloudSyncTabsPlainTextFolder.Trim());
            }
            catch
            {
                _cloudSyncTabsPlainTextFolder = bootstrap.CloudSyncTabsPlainTextFolder.Trim();
            }
        }
        else
            _cloudSyncTabsPlainTextFolder = string.Empty;

        _cloudSyncTabsPlainTextAlsoDuringCloudSave = bootstrap.CloudSyncTabsPlainTextAlsoDuringCloudSave ?? false;
        ApplyPlainTabsSyncIntervalFromSettings(bootstrap);

        if (!string.IsNullOrWhiteSpace(bootstrap.CloudSyncTabsPlainTextInFolder))
        {
            try
            {
                _cloudSyncTabsPlainTextInFolder = Path.GetFullPath(bootstrap.CloudSyncTabsPlainTextInFolder.Trim());
            }
            catch
            {
                _cloudSyncTabsPlainTextInFolder = bootstrap.CloudSyncTabsPlainTextInFolder.Trim();
            }
        }
        else
            _cloudSyncTabsPlainTextInFolder = string.Empty;

        if (_windowSettingsService.TryGetValidCloudHours(bootstrap.CloudSaveHours, out var cloudHours))
            _cloudSaveIntervalHours = cloudHours;
        if (_windowSettingsService.TryGetValidCloudMinutes(bootstrap.CloudSaveMinutes, out var cloudMinutes))
            _cloudSaveIntervalMinutes = cloudMinutes;

        _cloudSyncTabsPlainTextInstreamEnabled = bootstrap.CloudSyncTabsPlainTextInstreamEnabled ?? false;
    }

    private void ApplyPlainTabsSyncIntervalFromSettings(WindowSettings state)
    {
        if (_windowSettingsService.TryGetValidCloudHours(state.CloudSyncTabsPlainTextSyncHours, out var syncH))
            _cloudSyncTabsPlainTextSyncHours = syncH;
        else if (_windowSettingsService.TryGetValidCloudHours(state.CloudSyncTabsPlainTextInstreamHours, out syncH))
            _cloudSyncTabsPlainTextSyncHours = syncH;
        else if (_windowSettingsService.TryGetValidCloudHours(state.CloudSyncTabsPlainTextOutstreamHours, out syncH))
            _cloudSyncTabsPlainTextSyncHours = syncH;

        var plainHours = _cloudSyncTabsPlainTextSyncHours;
        if (_windowSettingsService.TryGetValidPlainTextTabSyncMinutes(state.CloudSyncTabsPlainTextSyncMinutes, plainHours, out var syncM))
            _cloudSyncTabsPlainTextSyncMinutes = syncM;
        else if (_windowSettingsService.TryGetValidPlainTextTabSyncMinutes(state.CloudSyncTabsPlainTextInstreamMinutes, plainHours, out syncM))
            _cloudSyncTabsPlainTextSyncMinutes = syncM;
        else if (_windowSettingsService.TryGetValidPlainTextTabSyncMinutes(state.CloudSyncTabsPlainTextOutstreamMinutes, plainHours, out syncM))
            _cloudSyncTabsPlainTextSyncMinutes = syncM;
    }

    private void ApplyEffectiveWindowSettings(WindowSettings state)
    {
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
        _cloudSyncTabsPlainTextEnabled = state.CloudSyncTabsPlainTextEnabled ?? false;
        if (!string.IsNullOrWhiteSpace(state.CloudSyncTabsPlainTextFolder))
        {
            try
            {
                _cloudSyncTabsPlainTextFolder = Path.GetFullPath(state.CloudSyncTabsPlainTextFolder.Trim());
            }
            catch
            {
                _cloudSyncTabsPlainTextFolder = state.CloudSyncTabsPlainTextFolder.Trim();
            }
        }
        else
            _cloudSyncTabsPlainTextFolder = string.Empty;

        _cloudSyncTabsPlainTextAlsoDuringCloudSave = state.CloudSyncTabsPlainTextAlsoDuringCloudSave ?? false;
        ApplyPlainTabsSyncIntervalFromSettings(state);

        if (!string.IsNullOrWhiteSpace(state.CloudSyncTabsPlainTextInFolder))
        {
            try
            {
                _cloudSyncTabsPlainTextInFolder = Path.GetFullPath(state.CloudSyncTabsPlainTextInFolder.Trim());
            }
            catch
            {
                _cloudSyncTabsPlainTextInFolder = state.CloudSyncTabsPlainTextInFolder.Trim();
            }
        }
        else
            _cloudSyncTabsPlainTextInFolder = string.Empty;

        if (_windowSettingsService.TryGetValidCloudHours(state.CloudSaveHours, out var cloudHours))
            _cloudSaveIntervalHours = cloudHours;
        if (_windowSettingsService.TryGetValidCloudMinutes(state.CloudSaveMinutes, out var cloudMinutes))
            _cloudSaveIntervalMinutes = cloudMinutes;

        _cloudSyncTabsPlainTextInstreamEnabled = state.CloudSyncTabsPlainTextInstreamEnabled ?? false;
        if (_windowSettingsService.TryGetValidUptimeHeartbeatSeconds(state.UptimeHeartbeatSeconds, out var uptimeHeartbeatSeconds))
            _uptimeHeartbeatSeconds = uptimeHeartbeatSeconds;
        _writeUptimeHeartbeatInNoted = state.WriteUptimeHeartbeatInNoted;
        _useStandaloneHeartbeatApp = state.UseStandaloneHeartbeatApp;
        _isFridayFeelingEnabled = state.FridayFeelingEnabled;
        _fancyBulletsEnabled = state.FancyBulletsEnabled;
        _wrapLongLinesVisually = state.WrapLongLinesVisually;
        _visualLineWrapColumn = NormalizeVisualLineWrapColumn(state.VisualLineWrapColumn);
        _showSmileys = state.ShowSmileys;
        _renderStyledTags = state.RenderStyledTags ?? true;
        _showLineAssignments = state.ShowLineAssignments ?? true;
        _showBulletHoverTooltips = state.ShowBulletHoverTooltips ?? true;
        _showHorizontalRuler = state.ShowHorizontalRuler;
        _showInlineImages = state.ShowInlineImages;
        _fancyBulletStyle = ParseFancyBulletStyle(state.FancyBulletStyle);
        UpdateViewMenuChecks();

        var loadedUsers = NormalizeUsers(state.UserProfiles);
        if (loadedUsers.Count == 0)
            loadedUsers = BuildUsersFromLegacyNames(state.Users);
        _users = loadedUsers;
        _searchFilesHistoryLimit = NormalizeSearchFilesHistoryLimit(state.SearchFilesHistoryLimit);
        if (state.TabCleanupStaleDays >= 1 && state.TabCleanupStaleDays <= 3650)
            _tabCleanupStaleDays = state.TabCleanupStaleDays;
        _closedTabsMaxCount = NormalizeClosedTabsMaxCount(state.ClosedTabsMaxCount);
        _closedTabsRetentionDays = NormalizeClosedTabsRetentionDays(state.ClosedTabsRetentionDays);
        _saveBulletsAsMarker = string.Equals(state.SaveBulletsAs, "*", StringComparison.Ordinal) ? '*' : '-';
        _midiPlayerVolumePercent = NormalizeMidiPlayerVolumePercent(state.MidiPlayerVolumePercent);
        _backupAdditionalIncludeSettingsFile = state.BackupAdditionalSettingsFile ?? true;
        _backupAdditionalIncludeAppLog = state.BackupAdditionalAppLog ?? true;
        _backupAdditionalIncludeHeartbeatLogs = state.BackupAdditionalHeartbeatLogs ?? true;
        _backupAdditionalIncludeTodoItems = state.BackupAdditionalTodoItems ?? true;
        _backupAdditionalIncludeStateConfig = state.BackupAdditionalStateConfig ?? true;
        _backupAdditionalIncludeSessionState = state.BackupAdditionalSessionState ?? true;
        _backupAdditionalIncludeSafePaste = state.BackupAdditionalSafePaste ?? false;
        _backupAdditionalIncludeTimeReports = state.BackupAdditionalTimeReports ?? true;
        _backupAdditionalIncludeProjectLineCounter = state.BackupAdditionalProjectLineCounter ?? true;
        _backupAdditionalIncludeTaskPanel = state.BackupAdditionalTaskPanel ?? true;
        _backupAdditionalIncludeAlarms = state.BackupAdditionalAlarms ?? true;
        _backupAdditionalIncludeStandup = state.BackupAdditionalStandup ?? true;
        _backupAdditionalIncludeMessageOverlay = state.BackupAdditionalMessageOverlay ?? true;
        _backupAdditionalIncludeMidiCustomSongs = state.BackupAdditionalMidiCustomSongs ?? false;
        _backupAdditionalIncludeImages = state.BackupAdditionalImages ?? true;

        ApplyThemeColorsFromSettings(state);
        _externalBrowserForLinks = NormalizeExternalBrowserChoice(state.ExternalBrowserForLinks);
    }

    private static ExternalBrowserChoice NormalizeExternalBrowserChoice(ExternalBrowserChoice value)
        => Enum.IsDefined(typeof(ExternalBrowserChoice), value) ? value : ExternalBrowserChoice.Default;

    private void SyncExternalBrowserLauncherPreference()
        => SafeHttpUriLauncher.GetPreferredExternalBrowser = () => _externalBrowserForLinks;

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
        if (TryParseKeyGesture(state.ShortcutToggleCriticalHighlight, out _))
            _shortcutToggleCriticalHighlight = state.ShortcutToggleCriticalHighlight!.Trim();
        if (TryParseKeyGesture(state.ShortcutGoToLine, out _))
            _shortcutGoToLine = state.ShortcutGoToLine!.Trim();
        if (TryParseKeyGesture(state.ShortcutGoToTab, out _))
            _shortcutGoToTab = state.ShortcutGoToTab!.Trim();
        if (TryParseKeyGesture(state.ShortcutMidiPlayer, out _))
            _shortcutMidiPlayer = state.ShortcutMidiPlayer!.Trim();
    }

    private void ApplyThemeColorsFromSettings(WindowSettings state)
    {
        if (TryParseColor(state.SelectedLineColor, out var selectedLineColor))
            _selectedLineColor = selectedLineColor;
        if (TryParseColor(state.HighlightedLineColor, out var highlightedLineColor))
            _highlightedLineColor = MigrateHighlightedLineColor(highlightedLineColor);
        if (TryParseColor(state.SelectedHighlightedLineColor, out var selectedHighlightedLineColor))
            _selectedHighlightedLineColor = MigrateSelectedHighlightedLineColor(selectedHighlightedLineColor);
        if (TryParseColor(state.CriticalHighlightedLineColor, out var criticalHighlightedLineColor))
            _criticalHighlightedLineColor = criticalHighlightedLineColor;
        if (TryParseColor(state.SelectedCriticalHighlightedLineColor, out var selectedCriticalHighlightedLineColor))
            _selectedCriticalHighlightedLineColor = selectedCriticalHighlightedLineColor;
    }

    private List<TaskAreaState> BuildDefaultTaskAreas()
    {
        var area = new TaskAreaState
        {
            Id = DefaultTaskAreaId,
            Name = DefaultTaskAreaName,
            Groups = []
        };
        for (int i = 0; i < DefaultTaskGroups.Length; i++)
        {
            area.Groups.Add(new TaskGroupState
            {
                Id = DefaultTaskGroups[i].Id,
                Name = DefaultTaskGroups[i].Name,
                ShortcutKey = DefaultTaskGroups[i].ShortcutKey,
                SortOrder = i + 1,
                CompletedRetentionDays = DefaultTaskGroups[i].CompletedRetentionDays,
                CompletedRetentionHours = DefaultTaskGroups[i].CompletedRetentionHours
            });
        }
        return [area];
    }

    private static string NormalizeTaskGroupShortcutKey(string? rawShortcut)
    {
        var shortcut = (rawShortcut ?? string.Empty).Trim();
        if (shortcut.Length == 0)
            return string.Empty;
        if (shortcut == "+")
            return "+";
        return shortcut.Length == 1 ? shortcut.ToUpperInvariant() : string.Empty;
    }

    private static string DefaultTaskGroupShortcutById(string groupId)
    {
        foreach (var entry in DefaultTaskGroups)
        {
            if (string.Equals(entry.Id, groupId, StringComparison.OrdinalIgnoreCase))
                return entry.ShortcutKey;
        }
        return string.Empty;
    }

    private static int DefaultCompletedRetentionDaysForGroupId(string groupId)
    {
        foreach (var entry in DefaultTaskGroups)
        {
            if (string.Equals(entry.Id, groupId, StringComparison.OrdinalIgnoreCase))
                return entry.CompletedRetentionDays;
        }
        return DefaultCompletedRetentionDays;
    }

    private static int NormalizeCompletedRetentionDays(int? retentionDays, string groupId)
    {
        var value = retentionDays ?? DefaultCompletedRetentionDaysForGroupId(groupId);
        return Math.Clamp(value, MinCompletedRetentionDays, MaxCompletedRetentionDays);
    }

    private static int NormalizeCompletedRetentionHours(int? retentionHours)
    {
        var value = retentionHours ?? DefaultCompletedRetentionHours;
        return Math.Clamp(value, MinCompletedRetentionHours, MaxCompletedRetentionHours);
    }

    private static int NormalizeUndoneMarkDays(int? days)
        => Math.Clamp(days ?? 0, MinUndoneMarkDays, MaxUndoneMarkDays);

    private static int NormalizeUndoneMarkHours(int? hours)
        => Math.Clamp(hours ?? MinUndoneMarkHours, MinUndoneMarkHours, MaxUndoneMarkHours);

    private List<TaskAreaState> BuildTaskAreasSnapshot()
        => _taskAreas
            .Where(area => area != null && !string.IsNullOrWhiteSpace(area.Id))
            .Select(area => new TaskAreaState
            {
                Id = area.Id,
                Name = string.IsNullOrWhiteSpace(area.Name) ? area.Id : area.Name,
                Groups = (area.Groups ?? [])
                    .Where(group => group != null && !string.IsNullOrWhiteSpace(group.Id))
                    .Select((group, index) => new TaskGroupState
                    {
                        Id = group.Id,
                        Name = string.IsNullOrWhiteSpace(group.Name) ? group.Id : group.Name,
                        ShortcutKey = NormalizeTaskGroupShortcutKey(group.ShortcutKey),
                        SortOrder = group.SortOrder > 0 ? group.SortOrder : index + 1,
                        CompletedRetentionDays = NormalizeCompletedRetentionDays(group.CompletedRetentionDays, group.Id),
                        CompletedRetentionHours = NormalizeCompletedRetentionHours(group.CompletedRetentionHours),
                        UndoneMarkEnabled = group.UndoneMarkEnabled == true,
                        UndoneMarkDays = NormalizeUndoneMarkDays(group.UndoneMarkDays),
                        UndoneMarkHours = NormalizeUndoneMarkHours(group.UndoneMarkHours)
                    })
                    .ToList()
            })
            .ToList();

    private void ApplyTaskPanelSettings(TaskPanelPluginState state)
    {
        _taskPanelTitle = string.IsNullOrWhiteSpace(state.TaskPanelTitle)
            ? DefaultTaskPanelTitle
            : state.TaskPanelTitle!.Trim();

        var areas = (state.TaskAreas ?? [])
            .Where(area => area != null && !string.IsNullOrWhiteSpace(area.Id))
            .Select(area => new TaskAreaState
            {
                Id = area.Id.Trim(),
                Name = string.IsNullOrWhiteSpace(area.Name) ? area.Id.Trim() : area.Name.Trim(),
                Groups = (area.Groups ?? [])
                    .Where(group => group != null && !string.IsNullOrWhiteSpace(group.Id))
                    .Select((group, index) => new TaskGroupState
                    {
                        Id = group.Id.Trim(),
                        Name = string.IsNullOrWhiteSpace(group.Name) ? group.Id.Trim() : group.Name.Trim(),
                        ShortcutKey = NormalizeTaskGroupShortcutKey(group.ShortcutKey),
                        SortOrder = group.SortOrder > 0 ? group.SortOrder : index + 1,
                        CompletedRetentionDays = NormalizeCompletedRetentionDays(group.CompletedRetentionDays, group.Id?.Trim() ?? string.Empty),
                        CompletedRetentionHours = NormalizeCompletedRetentionHours(group.CompletedRetentionHours),
                        UndoneMarkEnabled = group.UndoneMarkEnabled == true,
                        UndoneMarkDays = NormalizeUndoneMarkDays(group.UndoneMarkDays),
                        UndoneMarkHours = NormalizeUndoneMarkHours(group.UndoneMarkHours)
                    })
                    .ToList()
            })
            .ToList();

        if (areas.Count == 0)
        {
            areas = BuildDefaultTaskAreas();
        }
        else
        {
            // Ensure the default Main area exists, and that it has the 3 default groups.
            var mainArea = areas.FirstOrDefault(a => string.Equals(a.Id, DefaultTaskAreaId, StringComparison.OrdinalIgnoreCase));
            if (mainArea == null)
            {
                areas.Insert(0, BuildDefaultTaskAreas()[0]);
            }
            else if (mainArea.Groups.Count == 0)
            {
                var defaults = BuildDefaultTaskAreas()[0];
                mainArea.Groups = defaults.Groups;
            }
        }

        _taskAreas = areas;
        foreach (var area in _taskAreas)
        {
            foreach (var group in area.Groups)
            {
                var shortcut = NormalizeTaskGroupShortcutKey(group.ShortcutKey);
                if (shortcut.Length == 0)
                    shortcut = DefaultTaskGroupShortcutById(group.Id);
                group.ShortcutKey = shortcut;
            }
        }

        var desiredCurrent = (state.CurrentTaskAreaId ?? string.Empty).Trim();
        _currentTaskAreaId = _taskAreas.Any(area => string.Equals(area.Id, desiredCurrent, StringComparison.OrdinalIgnoreCase))
            ? desiredCurrent
            : _taskAreas[0].Id;
    }

    private Color MigrateHighlightedLineColor(Color color)
        => _colorThemeService.MigrateHighlightedLineColor(color, DefaultHighlightedLineColor);

    private Color MigrateSelectedHighlightedLineColor(Color color)
        => _colorThemeService.MigrateSelectedHighlightedLineColor(color, DefaultSelectedHighlightedLineColor);

    /// <summary>Creates <see cref="SettingsFileName"/> under the current backup folder when missing.</summary>
    private void EnsureSettingsFileExists()
        => _settingsService.EnsureFileExists(_backupFolder, SettingsFileName, SaveWindowSettings);

    private bool CopySettingsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, SettingsFileName);

    private void CopyClosedTabsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, ClosedTabsFileName);

    private void CopySearchFilesHistoryFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExists(fromFolder, toFolder, SearchFilesHistoryFileName);

    private bool CopyTimeReportsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, TimeReportsFileName);

    private bool CopyProjectLineCounterStateFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, ProjectLineCounterStateFileName);

    private bool CopyTaskPanelPluginStateFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, TaskPanelPluginStateFileName);

    private bool CopyAlarmsPluginStateFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, AlarmsPluginStateFileName);

    private bool CopyStandupPluginStateFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, StandupPluginStateFileName);

    private bool CopyMessageOverlayPluginStateFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, MessageOverlayPluginStateFileName);

    private bool CopyTodoItemsFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, TodoItemsFileName);

    private bool CopyStateConfigFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, StateConfigFileName);

    private bool CopySessionStateFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, SessionStateFileName);

    private bool CopySafePasteDataFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, SafePasteDataFileName);

    private bool CopySafePasteKeysFileToBackupFolder(string fromFolder, string toFolder)
        => _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, SafePasteKeysFileName);

    private int CopyImageFolderToBackupFolder(string fromFolder, string toFolder)
    {
        var copied = 0;
        try
        {
            var sourceImageFolder = Path.Combine(fromFolder, BackupImagesFolderName);
            if (!Directory.Exists(sourceImageFolder))
                return 0;

            var destinationImageFolder = Path.Combine(toFolder, BackupImagesFolderName);
            Directory.CreateDirectory(destinationImageFolder);
            foreach (var sourcePath in Directory.GetFiles(sourceImageFolder, "*.png"))
            {
                var targetPath = Path.Combine(destinationImageFolder, Path.GetFileName(sourcePath));
                if (!ShouldCopyByTimestampOrSize(sourcePath, targetPath))
                    continue;
                File.Copy(sourcePath, targetPath, overwrite: true);
                copied++;
            }
        }
        catch
        {
            // Best-effort migration.
        }

        return copied;
    }

    private static bool ShouldCopyByTimestampOrSize(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(destinationPath))
                return true;

            var src = new FileInfo(sourcePath);
            var dst = new FileInfo(destinationPath);
            if (src.Length != dst.Length)
                return true;

            return src.LastWriteTimeUtc > dst.LastWriteTimeUtc;
        }
        catch
        {
            return true;
        }
    }

    private static int CopyHeartbeatLogFilesBetweenFolders(string fromFolder, string toFolder)
    {
        var copied = 0;
        try
        {
            if (string.IsNullOrWhiteSpace(fromFolder) || string.IsNullOrWhiteSpace(toFolder))
                return 0;
            if (!Directory.Exists(fromFolder))
                return 0;

            Directory.CreateDirectory(toFolder);
            var pattern = $"{UptimeHeartbeatService.FileNamePrefix}*.log";
            foreach (var path in Directory.GetFiles(fromFolder, pattern))
            {
                var dest = Path.Combine(toFolder, Path.GetFileName(path));
                if (!ShouldCopyByTimestampOrSize(path, dest))
                    continue;
                File.Copy(path, dest, overwrite: true);
                copied++;
            }
        }
        catch
        {
            // Best-effort.
        }

        return copied;
    }

    private readonly struct AdditionalBackupArtifactsSummary
    {
        public bool IncludeSettings { get; init; }
        public bool IncludeAppLog { get; init; }
        public bool IncludeHeartbeat { get; init; }
        public bool IncludeTodoItems { get; init; }
        public bool IncludeStateConfig { get; init; }
        public bool IncludeSessionState { get; init; }
        public bool IncludeSafePaste { get; init; }
        public bool IncludeTimeReports { get; init; }
        public bool IncludeProjectLineCounter { get; init; }
        public bool IncludeTaskPanel { get; init; }
        public bool IncludeAlarms { get; init; }
        public bool IncludeStandup { get; init; }
        public bool IncludeMessageOverlay { get; init; }
        public bool IncludeMidiCustomSongs { get; init; }
        public bool IncludeImages { get; init; }

        /// <summary>Files copied per category (single-file cats are 0 or 1; multi-file heartbeat/images use higher counts).</summary>
        public int SettingsFilesCopied { get; init; }
        public int AppLogFilesCopied { get; init; }
        public int HeartbeatFilesCopied { get; init; }
        public int TodoItemsFilesCopied { get; init; }
        public int StateConfigFilesCopied { get; init; }
        public int SessionStateFilesCopied { get; init; }
        public int SafePasteFilesCopied { get; init; }
        public int TimeReportsFilesCopied { get; init; }
        public int ProjectLineCounterFilesCopied { get; init; }
        public int TaskPanelFilesCopied { get; init; }
        public int AlarmsFilesCopied { get; init; }
        public int StandupFilesCopied { get; init; }
        public int MessageOverlayFilesCopied { get; init; }
        public int MidiCustomSongsFilesCopied { get; init; }
        public int ImageFilesCopied { get; init; }

        /// <summary>Suffixed to “Cloud copying additional files - …” in <see cref="AppendAppLog"/>.</summary>
        public string ToLogLine()
        {
            static string CopiesPhrase(int files) =>
                files switch
                {
                    0 => "0 files",
                    1 => "1 file",
                    _ => $"{files} files"
                };

            const string TagSettings = "Settings";
            const string TagLog = "Log";
            const string TagHeartbeat = "Heartbeat logs";
            const string TagTodoItems = "Todo Items";
            const string TagStateConfig = "UI state";
            const string TagSessionState = "Session state";
            const string TagSafePaste = "Safe Paste";
            const string TagTimeReports = "Time Reports";
            const string TagProjectLineCounter = "Project Line Counter";
            const string TagTaskPanel = "Task panel";
            const string TagAlarms = "Alarms";
            const string TagStandup = "Standup";
            const string TagMessageOverlay = "Message overlay";
            const string TagMidiPaths = "MIDI Custom Songs Paths";
            const string TagImages = "Images";

            var included = new List<string>(15);
            if (IncludeSettings)
                included.Add($"{TagSettings}: {CopiesPhrase(SettingsFilesCopied)}");
            if (IncludeAppLog)
                included.Add($"{TagLog}: {CopiesPhrase(AppLogFilesCopied)}");
            if (IncludeHeartbeat)
                included.Add($"{TagHeartbeat}: {CopiesPhrase(HeartbeatFilesCopied)}");
            if (IncludeTodoItems)
                included.Add($"{TagTodoItems}: {CopiesPhrase(TodoItemsFilesCopied)}");
            if (IncludeStateConfig)
                included.Add($"{TagStateConfig}: {CopiesPhrase(StateConfigFilesCopied)}");
            if (IncludeSessionState)
                included.Add($"{TagSessionState}: {CopiesPhrase(SessionStateFilesCopied)}");
            if (IncludeSafePaste)
                included.Add($"{TagSafePaste}: {CopiesPhrase(SafePasteFilesCopied)}");
            if (IncludeTimeReports)
                included.Add($"{TagTimeReports}: {CopiesPhrase(TimeReportsFilesCopied)}");
            if (IncludeProjectLineCounter)
                included.Add($"{TagProjectLineCounter}: {CopiesPhrase(ProjectLineCounterFilesCopied)}");
            if (IncludeTaskPanel)
                included.Add($"{TagTaskPanel}: {CopiesPhrase(TaskPanelFilesCopied)}");
            if (IncludeAlarms)
                included.Add($"{TagAlarms}: {CopiesPhrase(AlarmsFilesCopied)}");
            if (IncludeStandup)
                included.Add($"{TagStandup}: {CopiesPhrase(StandupFilesCopied)}");
            if (IncludeMessageOverlay)
                included.Add($"{TagMessageOverlay}: {CopiesPhrase(MessageOverlayFilesCopied)}");
            if (IncludeMidiCustomSongs)
                included.Add($"{TagMidiPaths}: {CopiesPhrase(MidiCustomSongsFilesCopied)}");
            if (IncludeImages)
                included.Add($"{TagImages}: {CopiesPhrase(ImageFilesCopied)}");

            var excluded = new List<string>(14);
            if (!IncludeSettings)
                excluded.Add(TagSettings);
            if (!IncludeAppLog)
                excluded.Add(TagLog);
            if (!IncludeHeartbeat)
                excluded.Add(TagHeartbeat);
            if (!IncludeTodoItems)
                excluded.Add(TagTodoItems);
            if (!IncludeStateConfig)
                excluded.Add(TagStateConfig);
            if (!IncludeSessionState)
                excluded.Add(TagSessionState);
            if (!IncludeSafePaste)
                excluded.Add(TagSafePaste);
            if (!IncludeTimeReports)
                excluded.Add(TagTimeReports);
            if (!IncludeProjectLineCounter)
                excluded.Add(TagProjectLineCounter);
            if (!IncludeTaskPanel)
                excluded.Add(TagTaskPanel);
            if (!IncludeAlarms)
                excluded.Add(TagAlarms);
            if (!IncludeStandup)
                excluded.Add(TagStandup);
            if (!IncludeMessageOverlay)
                excluded.Add(TagMessageOverlay);
            if (!IncludeMidiCustomSongs)
                excluded.Add(TagMidiPaths);
            if (!IncludeImages)
                excluded.Add(TagImages);

            var copiedPart = included.Count > 0
                ? string.Join("; ", included)
                : "(no categories enabled)";

            if (excluded.Count == 0)
                return copiedPart + ".";

            return copiedPart + ". Excluding: " + string.Join("; ", excluded) + ".";
        }
    }

    /// <summary>Sidecar files beside tab bundles: settings, log, heartbeat, todos, etc. (migration + cloud).</summary>
    private AdditionalBackupArtifactsSummary CopySelectedAdditionalBackupArtifacts(string fromFolder, string toFolder)
    {
        var settingsCopied = _backupAdditionalIncludeSettingsFile && CopySettingsFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var appCopied = _backupAdditionalIncludeAppLog && _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, AppLogFileName)
            ? 1
            : 0;
        var heartbeatCopied = _backupAdditionalIncludeHeartbeatLogs
            ? CopyHeartbeatLogFilesBetweenFolders(fromFolder, toFolder)
            : 0;
        var todoCopied = _backupAdditionalIncludeTodoItems && CopyTodoItemsFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var stateConfigCopied = _backupAdditionalIncludeStateConfig && CopyStateConfigFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var sessionStateCopied = _backupAdditionalIncludeSessionState && CopySessionStateFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var safePasteCopied = 0;
        if (_backupAdditionalIncludeSafePaste)
        {
            if (CopySafePasteDataFileToBackupFolder(fromFolder, toFolder))
                safePasteCopied++;
            if (CopySafePasteKeysFileToBackupFolder(fromFolder, toFolder))
                safePasteCopied++;
        }
        var timeReportsCopied = _backupAdditionalIncludeTimeReports && CopyTimeReportsFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var projectLineCounterCopied = _backupAdditionalIncludeProjectLineCounter
            && CopyProjectLineCounterStateFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var taskPanelCopied = _backupAdditionalIncludeTaskPanel
            && CopyTaskPanelPluginStateFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var alarmsCopied = _backupAdditionalIncludeAlarms
            && CopyAlarmsPluginStateFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var standupCopied = _backupAdditionalIncludeStandup
            && CopyStandupPluginStateFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var messageOverlayCopied = _backupAdditionalIncludeMessageOverlay
            && CopyMessageOverlayPluginStateFileToBackupFolder(fromFolder, toFolder)
            ? 1
            : 0;
        var midiCopied = _backupAdditionalIncludeMidiCustomSongs
            && _settingsService.CopyFileIfExistsIfNewer(fromFolder, toFolder, MidiCustomSongsFileName)
            ? 1
            : 0;
        var imageCopied = _backupAdditionalIncludeImages
            ? CopyImageFolderToBackupFolder(fromFolder, toFolder)
            : 0;

        return new AdditionalBackupArtifactsSummary
        {
            IncludeSettings = _backupAdditionalIncludeSettingsFile,
            IncludeAppLog = _backupAdditionalIncludeAppLog,
            IncludeHeartbeat = _backupAdditionalIncludeHeartbeatLogs,
            IncludeTodoItems = _backupAdditionalIncludeTodoItems,
            IncludeStateConfig = _backupAdditionalIncludeStateConfig,
            IncludeSessionState = _backupAdditionalIncludeSessionState,
            IncludeSafePaste = _backupAdditionalIncludeSafePaste,
            IncludeTimeReports = _backupAdditionalIncludeTimeReports,
            IncludeProjectLineCounter = _backupAdditionalIncludeProjectLineCounter,
            IncludeTaskPanel = _backupAdditionalIncludeTaskPanel,
            IncludeAlarms = _backupAdditionalIncludeAlarms,
            IncludeStandup = _backupAdditionalIncludeStandup,
            IncludeMessageOverlay = _backupAdditionalIncludeMessageOverlay,
            IncludeMidiCustomSongs = _backupAdditionalIncludeMidiCustomSongs,
            IncludeImages = _backupAdditionalIncludeImages,
            SettingsFilesCopied = settingsCopied,
            AppLogFilesCopied = appCopied,
            HeartbeatFilesCopied = heartbeatCopied,
            TodoItemsFilesCopied = todoCopied,
            StateConfigFilesCopied = stateConfigCopied,
            SessionStateFilesCopied = sessionStateCopied,
            SafePasteFilesCopied = safePasteCopied,
            TimeReportsFilesCopied = timeReportsCopied,
            ProjectLineCounterFilesCopied = projectLineCounterCopied,
            TaskPanelFilesCopied = taskPanelCopied,
            AlarmsFilesCopied = alarmsCopied,
            StandupFilesCopied = standupCopied,
            MessageOverlayFilesCopied = messageOverlayCopied,
            MidiCustomSongsFilesCopied = midiCopied,
            ImageFilesCopied = imageCopied
        };
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

        void RefreshUserColorPreview()
        {
            if (list.SelectedItem is not UserProfile)
                return;
            if (TryParseColor(cmbUserColor.Text ?? string.Empty, out var color))
                colorPreview.Background = new SolidColorBrush(color);
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

        cmbUserColor.SelectionChanged += (_, _) => RefreshUserColorPreview();
        cmbUserColor.LostFocus += (_, _) => RefreshUserColorPreview();

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
            cmbUserColor.ApplyTemplate();
            if (cmbUserColor.Template.FindName("PART_EditableTextBox", cmbUserColor) is TextBox editableColorText)
                editableColorText.TextChanged += (_, _) => RefreshUserColorPreview();
        };

        if (dlg.ShowDialog() != true)
            return;

        _users = NormalizeUsers(users);
        foreach (var doc in _docs.Values)
            RedrawHighlight(doc);
        SaveWindowSettings();
    }

    private static int NormalizeMidiPlayerVolumePercent(int? value)
    {
        if (value is >= 0 and <= 100)
            return value.Value;
        return DefaultMidiPlayerVolumePercent;
    }
}
