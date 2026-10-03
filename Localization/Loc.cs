using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace MiniPlayer.Localization;

/// <summary>
/// UI language. Texts live in <see cref="Strings"/> (one row per key: English, Portuguese).
/// XAML binds through <see cref="TrExtension"/>, so switching language updates open windows live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>Supported languages, in the column order of <see cref="Strings.Table"/>.</summary>
    public static readonly string[] Languages = ["en", "pt-BR"];

    int _column;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (for texts built in code).</summary>
    public static event Action? Changed;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>Text for a key in the current language (falls back to English, then the key).</summary>
    public string this[string key] =>
        Strings.Table.TryGetValue(key, out var row) ? row[_column] ?? row[0] ?? key : key;

    /// <summary>"auto"/null = Windows language (Portuguese → pt-BR, anything else → English).</summary>
    public static void SetLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == "auto")
            code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt-BR" : "en";
        var column = Math.Max(0, Array.IndexOf(Languages, code));

        Instance._column = column;
        Instance.Culture = CultureInfo.GetCultureInfo(Languages[column]);
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);
}

/// <summary>XAML: <c>Text="{l:Tr settings_title}"</c> — a live binding to the translated text.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
