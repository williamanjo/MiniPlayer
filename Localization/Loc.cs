using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace MiniPlayer.Localization;

/// <summary>
/// UI language. Texts live in Localization/Languages/&lt;code&gt;.json, one file per language,
/// embedded in the exe. To add a language: add its file, its code to <see cref="Languages"/> and
/// an entry in the settings language list.
/// XAML binds through <see cref="TrExtension"/>, so switching language updates open windows live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Languages = ["en", "pt-BR", "es", "fr", "de", "ja", "ru", "ko"];

    // complete, and the fallback for keys a language file lacks; declared before Instance, which uses it
    static readonly Dictionary<string, string> English = Load("en");

    public static Loc Instance { get; } = new();

    Dictionary<string, string> _texts = English;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (for texts built in code).</summary>
    public static event Action? Changed;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>Text for a key in the current language (falls back to English, then the key).</summary>
    public string this[string key] =>
        _texts.TryGetValue(key, out var text) || English.TryGetValue(key, out text) ? text : key;

    /// <summary>"auto"/null = Windows language when supported (any Portuguese → pt-BR), else English.</summary>
    public static void SetLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == "auto")
        {
            var windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            code = windows == "pt" ? "pt-BR" : windows;
        }
        if (!Languages.Contains(code)) code = "en";

        Instance._texts = code == "en" ? English : Load(code);
        Instance.Culture = CultureInfo.GetCultureInfo(code);
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"lang/{code}.json")
            ?? throw new InvalidOperationException($"language file {code}.json is not embedded");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}

/// <summary>XAML: <c>Text="{l:Tr settings_title}"</c> — a live binding to the translated text.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
