using System.IO;

namespace MiniPlayer.Services;

/// <summary>
/// Build flavor and folders. The Microsoft Store build (<c>-p:StoreBuild=true</c>) runs inside an
/// MSIX package: Windows redirects its AppData writes to a private package folder that other
/// programs (Explorer, OBS) don't see, so files meant for the user live in Documents there.
/// </summary>
public static class AppInfo
{
    /// <summary>Microsoft Store (MSIX) build.</summary>
    public static bool IsStore { get; } =
#if STORE
        true;
#else
        false;
#endif

    static readonly string AppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer");

    static readonly string Documents = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MiniPlayer");

    /// <summary>Settings, history, covers (only the app reads them).</summary>
    public static string DataDir => AppData;

    /// <summary>Where <see cref="DataDir"/> really is on disk, for "open folder" buttons.</summary>
    public static string DataDirOnDisk
    {
        get
        {
#if STORE
            try
            {
                return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Roaming", "MiniPlayer");
            }
            catch
            {
                return AppData;
            }
#else
            return AppData;
#endif
        }
    }

    /// <summary>Theme files the user creates or downloads.</summary>
    public static string ThemesDir => Path.Combine(IsStore ? Documents : AppData, IsStore ? "Temas" : "themes");

    /// <summary>Default "now playing" folder (OBS must be able to read it).</summary>
    public static string NowPlayingDir => Path.Combine(IsStore ? Documents : AppData, "nowplaying");
}
