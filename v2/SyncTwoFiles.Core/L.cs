using System.Collections.Generic;
using System.Globalization;

namespace SyncTwoFiles.Core;

// Textos de la app en espanol e ingles. "" = idioma de Windows (espanol si Windows esta en espanol).
public static partial class L
{
    private static string _lang = Resolve(null);

    public static string Language
    {
        get => _lang;
        set => _lang = Resolve(value);
    }

    public static string Resolve(string? preference) =>
        preference is "es" or "en"
            ? preference
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";

    public static IReadOnlyDictionary<string, (string Es, string En)> All => Strings;

    public static string T(string key, params object?[] args)
    {
        if (!Strings.TryGetValue(key, out var v)) return key;
        var s = _lang == "en" ? v.En : v.Es;
        return args.Length == 0 ? s : string.Format(CultureInfo.CurrentCulture, s, args);
    }
}
