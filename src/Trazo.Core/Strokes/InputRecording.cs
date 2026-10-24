using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trazo.Core.Geometry;

namespace Trazo.Core.Strokes;

/// <summary>Muestra cruda del dispositivo en coordenadas de pantalla (DIP).</summary>
public sealed record RecordedSample(double T, float X, float Y, float Pressure, float Tilt);

/// <summary>
/// Grabación de un trazo tal como llegó del dispositivo, con la vista que había
/// en ese momento. Permite reproducir exactamente un trazo real en las pruebas.
/// </summary>
public sealed record InputRecording
{
    public int Version { get; init; } = 1;
    public string Device { get; init; } = "sintético";
    public string? Note { get; init; }
    public float ViewScale { get; init; } = 1;
    public float ViewRotation { get; init; }
    public float ViewOffsetX { get; init; }
    public float ViewOffsetY { get; init; }
    public List<RecordedSample> Samples { get; init; } = [];

    public ViewTransform View => new() { Scale = ViewScale, Rotation = ViewRotation, Offset = new Vector2(ViewOffsetX, ViewOffsetY) };

    /// <summary>Convierte las muestras a coordenadas del documento con la vista grabada.</summary>
    public IEnumerable<InputPoint> ToDocument()
    {
        var v = View;
        foreach (var s in Samples)
        {
            var d = v.ToDocument(new Vector2(s.X, s.Y));
            yield return new InputPoint(d.X, d.Y, s.Pressure, s.Tilt, s.T);
        }
    }

    /// <summary>Crea una grabación a partir de puntos del documento vistos con <paramref name="view"/>.</summary>
    public static InputRecording FromDocument(IEnumerable<InputPoint> points, ViewTransform view, string device = "sintético") => new()
    {
        Device = device,
        ViewScale = view.Scale,
        ViewRotation = view.Rotation,
        ViewOffsetX = view.Offset.X,
        ViewOffsetY = view.Offset.Y,
        Samples = points.Select(p =>
        {
            var s = view.ToScreen(new Vector2(p.X, p.Y));
            return new RecordedSample(p.Time, s.X, s.Y, p.Pressure, p.Tilt);
        }).ToList(),
    };

    public string ToJson() => JsonSerializer.Serialize(this, RecordingJson.Default.InputRecording);

    public static InputRecording FromJson(string json) =>
        JsonSerializer.Deserialize(json, RecordingJson.Default.InputRecording) ?? throw new InvalidDataException("Grabación vacía");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(InputRecording))]
internal sealed partial class RecordingJson : JsonSerializerContext;

/// <summary>Trayectorias sintéticas reproducibles para pruebas de trazo.</summary>
public static class SyntheticStrokes
{
    /// <summary>Muestrea una trayectoria paramétrica a velocidad uniforme.</summary>
    public static List<InputPoint> Sample(Func<float, Vector2> path, float durationMs, float hz, Func<float, float>? pressure = null, float tilt = 0)
    {
        var n = Math.Max(2, (int)MathF.Round(durationMs / 1000 * hz));
        var list = new List<InputPoint>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var t = i / (float)n;
            var p = path(t);
            list.Add(new InputPoint(p.X, p.Y, pressure?.Invoke(t) ?? 1, tilt, 1 + t * durationMs));
        }
        return list;
    }

    public static Func<float, Vector2> Line(Vector2 a, Vector2 b) => t => Vector2.Lerp(a, b, t);

    public static Func<float, Vector2> Circle(Vector2 c, float r) =>
        t => c + new Vector2(MathF.Cos(t * MathF.Tau), MathF.Sin(t * MathF.Tau)) * r;

    public static Func<float, Vector2> Zigzag(Vector2 start, float width, float height, int teeth) => t =>
    {
        var x = start.X + t * width;
        var phase = t * teeth % 1;
        var y = start.Y + (phase < 0.5f ? phase * 2 : 2 - phase * 2) * height;
        return new Vector2(x, y);
    };

    public static Func<float, Vector2> Spiral(Vector2 c, float r0, float r1, float turns) => t =>
    {
        var r = r0 + (r1 - r0) * t;
        var a = t * turns * MathF.Tau;
        return c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
    };

    /// <summary>Añade temblor de mano gaussiano reproducible.</summary>
    public static List<InputPoint> WithTremor(IEnumerable<InputPoint> pts, float amplitude, int seed)
    {
        var rnd = new Random(seed);
        float G() => (float)(Math.Sqrt(-2 * Math.Log(1 - rnd.NextDouble())) * Math.Cos(2 * Math.PI * rnd.NextDouble()));
        return pts.Select(p => p with { X = p.X + G() * amplitude, Y = p.Y + G() * amplitude }).ToList();
    }
}
