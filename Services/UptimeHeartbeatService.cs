using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Noted.Services;

public static class UptimeHeartbeatService
{
    public const string FileNamePrefix = "uptime-heartbeat-";
    private static readonly Mutex HeartbeatFileMutex = new(initiallyOwned: false, name: @"Global\Noted.UptimeHeartbeat");

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    public static DateTimeOffset GetNextHeartbeatAtLocal(DateTimeOffset nowLocal, int heartbeatSeconds, DateTimeOffset lastHeartbeatAtLocal)
    {
        var now = nowLocal.LocalDateTime;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Local);
        var elapsedSeconds = (now - hourStart).TotalSeconds;
        var slotsPassed = (int)Math.Floor(elapsedSeconds / heartbeatSeconds);
        var nextLocal = hourStart.AddSeconds((slotsPassed + 1) * heartbeatSeconds);
        var next = new DateTimeOffset(nextLocal);
        if (lastHeartbeatAtLocal != DateTimeOffset.MinValue && next <= lastHeartbeatAtLocal)
            return lastHeartbeatAtLocal.AddSeconds(heartbeatSeconds);

        return next;
    }

    public static void AppendHeartbeatTimestamp(
        string backupFolder,
        DateTimeOffset timestampLocal,
        Func<string> captureAudioSummary,
        string source,
        ref DateTimeOffset lastHeartbeatAtLocal,
        bool markAsStartup = false,
        bool markAsShutdown = false)
    {
        if (string.IsNullOrWhiteSpace(backupFolder))
            return;

        Directory.CreateDirectory(backupFolder);
        var path = GetHeartbeatFilePath(backupFolder, timestampLocal);
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = HeartbeatFileMutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                // Another process died while holding the mutex; treat lock as acquired.
                lockTaken = true;
            }

            if (!lockTaken)
                return;

            var lastTimestamp = ReadLastHeartbeatTimestamp(path);
            var latestKnown = lastHeartbeatAtLocal;
            if (lastTimestamp.HasValue && (latestKnown == DateTimeOffset.MinValue || lastTimestamp.Value > latestKnown))
                latestKnown = lastTimestamp.Value;

            if (latestKnown != DateTimeOffset.MinValue && timestampLocal <= latestKnown)
                return;

            var timestamp = timestampLocal.ToString("O", CultureInfo.InvariantCulture);
            var writtenAtLocal = DateTimeOffset.Now;
            var isDelayed = (writtenAtLocal - timestampLocal) > TimeSpan.FromMinutes(1);
            var idleSeconds = GetSystemIdleSeconds();
            var audioSummary = captureAudioSummary();
            var sourceTag = NormalizeSource(source);
            var delayedFlag = isDelayed ? ",delayed=1" : string.Empty;
            var eventFlag = markAsShutdown ? ",stop=1" : markAsStartup ? ",start=1" : string.Empty;
            File.AppendAllText(path, $"{timestamp},idle={idleSeconds}s,audio={audioSummary},source={sourceTag}{delayedFlag}{eventFlag}{Environment.NewLine}");
            lastHeartbeatAtLocal = timestampLocal;
        }
        finally
        {
            if (lockTaken)
                HeartbeatFileMutex.ReleaseMutex();
        }
    }

    public static string GetHeartbeatFilePath(string backupFolder, DateTimeOffset timestampLocal)
    {
        var fileName = $"{FileNamePrefix}{timestampLocal:yyyy-MM}.log";
        return Path.Combine(backupFolder, fileName);
    }

    public static DateTimeOffset? ReadLastHeartbeatTimestamp(string path)
    {
        if (!File.Exists(path))
            return null;

        var lastLine = File.ReadLines(path)
            .Select(line => line.Trim())
            .LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(lastLine))
            return null;

        var timestampPart = lastLine.Split(',', 2)[0].Trim();
        if (!DateTimeOffset.TryParseExact(timestampPart, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return null;

        return parsed;
    }

    public static long GetSystemIdleSeconds()
    {
        try
        {
            LastInputInfo info = new()
            {
                cbSize = (uint)Marshal.SizeOf<LastInputInfo>()
            };

            if (!GetLastInputInfo(ref info))
                return -1;

            var nowTick = unchecked((uint)Environment.TickCount);
            var elapsedMs = unchecked(nowTick - info.dwTime);

            return elapsedMs / 1000;
        }
        catch
        {
            return -1;
        }
    }

    private static string NormalizeSource(string source)
    {
        if (string.Equals(source, "n", StringComparison.OrdinalIgnoreCase))
            return "n";
        if (string.Equals(source, "s", StringComparison.OrdinalIgnoreCase))
            return "s";
        return "u";
    }
}
