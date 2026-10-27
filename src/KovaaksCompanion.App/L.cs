using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace KovaaksCompanion.App;

/// <summary>UI text lookup. The English source text is the key; a language file (Resources/Strings_xx.resx) maps it to that language and a missing entry shows the English text.</summary>
public static class L
{
    public static readonly (string Code, string Name)[] Languages = [("en", "English"), ("ko", "한국어"), ("zh", "中文"), ("ru", "Русский")];

    static ResourceManager? _rm;
    static string _code = "en";
    static CultureInfo _culture = CultureInfo.InvariantCulture;
    public static string Code => _code;
    public static CultureInfo Culture => _culture;

    /// <summary>Picks the language: a known code, or Windows' UI language when empty/unknown.</summary>
    public static void Set(string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        if (!Languages.Any(l => l.Code == c)) c = Languages.Any(l => l.Code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : "en";
        _code = c;
        _culture = c == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(c);
        _rm = c == "en" ? null : new ResourceManager("KovaaksCompanion.App.Resources.Strings_" + c, typeof(L).Assembly);
    }

    /// <summary>Culture for dates and times: the selected language's, with US English for English (the invariant culture has no real date patterns).</summary>
    public static CultureInfo DateCulture => Code == "en" ? CultureInfo.GetCultureInfo("en-US") : Culture;

    public static string ShortDate(DateTime t) => t.ToString("d", DateCulture);
    public static string MonthDay(DateTime t) => t.ToString("M", DateCulture);
    public static string Time(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string Stamp(DateTime t) => ShortDate(t) + " " + Time(t);

    /// <summary>Every language file's texts by language code (English text to translation), for the browser popup, which picks its language itself.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables() => Languages.Where(l => l.Code != "en").ToDictionary(l => l.Code, l =>
    {
        var set = new ResourceManager("KovaaksCompanion.App.Resources.Strings_" + l.Code, typeof(L).Assembly).GetResourceSet(CultureInfo.InvariantCulture, true, false);
        return (IReadOnlyDictionary<string, string>)(set?.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!) ?? []);
    });

    public static string T(string key)
    {
        if (_rm == null) return key;
        try { return _rm.GetString(key, CultureInfo.InvariantCulture) ?? key; }
        catch (MissingManifestResourceException) { return key; }
    }

    /// <summary>Translated format string filled with <paramref name="args"/>.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);
}

/// <summary>XAML: <c>Text="{app:Loc Settings}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
