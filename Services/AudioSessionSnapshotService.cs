using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Noted.Services;

public sealed class AudioSessionSnapshotService
{
    private const float ActivePeakThreshold = 0.0001f;
    private const int ClsCtxAll = 23; // InprocServer | InprocHandler | LocalServer

    public void TrySetCurrentProcessSessionDisplayName(string displayName)
    {
        try
        {
            SetCurrentProcessSessionDisplayName(displayName);
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Sets linear master volume (0–1) on every **WASAPI render session** owned
    /// by this **process** on each output device — the same per-app level as in
    /// Volume Mixer for "Noted". This is not device-wide / master-knob volume.
    /// MCI MIDI must be routed through a session for this process for it to take
    /// effect; we enumerate **all** active render endpoints because MIDI often
    /// does not use the default role endpoint alone.
    /// </summary>
    public void TrySetCurrentProcessSessionsMasterVolume(float level)
    {
        try
        {
            SetCurrentProcessSessionsMasterVolume(level);
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Reads the highest session master level (0–1) among this process's render
    /// sessions, matching what the Volume Mixer reflects better than JSON settings.
    /// </summary>
    public bool TryGetCurrentProcessSessionsMasterVolume(out float level)
    {
        level = 1f;
        try
        {
            var deviceEnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
            IMMDeviceEnumerator? deviceEnumerator = null;
            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(deviceEnumeratorType)!;
                var maxLinear = -1f;
                var found = false;
                if (TryReadMasterVolumeOnAllActiveRenderEndpoints(deviceEnumerator, ref maxLinear, ref found)
                    && found)
                {
                    level = Math.Clamp(maxLinear, 0f, 1f);
                    return true;
                }

                TryReadMasterVolumeOnDefaultRenderEndpoint(deviceEnumerator, ERole.eMultimedia, ref maxLinear, ref found);
                TryReadMasterVolumeOnDefaultRenderEndpoint(deviceEnumerator, ERole.eConsole, ref maxLinear, ref found);
                TryReadMasterVolumeOnDefaultRenderEndpoint(deviceEnumerator, ERole.eCommunications, ref maxLinear, ref found);
                if (!found)
                    return false;
                level = Math.Clamp(maxLinear, 0f, 1f);
                return true;
            }
            finally
            {
                ReleaseComObject(deviceEnumerator);
            }
        }
        catch
        {
            return false;
        }
    }

    public string CaptureOutputAudioSummary()
    {
        try
        {
            var activeSessions = CaptureActiveSessions();
            if (activeSessions.Count == 0)
                return "0";

            var sessionSummary = string.Join(",", activeSessions
                .OrderByDescending(item => item.Peak)
                .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{SanitizeKey(item.ProcessName)}={item.Peak.ToString("0.000", CultureInfo.InvariantCulture)}"));

            return $"{{{sessionSummary}}}";
        }
        catch
        {
            // Best effort diagnostics only.
            return "0";
        }
    }

    private static void SetCurrentProcessSessionDisplayName(string displayName)
    {
        var target = (displayName ?? string.Empty).Trim();
        if (target.Length == 0)
            return;

        var deviceEnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
        IMMDeviceEnumerator? deviceEnumerator = null;
        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(deviceEnumeratorType)!;
            if (TryRenameOurProcessSessionsOnAllActiveRenderEndpoints(deviceEnumerator, target))
                return;

            TryRenameOurProcessSessionsOnDefaultRenderEndpoint(deviceEnumerator, ERole.eMultimedia, target);
            TryRenameOurProcessSessionsOnDefaultRenderEndpoint(deviceEnumerator, ERole.eConsole, target);
            TryRenameOurProcessSessionsOnDefaultRenderEndpoint(deviceEnumerator, ERole.eCommunications, target);
        }
        finally
        {
            ReleaseComObject(deviceEnumerator);
        }
    }

    private static bool TryRenameOurProcessSessionsOnAllActiveRenderEndpoints(
        IMMDeviceEnumerator enumerator,
        string target)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceStateActive, out collection) != 0
                || collection is null)
            {
                return false;
            }

            if (collection.GetCount(out var count) != 0 || count <= 0)
                return false;

            for (var index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(index, out device) != 0 || device is null)
                        continue;
                    RenameOurProcessSessionsOnDevice(device, target);
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(collection);
        }
    }

    private static void TryRenameOurProcessSessionsOnDefaultRenderEndpoint(
        IMMDeviceEnumerator enumerator,
        ERole role,
        string target)
    {
        IMMDevice? defaultDevice = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out defaultDevice) != 0
                || defaultDevice is null)
            {
                return;
            }

            RenameOurProcessSessionsOnDevice(defaultDevice, target);
        }
        catch
        {
            // Role or device may be unavailable — ignore.
        }
        finally
        {
            ReleaseComObject(defaultDevice);
        }
    }

    private static void RenameOurProcessSessionsOnDevice(IMMDevice device, string target)
    {
        var currentProcessId = (uint)Environment.ProcessId;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            var sessionManagerGuid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref sessionManagerGuid, ClsCtxAll, IntPtr.Zero, out var sessionManagerObj));
            sessionManager = (IAudioSessionManager2)sessionManagerObj;
            Marshal.ThrowExceptionForHR(sessionManager.GetSessionEnumerator(out sessionEnumerator));
            Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var count));

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? sessionControl = null;
                IAudioSessionControl2? sessionControl2 = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(index, out sessionControl));
                    sessionControl2 = (IAudioSessionControl2)sessionControl;
                    Marshal.ThrowExceptionForHR(sessionControl2.GetProcessId(out var processId));
                    if (processId != currentProcessId)
                        continue;

                    if (sessionControl2.GetDisplayName(out var currentDisplayName) == 0
                        && string.Equals(currentDisplayName, target, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var context = Guid.Empty;
                    sessionControl2.SetDisplayName(target, ref context);
                }
                catch
                {
                    // Skip this session and continue with the next.
                }
                finally
                {
                    // sessionControl2 is a cast of the same RCW as sessionControl — do not
                    // ReleaseComObject both or the COM refcount is decremented twice.
                    ReleaseComObject(sessionControl);
                }
            }
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
        }
    }

    private const int DeviceStateActive = 0x1;

    private static void SetCurrentProcessSessionsMasterVolume(float level)
    {
        var linear = level;
        if (linear < 0f)
            linear = 0f;
        else if (linear > 1f)
            linear = 1f;

        var deviceEnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
        IMMDeviceEnumerator? deviceEnumerator = null;
        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(deviceEnumeratorType)!;
            if (TryApplyMasterVolumeOnAllActiveRenderEndpoints(deviceEnumerator, linear))
                return;

            TryApplyMasterVolumeOnDefaultRenderEndpoint(deviceEnumerator, ERole.eMultimedia, linear);
            TryApplyMasterVolumeOnDefaultRenderEndpoint(deviceEnumerator, ERole.eConsole, linear);
            TryApplyMasterVolumeOnDefaultRenderEndpoint(deviceEnumerator, ERole.eCommunications, linear);
        }
        finally
        {
            ReleaseComObject(deviceEnumerator);
        }
    }

    /// <summary>Returns true if at least one active render endpoint was enumerated.</summary>
    private static bool TryApplyMasterVolumeOnAllActiveRenderEndpoints(
        IMMDeviceEnumerator enumerator,
        float linear)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceStateActive, out collection) != 0
                || collection is null)
            {
                return false;
            }

            if (collection.GetCount(out var count) != 0 || count <= 0)
                return false;

            for (var index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(index, out device) != 0 || device is null)
                        continue;
                    ApplyMasterVolumeToOurProcessSessionsOnDevice(device, linear);
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(collection);
        }
    }

    private static void TryApplyMasterVolumeOnDefaultRenderEndpoint(
        IMMDeviceEnumerator enumerator,
        ERole role,
        float linear)
    {
        IMMDevice? defaultDevice = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out defaultDevice) != 0
                || defaultDevice is null)
            {
                return;
            }

            ApplyMasterVolumeToOurProcessSessionsOnDevice(defaultDevice, linear);
        }
        catch
        {
            // Role or device may be unavailable — ignore.
        }
        finally
        {
            ReleaseComObject(defaultDevice);
        }
    }

    private static bool TryReadMasterVolumeOnAllActiveRenderEndpoints(
        IMMDeviceEnumerator enumerator,
        ref float maxLinear,
        ref bool found)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceStateActive, out collection) != 0
                || collection is null)
            {
                return false;
            }

            if (collection.GetCount(out var count) != 0 || count <= 0)
                return false;

            for (var index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(index, out device) != 0 || device is null)
                        continue;
                    ReadMaxMasterVolumeFromOurProcessSessionsOnDevice(device, ref maxLinear, ref found);
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(collection);
        }
    }

    private static void TryReadMasterVolumeOnDefaultRenderEndpoint(
        IMMDeviceEnumerator enumerator,
        ERole role,
        ref float maxLinear,
        ref bool found)
    {
        IMMDevice? defaultDevice = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out defaultDevice) != 0
                || defaultDevice is null)
            {
                return;
            }

            ReadMaxMasterVolumeFromOurProcessSessionsOnDevice(defaultDevice, ref maxLinear, ref found);
        }
        catch
        {
            // Ignore.
        }
        finally
        {
            ReleaseComObject(defaultDevice);
        }
    }

    private static void ReadMaxMasterVolumeFromOurProcessSessionsOnDevice(
        IMMDevice device,
        ref float maxLinear,
        ref bool found)
    {
        var currentProcessId = (uint)Environment.ProcessId;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            var sessionManagerGuid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref sessionManagerGuid, ClsCtxAll, IntPtr.Zero, out var sessionManagerObj));
            sessionManager = (IAudioSessionManager2)sessionManagerObj;
            Marshal.ThrowExceptionForHR(sessionManager.GetSessionEnumerator(out sessionEnumerator));
            Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var count));

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? sessionControl = null;
                IAudioSessionControl2? sessionControl2 = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(index, out sessionControl));
                    sessionControl2 = (IAudioSessionControl2)sessionControl;
                    Marshal.ThrowExceptionForHR(sessionControl2.GetProcessId(out var processId));
                    if (!IsOurNotedSession(sessionControl2, processId, currentProcessId))
                        continue;

                    ISimpleAudioVolume? simpleVolume = null;
                    var simpleVolumeIsSeparateRcw = false;
                    try
                    {
                        simpleVolume = (ISimpleAudioVolume)(object)sessionControl2;
                    }
                    catch
                    {
                        var unknown = Marshal.GetIUnknownForObject(sessionControl2);
                        try
                        {
                            var iid = typeof(ISimpleAudioVolume).GUID;
                            if (Marshal.QueryInterface(unknown, in iid, out var volumePtr) != 0
                                || volumePtr == IntPtr.Zero)
                            {
                                continue;
                            }

                            try
                            {
                                simpleVolume = (ISimpleAudioVolume)Marshal.GetObjectForIUnknown(volumePtr);
                                simpleVolumeIsSeparateRcw = true;
                            }
                            finally
                            {
                                Marshal.Release(volumePtr);
                            }
                        }
                        finally
                        {
                            Marshal.Release(unknown);
                        }
                    }

                    if (simpleVolume is null)
                        continue;

                    simpleVolume.GetMasterVolume(out var linear);
                    if (linear > maxLinear)
                        maxLinear = linear;
                    found = true;

                    if (simpleVolumeIsSeparateRcw)
                        ReleaseComObject(simpleVolume);
                }
                catch
                {
                    // Skip this session and continue with the next.
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
        }
    }

    private static void ApplyMasterVolumeToOurProcessSessionsOnDevice(IMMDevice device, float linear)
    {
        var currentProcessId = (uint)Environment.ProcessId;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            var sessionManagerGuid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref sessionManagerGuid, ClsCtxAll, IntPtr.Zero, out var sessionManagerObj));
            sessionManager = (IAudioSessionManager2)sessionManagerObj;
            Marshal.ThrowExceptionForHR(sessionManager.GetSessionEnumerator(out sessionEnumerator));
            Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var count));

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? sessionControl = null;
                IAudioSessionControl2? sessionControl2 = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(index, out sessionControl));
                    sessionControl2 = (IAudioSessionControl2)sessionControl;
                    Marshal.ThrowExceptionForHR(sessionControl2.GetProcessId(out var processId));
                    if (!IsOurNotedSession(sessionControl2, processId, currentProcessId))
                        continue;

                    var eventContext = Guid.Empty;
                    ISimpleAudioVolume? simpleVolume = null;
                    var simpleVolumeIsSeparateRcw = false;
                    try
                    {
                        simpleVolume = (ISimpleAudioVolume)(object)sessionControl2;
                    }
                    catch
                    {
                        var unknown = Marshal.GetIUnknownForObject(sessionControl2);
                        try
                        {
                            var iid = typeof(ISimpleAudioVolume).GUID;
                            if (Marshal.QueryInterface(unknown, in iid, out var volumePtr) != 0
                                || volumePtr == IntPtr.Zero)
                            {
                                continue;
                            }

                            try
                            {
                                simpleVolume = (ISimpleAudioVolume)Marshal.GetObjectForIUnknown(volumePtr);
                                simpleVolumeIsSeparateRcw = true;
                            }
                            finally
                            {
                                Marshal.Release(volumePtr);
                            }
                        }
                        finally
                        {
                            Marshal.Release(unknown);
                        }
                    }

                    if (simpleVolume is null)
                        continue;

                    if (linear > 0f)
                    {
                        try
                        {
                            simpleVolume.SetMute(false, ref eventContext);
                        }
                        catch
                        {
                            // Best effort — still try master volume.
                        }
                    }

                    simpleVolume.SetMasterVolume(linear, ref eventContext);

                    if (simpleVolumeIsSeparateRcw)
                        ReleaseComObject(simpleVolume);
                }
                catch
                {
                    // Skip this session and continue with the next.
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
        }
    }

    private static List<AudioSessionPeak> CaptureActiveSessions()
    {
        var sessions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? defaultDevice = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            var deviceEnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(deviceEnumeratorType)!;
            Marshal.ThrowExceptionForHR(deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out defaultDevice));
            var sessionManagerGuid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(defaultDevice.Activate(ref sessionManagerGuid, ClsCtxAll, IntPtr.Zero, out var sessionManagerObj));
            sessionManager = (IAudioSessionManager2)sessionManagerObj;
            Marshal.ThrowExceptionForHR(sessionManager.GetSessionEnumerator(out sessionEnumerator));
            Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var count));

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? sessionControl = null;
                IAudioSessionControl2? sessionControl2 = null;
                IAudioMeterInformation? meter = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(index, out sessionControl));
                    sessionControl2 = (IAudioSessionControl2)sessionControl;
                    meter = (IAudioMeterInformation)sessionControl;

                    Marshal.ThrowExceptionForHR(meter.GetPeakValue(out var peak));
                    if (peak <= ActivePeakThreshold)
                        continue;

                    Marshal.ThrowExceptionForHR(sessionControl2.GetProcessId(out var processId));
                    var processName = TryGetProcessName(processId);
                    if (!sessions.TryGetValue(processName, out var currentPeak) || peak > currentPeak)
                        sessions[processName] = peak;
                }
                catch
                {
                    // Skip this session and continue with the next.
                }
                finally
                {
                    // meter and sessionControl2 are casts of the same RCW as sessionControl.
                    ReleaseComObject(sessionControl);
                }
            }

            return sessions.Select(item => new AudioSessionPeak(item.Key, item.Value)).ToList();
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(defaultDevice);
            ReleaseComObject(deviceEnumerator);
        }
    }

    private static string TryGetProcessName(uint processId)
    {
        if (processId == 0)
            return "SystemSounds";

        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (string.IsNullOrWhiteSpace(process.ProcessName))
                return $"pid-{processId}";

            return process.ProcessName;
        }
        catch
        {
            return $"pid-{processId}";
        }
    }

    private static string SanitizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        Span<char> forbidden = stackalloc[] { ',', '|', '=' };
        var sanitized = value.Trim();
        foreach (var ch in forbidden)
            sanitized = sanitized.Replace(ch, '_');

        return sanitized;
    }

    // MCI MIDI playback can register its render session under the audio service's
    // PID rather than ours. We tag those sessions with display name "Noted" via
    // SetDisplayName, so accept either a PID match or a name match here.
    private static bool IsOurNotedSession(IAudioSessionControl2 sessionControl2, uint sessionProcessId, uint currentProcessId)
    {
        if (sessionProcessId == currentProcessId)
            return true;
        if (sessionControl2.GetDisplayName(out var displayName) != 0)
            return false;
        return string.Equals(displayName, "Noted", StringComparison.Ordinal);
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.ReleaseComObject(comObject);
    }

    private readonly record struct AudioSessionPeak(string ProcessName, float Peak);

    private enum EDataFlow
    {
        eRender = 0
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B2E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int pcDevices);

        [PreserveSig]
        int Item(int nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(int storageAccessMode, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out IntPtr audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

        [PreserveSig]
        int RegisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

        [PreserveSig]
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl sessionControl);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr events);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr events);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr events);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr events);

        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);

        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);

        [PreserveSig]
        int GetProcessId(out uint processId);

        [PreserveSig]
        int IsSystemSoundsSession();

        [PreserveSig]
        int SetDuckingPreference(bool shouldDuckingOptOut);
    }

    [ComImport]
    [Guid("87CE5498-4680-4E82-9C7A-C98C566B364A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid eventContext);

        void GetMasterVolume(out float level);

        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);

        [PreserveSig]
        int GetMeteringChannelCount(out int channelCount);

        [PreserveSig]
        int GetChannelsPeakValues(int channelCount, out float peakValues);

        [PreserveSig]
        int QueryHardwareSupport(out int hardwareSupportMask);
    }
}
