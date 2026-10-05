using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MiniPlayer.Localization;

namespace MiniPlayer.Services;

/// <summary>
/// Backup of all settings in one .zip: settings.json plus the custom themes (and their images),
/// to move the setup to another PC or restore it after a reinstall.
/// </summary>
public static class BackupService
{
    const string SettingsEntry = "settings.json";
    const string ThemesPrefix = "themes/";

    public static void Export(SettingsService settings, string zipPath)
    {
        settings.Save();
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        using (var writer = new StreamWriter(zip.CreateEntry(SettingsEntry).Open()))
            writer.Write(settings.ToJson());

        if (Directory.Exists(AppInfo.ThemesDir))
            foreach (var file in Directory.GetFiles(AppInfo.ThemesDir))
                zip.CreateEntryFromFile(file, ThemesPrefix + Path.GetFileName(file));
    }

    /// <summary>Restores the backup: themes are added (same names replaced), settings replaced.</summary>
    public static void Import(SettingsService settings, string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(SettingsEntry) ?? throw new InvalidDataException(Loc.T("backup_invalid"));
        AppSettings data;
        using (var reader = new StreamReader(entry.Open()))
        {
            try { data = SettingsService.Parse(reader.ReadToEnd()); }
            catch (JsonException) { throw new InvalidDataException(Loc.T("backup_invalid")); }
        }

        ThemeService.EnsureUserDir();
        foreach (var theme in zip.Entries)
        {
            if (!theme.FullName.StartsWith(ThemesPrefix) || theme.Name.Length == 0) continue;
            // flat folder only: the file name alone, never a path from the archive
            theme.ExtractToFile(Path.Combine(AppInfo.ThemesDir, Path.GetFileName(theme.Name)), overwrite: true);
        }
        settings.Replace(data);
    }
}
