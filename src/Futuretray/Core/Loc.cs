using System.Globalization;

namespace Futuretray.Core;

/// <summary>
/// Перевод интерфейса. Исходный текст — русский, он же ключ (как в gettext);
/// английские строки — в <see cref="English"/>. Нет перевода — показываем русский.
/// </summary>
public static class Loc
{
    /// <summary>"ru" или "en".</summary>
    public static string Language { get; private set; } = "ru";

    public static bool IsEnglish => Language == "en";

    public static void Init(UiLanguage preference)
    {
        Language = preference switch
        {
            UiLanguage.Russian => "ru",
            UiLanguage.English => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en",
        };
    }

    public static string T(string russian) =>
        IsEnglish && English.Strings.TryGetValue(russian, out var english) ? english : russian;

    public static string F(string russian, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(russian), args);

    /// <summary>«1 приложение / 3 приложения / 7 приложений» — или «1 app / 7 apps».</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        if (IsEnglish) return n == 1 ? T(one) : T(many);
        int m = Math.Abs(n) % 100;
        if (m is >= 11 and <= 14) return many;
        return (m % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }
}
