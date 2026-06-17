using System.Windows;
using System.Windows.Media;

namespace QuickPanel.Services;

public enum ThemeMode
{
    Dark,
    Light,
    System
}

public static class ThemeService
{
    /// <summary>Aplica el esquema MD3 a partir del color semilla y el modo de tema.</summary>
    public static void Apply(string seedHex, ThemeMode mode = ThemeMode.Dark)
    {
        Color seed;
        try { seed = (Color)ColorConverter.ConvertFromString(seedHex); }
        catch { seed = (Color)ColorConverter.ConvertFromString("#6366F1"); }

        var (h, s, _) = ToHsl(seed);

        bool light = mode switch
        {
            ThemeMode.Light  => true,
            ThemeMode.System => SystemUsesLight(),
            _                => false
        };

        if (light) ApplyLight(h, s);
        else        ApplyDark(h, s);
    }

    private static void ApplyDark(double h, double s)
    {
        Set("Md3Primary",              FromHsl(h, s * 0.90, 0.72));
        Set("Md3OnPrimary",            FromHsl(h, s * 0.70, 0.14));
        Set("Md3PrimaryContainer",     FromHsl(h, s * 0.65, 0.30));
        Set("Md3OnPrimaryContainer",   FromHsl(h, s * 0.55, 0.90));
        Set("Md3Surface",              FromHsl(h, s * 0.14, 0.09));
        Set("Md3SurfaceContainer",     FromHsl(h, s * 0.14, 0.13));
        Set("Md3SurfaceContainerHigh", FromHsl(h, s * 0.14, 0.17));
        Set("Md3OnSurface",            FromHsl(h, s * 0.06, 0.92));
        Set("Md3OnSurfaceVariant",     FromHsl(h, s * 0.08, 0.68));
        Set("Md3Outline",              FromHsl(h, s * 0.08, 0.42));
        Set("Md3Error",                (Color)ColorConverter.ConvertFromString("#F2B8B5"));
    }

    private static void ApplyLight(double h, double s)
    {
        Set("Md3Primary",              FromHsl(h, s * 0.85, 0.45));
        Set("Md3OnPrimary",            FromHsl(h, s * 0.30, 0.98));
        Set("Md3PrimaryContainer",     FromHsl(h, s * 0.55, 0.88));
        Set("Md3OnPrimaryContainer",   FromHsl(h, s * 0.70, 0.20));
        Set("Md3Surface",              FromHsl(h, s * 0.10, 0.98));
        Set("Md3SurfaceContainer",     FromHsl(h, s * 0.12, 0.94));
        Set("Md3SurfaceContainerHigh", FromHsl(h, s * 0.12, 0.90));
        Set("Md3OnSurface",            FromHsl(h, s * 0.20, 0.12));
        Set("Md3OnSurfaceVariant",     FromHsl(h, s * 0.15, 0.35));
        Set("Md3Outline",              FromHsl(h, s * 0.10, 0.65));
        Set("Md3Error",                (Color)ColorConverter.ConvertFromString("#B3261E"));
    }

    private static bool SystemUsesLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int i && i != 0;
        }
        catch { return false; }
    }

    private static void Set(string key, Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        Application.Current.Resources[key]           = brush;
        Application.Current.Resources[key + "Color"] = c;
    }

    private static (double h, double s, double l) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0, h = 0, s = 0;
        if (Math.Abs(max - min) > 1e-6)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r)      h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else               h = (r - g) / d + 4;
            h /= 6.0;
        }
        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double r, g, b;
        if (s < 1e-6) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
