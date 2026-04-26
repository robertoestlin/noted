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

        var currentProcessId = (uint)Environment.ProcessId;
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
                    ReleaseComObject(sessionControl2);
                    ReleaseComObject(sessionControl);
                }
            }
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(defaultDevice);
            ReleaseComObject(deviceEnumerator);
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
                    ReleaseComObject(meter);
                    ReleaseComObject(sessionControl2);
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
        eMultimedia = 1
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out object devices);

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
