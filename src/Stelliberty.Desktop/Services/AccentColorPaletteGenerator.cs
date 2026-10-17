using Avalonia.Media;

namespace Stelliberty.Desktop.Services;

internal static class AccentColorPaletteGenerator
{
    private static readonly Color LightOnAccent = ThemeSurfaceColors.Light;
    private static readonly Color DarkOnAccent = ThemeSurfaceColors.Dark;

    public static AccentColorPalette Generate(Color source, bool isLightTheme)
    {
        var oklch = ToOklch(source);
        var chroma = NormalizeChroma(oklch.Chroma);
        var hue = double.IsNaN(oklch.Hue) ? 0 : oklch.Hue;
        var surface = isLightTheme ? ThemeSurfaceColors.Light : ThemeSurfaceColors.Dark;
        var accentTone = isLightTheme ? 0.54 : 0.78;
        var accent = EnsureSurfaceContrast(FromOklch(accentTone, chroma, hue), chroma, hue, surface, isLightTheme);
        var neutralOnAccent = ContrastRatio(accent, LightOnAccent) >= ContrastRatio(accent, DarkOnAccent)
            ? LightOnAccent
            : DarkOnAccent;
        var onAccent = FromOklch(ToOklch(neutralOnAccent).Lightness, chroma, hue);

        return new AccentColorPalette(
            accent,
            onAccent,
            FromOklch(isLightTheme ? 0.91 : 0.34, chroma * 0.55, hue),
            FromOklch(isLightTheme ? 0.62 : 0.66, chroma * 0.80, hue),
            FromOklch(isLightTheme ? 0.94 : 0.28, chroma * 0.45, hue));
    }

    public static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color EnsureSurfaceContrast(Color color, double chroma, double hue, Color surface, bool isLightTheme)
    {
        if (ContrastRatio(color, surface) >= 3)
        {
            return color;
        }

        var tone = ToOklch(color).Lightness;
        for (var i = 0; i < 24; i++)
        {
            tone += isLightTheme ? -0.02 : 0.02;
            tone = Math.Clamp(tone, 0.30, 0.88);
            color = FromOklch(tone, chroma, hue);
            if (ContrastRatio(color, surface) >= 3)
            {
                return color;
            }
        }

        return color;
    }

    private static double NormalizeChroma(double chroma)
    {
        if (chroma < 0.015)
        {
            return 0;
        }

        return Math.Clamp(chroma, 0.025, 0.18);
    }

    private static Color FromOklch(double lightness, double chroma, double hue)
    {
        for (var i = 0; i < 32; i++)
        {
            var lab = ToOklab(lightness, chroma, hue);
            var linearRgb = ToLinearRgb(lab);
            if (IsInGamut(linearRgb))
            {
                return ToColor(linearRgb);
            }

            chroma *= 0.90;
        }

        return ToColor(ToLinearRgb(new Oklab(lightness, 0, 0)));
    }

    private static Oklch ToOklch(Color color)
    {
        var lab = ToOklab(color);
        var chroma = Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
        var hue = chroma < 0.000001 ? double.NaN : Math.Atan2(lab.B, lab.A) * 180 / Math.PI;
        if (hue < 0)
        {
            hue += 360;
        }

        return new Oklch(lab.Lightness, chroma, hue);
    }

    private static Oklab ToOklab(Color color)
    {
        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return new Oklab(
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static Oklab ToOklab(double lightness, double chroma, double hue)
    {
        var radians = hue * Math.PI / 180;
        return new Oklab(lightness, chroma * Math.Cos(radians), chroma * Math.Sin(radians));
    }

    private static LinearRgb ToLinearRgb(Oklab lab)
    {
        var l = lab.Lightness + 0.3963377774 * lab.A + 0.2158037573 * lab.B;
        var m = lab.Lightness - 0.1055613458 * lab.A - 0.0638541728 * lab.B;
        var s = lab.Lightness - 0.0894841775 * lab.A - 1.2914855480 * lab.B;

        l *= l * l;
        m *= m * m;
        s *= s * s;

        return new LinearRgb(
            4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static bool IsInGamut(LinearRgb rgb)
    {
        return rgb.R is >= 0 and <= 1
            && rgb.G is >= 0 and <= 1
            && rgb.B is >= 0 and <= 1;
    }

    private static Color ToColor(LinearRgb rgb)
    {
        return Color.FromRgb(ToByte(FromLinear(rgb.R)), ToByte(FromLinear(rgb.G)), ToByte(FromLinear(rgb.B)));
    }

    private static double RelativeLuminance(Color color)
    {
        return 0.2126 * ToLinear(color.R / 255d)
            + 0.7152 * ToLinear(color.G / 255d)
            + 0.0722 * ToLinear(color.B / 255d);
    }

    private static double ToLinear(double value)
    {
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double FromLinear(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4d) - 0.055;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }

    private readonly record struct Oklab(double Lightness, double A, double B);

    private readonly record struct Oklch(double Lightness, double Chroma, double Hue);

    private readonly record struct LinearRgb(double R, double G, double B);
}
