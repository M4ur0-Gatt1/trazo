using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>TL-01 — Time-lapse (docs/specs/TL-01-timelapse.md), parte sin GPU.</summary>
public sealed class TL01_TimelapseTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trazo-tl01-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_(string name) => Path.Combine(_dir, name);

    /// <summary>«JPEG» de prueba: bytes reconocibles de longitud variable (el archivo no interpreta su contenido).</summary>
    private static byte[] Frame(int n) => Enumerable.Range(0, 50 + n * 7).Select(i => (byte)(i * 31 + n)).ToArray();

    /// <summary>Reloj que avanza lo que se le pida.</summary>
    private sealed class Clock
    {
        public DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Get() => Now;
    }

    // ---------- TL-01.1 ----------

    [Fact]
    public void FramesRoundTripAndAppend()
    {
        var path = File_("obra.trazo.timelapse");
        var clock = new Clock();
        using (var w = TimelapseWriter.Open(path, 640, 480, clock.Get))
        {
            for (var i = 0; i < 3; i++)
            {
                clock.Now = clock.Now.AddSeconds(2);
                w.Append(Frame(i));
            }
            Assert.Equal(3, w.Count);
        }
        // otra sesión: conserva el tamaño original aunque se pida otro y sigue contando el tiempo desde 6 s
        clock.Now = clock.Now.AddHours(5); // el tiempo con la app cerrada no cuenta
        using (var w = TimelapseWriter.Open(path, 100, 100, clock.Get))
        {
            Assert.Equal((640, 480), (w.Width, w.Height));
            Assert.Equal(3, w.Count);
            clock.Now = clock.Now.AddSeconds(1);
            w.Append(Frame(3));
        }
        using var r = TimelapseReader.Open(path);
        Assert.Equal((640, 480), (r.Width, r.Height));
        Assert.Equal(4, r.Frames.Count);
        for (var i = 0; i < 4; i++) Assert.Equal(Frame(i), r.ReadFrame(i));
        Assert.Equal([2000, 4000, 6000, 7000], r.Frames.Select(f => f.ElapsedMs));
        Assert.Equal(TimeSpan.FromSeconds(7), r.Recorded);
        Assert.Equal(new FileInfo(path).Length, r.Bytes);
    }

    // ---------- TL-01.2 ----------

    [Fact]
    public void TruncatedTailIsDropped()
    {
        var path = File_("a.timelapse");
        using (var w = TimelapseWriter.Open(path, 64, 64))
            for (var i = 0; i < 5; i++) w.Append(Frame(i));
        var full = File.ReadAllBytes(path);
        // corte a mitad del último fotograma, como un cierre inesperado mientras se escribía
        File.WriteAllBytes(path, full[..^10]);
        using (var r = TimelapseReader.Open(path)) Assert.Equal(4, r.Frames.Count);
        // al volver a grabar se descarta ese final y se sigue sin perder nada más
        using (var w = TimelapseWriter.Open(path, 64, 64))
        {
            Assert.Equal(4, w.Count);
            w.Append(Frame(9));
        }
        using (var r = TimelapseReader.Open(path))
        {
            Assert.Equal(5, r.Frames.Count);
            Assert.Equal(Frame(9), r.ReadFrame(4));
        }
        // cortes en cualquier punto: nunca otra excepción, y los fotogramas que quedan están completos
        for (var n = TimelapseFile.HeaderSize; n < full.Length; n += 29)
        {
            File.WriteAllBytes(path, full[..n]);
            using var r = TimelapseReader.Open(path);
            for (var i = 0; i < r.Frames.Count; i++) Assert.Equal(Frame(i), r.ReadFrame(i));
        }
    }

    [Fact]
    public void MalformedFilesFailCleanly()
    {
        var path = File_("b.timelapse");
        File.WriteAllBytes(path, "esto no es un time-lapse"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => TimelapseReader.Open(path).Dispose());
        File.WriteAllBytes(path, [.. "TRZTL1"u8, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Throws<InvalidDataException>(() => TimelapseReader.Open(path).Dispose()); // versión desconocida
        File.WriteAllBytes(path, [.. "TRZTL1"u8, 1, 0, 3, 0, 0, 0, 4, 0, 0, 0]);
        Assert.Throws<InvalidDataException>(() => TimelapseReader.Open(path).Dispose()); // ancho impar

        // bytes cambiados al azar después de la cabecera: se lee lo que sea coherente, sin otra excepción
        using (var w = TimelapseWriter.Open(path = File_("c.timelapse"), 32, 32))
            for (var i = 0; i < 8; i++) w.Append(Frame(i));
        var good = File.ReadAllBytes(path);
        var rng = new Random(26);
        for (var k = 0; k < 200; k++)
        {
            var bad = (byte[])good.Clone();
            bad[rng.Next(TimelapseFile.HeaderSize, bad.Length)] = (byte)rng.Next(256);
            File.WriteAllBytes(path, bad);
            using var r = TimelapseReader.Open(path);
            for (var i = 0; i < r.Frames.Count; i++) Assert.Equal(r.Frames[i].Length, r.ReadFrame(i).Length);
        }

        // un archivo dañado no impide seguir grabando: se aparta y se empieza otro
        File.WriteAllBytes(path, "basura"u8.ToArray());
        using (var w = TimelapseWriter.Open(path, 32, 32)) w.Append(Frame(0));
        Assert.True(File.Exists(path + ".dañado"));
        using (var r = TimelapseReader.Open(path)) Assert.Single(r.Frames);
    }

    // ---------- TL-01.3 ----------

    [Fact]
    public void FrameSizeFitsAndIsEven()
    {
        foreach (var (w, h) in new[] { (3840, 2160), (2048, 2048), (1080, 1920), (2481, 3509), (16384, 16), (16, 16), (999, 333), (1080, 1080) })
        {
            var (fw, fh) = TimelapseFile.FrameSize(w, h);
            Assert.True(fw % 2 == 0 && fh % 2 == 0, $"{w}×{h} → {fw}×{fh}");
            Assert.True(Math.Max(fw, fh) <= TimelapseFile.MaxSide);
            Assert.True(fw <= Math.Max(2, w + 1) && fh <= Math.Max(2, h + 1), "no se amplía");
            // misma proporción salvo el redondeo a par (± 1 px en cada lado)
            Assert.True(Math.Abs(fw * (double)h / w - fh) <= 2.5, $"{w}×{h} → {fw}×{fh}");
        }
        Assert.Equal((1080, 608), TimelapseFile.FrameSize(3840, 2160));
        Assert.Equal((1080, 1080), TimelapseFile.FrameSize(2048, 2048));
    }

    // ---------- TL-01.4 ----------

    [Fact]
    public void ExportScheduleFitsAndKeepsEnds()
    {
        const int hold = TimelapseSchedule.HoldSeconds * TimelapseSchedule.Fps;
        Assert.Empty(TimelapseSchedule.Full(0));
        Assert.Empty(TimelapseSchedule.Short(0));

        var full = TimelapseSchedule.Full(2500);
        Assert.Equal(2500 + hold, full.Length);
        Assert.Equal(Enumerable.Range(0, 2500), full[..2500]);
        Assert.All(full[2500..], i => Assert.Equal(2499, i));

        foreach (var n in new[] { 1, 2, 100, 840, 841, 2500, 100_000 })
        {
            var s = TimelapseSchedule.Short(n);
            Assert.True(s.Length <= 30 * TimelapseSchedule.Fps, $"{n}: {s.Length} fotogramas");
            Assert.Equal(0, s[0]);
            Assert.Equal(n - 1, s[^1]);
            Assert.All(s[^hold..], i => Assert.Equal(n - 1, i));
            var body = s[..^hold];
            // en orden, sin repetir (no se inventan fotogramas) y repartidos por igual
            for (var i = 1; i < body.Length; i++) Assert.True(body[i] > body[i - 1], $"{n}: no avanza en {i}");
            Assert.Equal(Math.Min(n, 28 * TimelapseSchedule.Fps), body.Length);
            if (body.Length > 2)
            {
                var steps = body.Zip(body.Skip(1), (a, b) => b - a).ToList();
                Assert.True(steps.Max() - steps.Min() <= 1, $"{n}: saltos de {steps.Min()} a {steps.Max()}");
            }
        }
    }

    // ---------- TL-01.7 ----------

    [Fact]
    public void SidecarFollowsProject()
    {
        var a = File_("gato.trazo");
        File.WriteAllText(a, "obra");
        File.WriteAllText(TimelapseFile.SidecarPath(a), "tl");
        var b = File_("gato copia.trazo");
        ProjectFiles.Copy(a, b);
        Assert.Equal("tl", File.ReadAllText(TimelapseFile.SidecarPath(b)));
        Assert.True(File.Exists(TimelapseFile.SidecarPath(a)));
        var c = File_("michi.trazo");
        ProjectFiles.Move(a, c);
        Assert.False(File.Exists(a) || File.Exists(TimelapseFile.SidecarPath(a)));
        Assert.Equal("tl", File.ReadAllText(TimelapseFile.SidecarPath(c)));
        // una obra sin time-lapse también se mueve y se copia
        var d = File_("sin.trazo");
        File.WriteAllText(d, "x");
        ProjectFiles.Move(d, File_("sin2.trazo"));
        ProjectFiles.Copy(File_("sin2.trazo"), File_("sin3.trazo"));
        Assert.False(File.Exists(TimelapseFile.SidecarPath(File_("sin3.trazo"))));
    }
}
