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
        // build next to the target, then swap: a failure midway keeps the previous backup
        var tmp = zipPath + ".tmp";
        File.Delete(tmp); // a leftover from a failed export (Create mode refuses an existing file)
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(SettingsEntry).Open()))
                    writer.Write(settings.ToJson());

                if (Directory.Exists(AppInfo.ThemesDir))
                    foreach (var file in Directory.GetFiles(AppInfo.ThemesDir))
                        zip.CreateEntryFromFile(file, ThemesPrefix + Path.GetFileName(file));
            }
            File.Move(tmp, zipPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
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
