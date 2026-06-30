using System.Globalization;
using System.Windows.Markup;

namespace QuickPanel.Services;

/// <summary>Idiomas soportados por la interfaz. El valor null en settings = automático
/// (detectar el de Windows). Si el de Windows no coincide con ninguno, se usa English.</summary>
public enum Lang
{
    English,
    Spanish,
    German,
    French,
    Italian,
    Portuguese,
    Japanese,
    ChineseSimplified,
    ChineseTraditional
}

/// <summary>
/// Localización de la interfaz. Cargado una vez al inicio (App) según el idioma guardado
/// en settings, o el de Windows si está en "automático". Los strings se obtienen por clave
/// con <see cref="T"/> (código) o con la extensión de markup {loc:Loc Clave} (XAML).
///
/// El cambio de idioma se aplica a las ventanas que se abran DESPUÉS del cambio (no hace
/// binding en vivo). Como Configuración y los overlays se recrean al abrirse, alcanza con
/// guardar la preferencia y reabrir/recrear.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _map = Translations.Get(Lang.English);

    public static Lang Current { get; private set; } = Lang.English;

    /// <summary>Inicializa el idioma. forced=null → autodetectar desde Windows.</summary>
    public static void Init(Lang? forced)
    {
        Set(forced ?? DetectFromWindows());
    }

    public static void Set(Lang lang)
    {
        Current = lang;
        _map = Translations.Get(lang);
    }

    /// <summary>Traduce una clave. Si falta en el idioma actual, cae a inglés; si tampoco
    /// está, devuelve la clave (nunca lanza ni devuelve vacío).</summary>
    public static string T(string key)
    {
        if (_map.TryGetValue(key, out var v)) return v;
        if (Translations.Get(Lang.English).TryGetValue(key, out var en)) return en;
        return key;
    }

    /// <summary>Mapea la cultura de Windows a un idioma soportado; si no hay match, English.</summary>
    public static Lang DetectFromWindows()
    {
        var c = CultureInfo.CurrentUICulture;
        string two  = c.TwoLetterISOLanguageName.ToLowerInvariant();
        string name = c.Name.ToLowerInvariant(); // ej: "pt-br", "zh-hans", "zh-tw"

        return two switch
        {
            "es" => Lang.Spanish,
            "de" => Lang.German,
            "fr" => Lang.French,
            "it" => Lang.Italian,
            "pt" => Lang.Portuguese,
            "ja" => Lang.Japanese,
            "zh" => (name.Contains("hant") || name.Contains("tw") ||
                     name.Contains("hk")   || name.Contains("mo"))
                        ? Lang.ChineseTraditional
                        : Lang.ChineseSimplified,
            _ => Lang.English
        };
    }
}

/// <summary>
/// Extensión de markup para usar traducciones en XAML: <c>Text="{loc:Loc Settings_Title}"</c>.
/// Resuelve el string en el momento de cargar el XAML (no se actualiza en vivo).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
