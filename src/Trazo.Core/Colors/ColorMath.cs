using System.Globalization;

namespace Trazo.Core.Colors;

/// <summary>Color RGBA de 8 bits por canal (no premultiplicado).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba White = new(255, 255, 255);
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba Transparent = new(0, 0, 0, 0);

    public string ToHex() => A == 255
        ? $"#{R:x2}{G:x2}{B:x2}"
        : $"#{R:x2}{G:x2}{B:x2}{A:x2}";

    /// <summary>Acepta #rgb, #rrggbb y #rrggbbaa (con o sin #).</summary>
    public static bool TryParseHex(string? text, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length is not (6 or 8)) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = s.Length == 6
            ? new Rgba((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgba FromHex(string text) =>
        TryParseHex(text, out var c) ? c : throw new FormatException($"Color no válido: {text}");

    public Hsv ToHsv() => ColorMath.RgbToHsv(this);
}

/// <summary>Tono 0–360, saturación y brillo 0–1.</summary>
public readonly record struct Hsv(double H, double S, double V)
{
    public Rgba ToRgba(byte alpha = 255) => ColorMath.HsvToRgb(this, alpha);
}

public static class ColorMath
{
    public static Rgba HsvToRgb(Hsv c, byte alpha = 255)
    {
        var h = ((c.H % 360) + 360) % 360 / 60.0;
        var s = Math.Clamp(c.S, 0, 1);
        var v = Math.Clamp(c.V, 0, 1);
        var i = (int)Math.Floor(h);
        var f = h - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new Rgba(ToByte(r), ToByte(g), ToByte(b), alpha);
    }

    public static Hsv RgbToHsv(Rgba c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = (g - b) / d % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
            if (h < 0) h += 360;
        }
        return new Hsv(h, max > 0 ? d / max : 0, max);
    }

    private static byte ToByte(double x) => (byte)Math.Clamp(Math.Round(x * 255), 0, 255);
}
