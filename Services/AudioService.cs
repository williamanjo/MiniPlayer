using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MiniPlayer.Services;

/// <summary>
/// Per-app volume through Windows Core Audio (the same sessions the Volume Mixer shows).
/// Browsers play audio from a helper process (chrome.exe, msedge.exe...), so sessions are
/// matched by process name rather than by PID.
/// </summary>
public static class AudioService
{
    /// <summary>Process name (without .exe) that owns the audio for a media session AUMID.</summary>
    public static string? ProcessNameFor(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return null;
        var id = aumid.ToLowerInvariant();
        if (id.Contains("msedge")) return "msedge";
        if (id.Contains("chrome")) return "chrome";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "firefox";
        if (id.Contains("brave")) return "brave";
        if (id.Contains("opera")) return "opera";
        if (id.Contains("vivaldi")) return "vivaldi";
        // Desktop apps: "Spotify.exe", "C:\...\foo.exe", "Company.App_xyz!App"
        var name = aumid.Split('!')[0].Split('\\', '/').Last();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Split('.', '_')[0].ToLowerInvariant();
    }

    // Fallback mute (IChannelAudioVolume has none): remembered level per process.
    static readonly Dictionary<string, float> MutedLevels = [];

    /// <summary>Current volume (0..1) and mute of the app, or null if it has no audio session.</summary>
    public static (float Volume, bool Muted)? Get(string processName)
    {
        var sessions = Sessions(processName);
        if (sessions.Count == 0) return null;
        var volume = sessions.Max(s => s.Volume);
        var muted = sessions.All(s => s.Muted) || (volume <= 0.001f && MutedLevels.ContainsKey(processName));
        return (MutedLevels.TryGetValue(processName, out var saved) && muted ? saved : volume, muted);
    }

    /// <summary>Changes the app volume by <paramref name="delta"/>; returns the new level.</summary>
    public static (float Volume, bool Muted)? Change(string processName, float delta)
    {
        var current = Get(processName);
        if (current is null) return null;
        var target = Math.Clamp(current.Value.Volume + delta, 0f, 1f);
        MutedLevels.Remove(processName);
        foreach (var s in Sessions(processName))
        {
            s.Volume = target;
            s.Muted = false;
        }
        return (target, false);
    }

    /// <summary>Sets the app volume to an absolute level (0..1).</summary>
    public static void SetVolume(string processName, float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        foreach (var s in Sessions(processName)) s.Volume = level;
    }

    /// <summary>
    /// Peak meters of the app's audio sessions (what the Volume Mixer bars show). Cheap to poll;
    /// resolve once and reuse, since matching sessions to processes is the expensive part.
    /// </summary>
    public static List<IAudioMeterInformation> Meters(string processName) =>
        [.. SessionControls(processName).OfType<IAudioMeterInformation>()];

    /// <summary>Highest current peak (0..1) of the given meters; null if they went away.</summary>
    public static float? Peak(IReadOnlyList<IAudioMeterInformation> meters)
    {
        try
        {
            var peak = 0f;
            foreach (var m in meters)
            {
                m.GetPeakValue(out var value);
                peak = Math.Max(peak, value);
            }
            return peak;
        }
        catch
        {
            return null;
        }
    }

    public static (float Volume, bool Muted)? ToggleMute(string processName)
    {
        var current = Get(processName);
        if (current is null) return null;
        var mute = !current.Value.Muted;
        foreach (var s in Sessions(processName))
        {
            if (s.SupportsMute)
            {
                s.Muted = mute;
            }
            else
            {
                s.Volume = mute ? 0 : current.Value.Volume;
            }
        }
        if (mute) MutedLevels[processName] = current.Value.Volume;
        else MutedLevels.Remove(processName);
        return (current.Value.Volume, mute);
    }

    /// <summary>
    /// One audio session's volume. Prefers ISimpleAudioVolume; some Windows builds only expose
    /// IChannelAudioVolume on sessions of other processes, so fall back to setting every channel.
    /// </summary>
    sealed class SessionVolume(ISimpleAudioVolume? simple, IChannelAudioVolume? channels)
    {
        static Guid _ctx = Guid.Empty;

