using System.Reflection;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace MiniPlayer.Services;

/// <summary>
/// Checks GitHub Releases (Velopack feed) for a newer version. Only active in installed copies;
/// a dev build or loose exe has nothing to update.
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/williamanjo/MiniPlayer";
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    readonly UpdateManager _manager;
    readonly DispatcherTimer _timer = new() { Interval = CheckInterval };
    UpdateInfo? _pending;
    /// <summary>In-flight check, shared by concurrent callers (startup check vs toast click).</summary>
    Task<UpdateInfo?>? _check;

    public UpdateService()
    {
        // MINIPLAYER_UPDATE_SOURCE: local folder or URL, used to test updates without publishing.
        var source = Environment.GetEnvironmentVariable("MINIPLAYER_UPDATE_SOURCE");
        _manager = string.IsNullOrWhiteSpace(source)
            ? new UpdateManager(new GithubSource(RepoUrl, null, false))
            : new UpdateManager(source);
        _timer.Tick += async (_, _) => await CheckAsync(manual: false, atStartup: false);
    }

    /// <summary>Raised on the UI thread when a new version is found (version, found at startup).</summary>
    public event Action<string, bool>? UpdateAvailable;

    /// <summary>True for installer (Velopack) copies; the Store build updates through the Store.</summary>
    public bool IsInstalled => !AppInfo.IsStore && _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    /// <summary>Version waiting to be installed, if any.</summary>
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Version already downloaded; Velopack applies it on the next start.</summary>
    public string? DownloadedVersion { get; private set; }

    /// <param name="checkNow">False when a toast click launched the app: the click drives the update.</param>
    public void Start(bool checkNow = true)
    {
        if (!IsInstalled) return;
        if (checkNow) _ = CheckAsync(manual: false, atStartup: true);
        _timer.Start();
    }

    /// <returns>New version, or null when up to date / not installed / offline.</returns>
    public async Task<string?> CheckAsync(bool manual, bool atStartup = false)
    {
        if (!IsInstalled) return null;
        var owner = _check is null;
        _check ??= _manager.CheckForUpdatesAsync();
        UpdateInfo? info;
        try
        {
            info = await _check;
        }
        catch
        {
            if (manual) throw;
            return null; // offline: try again on the next tick
        }
        finally
        {
            if (owner) _check = null;
        }

        if (info is null) return null;
        var isNew = AvailableVersion != info.TargetFullRelease.Version.ToString();
        _pending = info;
        if (isNew && !manual) UpdateAvailable?.Invoke(AvailableVersion!, atStartup);
        return AvailableVersion;
    }

    /// <summary>Downloads the pending version without installing it.</summary>
    public async Task DownloadAsync(Action<int>? progress = null)
    {
        if (_pending is null) return;
        await _manager.DownloadUpdatesAsync(_pending, progress);
        DownloadedVersion = AvailableVersion;
    }

    /// <summary>Downloads (if needed) the pending version, then restarts into it.</summary>
    public async Task DownloadAndRestartAsync(Action<int> progress, Action beforeRestart)
    {
        if (_pending is null) return;
        if (DownloadedVersion != AvailableVersion) await DownloadAsync(progress);
        beforeRestart();
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
