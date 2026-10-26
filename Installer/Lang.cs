using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace MiniPlayerSetup;

/// <summary>The app's own language files (embedded), picked by the Windows language like the app does.</summary>
static class Lang
{
    static readonly string[] Codes = ["en", "pt-BR", "es", "fr", "de", "ja", "ru", "ko"];
    static readonly Dictionary<string, object> English = Load("en");
    static readonly Dictionary<string, object> Current;

    public static string Code { get; }

    static Lang()
    {
        var windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var code = windows == "pt" ? "pt-BR" : windows;
        Code = Codes.Contains(code) ? code : "en";
        Current = Code == "en" ? English : Load(Code);
    }

    public static string T(string key) =>
        Current.TryGetValue(key, out var v) || English.TryGetValue(key, out v) ? (string)v : key;

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    static Dictionary<string, object> Load(string code)
    {
        using var stream = typeof(Lang).Assembly.GetManifestResourceStream($"lang/{code}.json");
        using var reader = new StreamReader(stream!);
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
    }
}