        public bool SupportsMute => simple is not null;

        public float Volume
        {
            get
            {
                if (simple is not null)
                {
                    simple.GetMasterVolume(out var level);
                    return level;
                }
                channels!.GetChannelCount(out var count);
                var max = 0f;
                for (uint i = 0; i < count; i++)
                {
                    channels.GetChannelVolume(i, out var v);
                    max = Math.Max(max, v);
                }
                return max;
            }
            set
            {
                if (simple is not null)
                {
                    simple.SetMasterVolume(value, ref _ctx);
                    return;
                }
                channels!.GetChannelCount(out var count);
                for (uint i = 0; i < count; i++) channels.SetChannelVolume(i, value, ref _ctx);
            }
        }

        public bool Muted
        {
            get
            {
                if (simple is null) return false;
                simple.GetMute(out var m);
                return m;
            }
            set => simple?.SetMute(value, ref _ctx);
        }
    }

    static List<SessionVolume> Sessions(string processName)
    {
        var result = new List<SessionVolume>();
        foreach (var control in SessionControls(processName))
        {
            var simple = control as ISimpleAudioVolume;
            var channels = simple is null ? control as IChannelAudioVolume : null;
            if (simple is not null || channels is not null) result.Add(new SessionVolume(simple, channels));
        }
        return result;
    }

    /// <summary>An app producing sound (for "lower the music when another app plays").</summary>
    public sealed record AudioApp(string ProcessName, uint ProcessId, IAudioMeterInformation Meter);

    /// <summary>Every audio session on all output devices, with its process ("system" for Windows sounds).</summary>
    public static List<AudioApp> AllSessions()
    {
        var result = new List<AudioApp>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.EnumAudioEndpoints(EDataFlow.Render, DEVICE_STATE_ACTIVE, out var devices);
            devices.GetCount(out var deviceCount);
            for (var d = 0; d < deviceCount; d++)
            {
                devices.Item(d, out var device);
                var iid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var managerObj);
                ((IAudioSessionManager2)managerObj).GetSessionEnumerator(out var sessions);
                sessions.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    sessions.GetSession(i, out var control);
                    if (control is not IAudioSessionControl2 control2 || control is not IAudioMeterInformation meter) continue;
                    control2.GetProcessId(out var pid);
                    var name = control2.IsSystemSoundsSession() == 0 ? "system" : ProcessName(pid);
                    if (name is not null) result.Add(new AudioApp(name, pid, meter));
                }
            }
        }
        catch
        {
            // Devices changed mid-enumeration; the caller refreshes again soon.
        }
        return result;
    }

    static string? ProcessName(uint pid)
    {
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Audio session objects (all render devices) owned by the process.</summary>
    static List<object> SessionControls(string processName)
    {
        var result = new List<object>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.EnumAudioEndpoints(EDataFlow.Render, DEVICE_STATE_ACTIVE, out var devices);
            devices.GetCount(out var deviceCount);
            for (var d = 0; d < deviceCount; d++)
            {
                devices.Item(d, out var device);
                var iid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var managerObj);
                var manager = (IAudioSessionManager2)managerObj;
                manager.GetSessionEnumerator(out var sessions);
                sessions.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    sessions.GetSession(i, out var control);
                    if (control is not IAudioSessionControl2 control2) continue;
                    control2.GetProcessId(out var pid);
                    if (pid == 0 || !MatchesProcess(pid, processName)) continue;
                    result.Add(control);
                }
            }
        }
        catch
        {
            // Audio devices can disappear at any time; treat as "no session".
        }
        return result;
    }

    static bool MatchesProcess(uint pid, string processName)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    #region Core Audio COM interop

    const uint DEVICE_STATE_ACTIVE = 1;
    const uint CLSCTX_ALL = 23;

    enum EDataFlow { Render, Capture, All }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        // Remaining methods unused.
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid ctx);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
        /// <summary>S_OK (0) for the "System sounds" session, S_FALSE (1) otherwise.</summary>
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F7AFB1A3DD9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IChannelAudioVolume
    {
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetChannelVolume(uint index, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolume(uint index, out float level);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    #endregion
}
