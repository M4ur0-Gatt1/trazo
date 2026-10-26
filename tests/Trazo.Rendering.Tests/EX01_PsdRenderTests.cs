using Trazo.Core.Documents;
using Trazo.Core.IO;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>EX-01.7 — Exportar a PSD y volver a importar con el motor real (WARP).</summary>
public class EX01_PsdRenderTests
{
    private const int W = 160, H = 120;

    /// <summary>Documento con lo que el PSD tiene que conservar: semitransparencias, grupo, máscara, recorte y modos.</summary>
    private static PaintDocument Sample(bool backgroundVisible)
    {
        var doc = new PaintDocument(Gpu.Device, W, H, "psd") { Background = Color.FromArgb(255, 240, 230, 210), BackgroundVisible = backgroundVisible };
        var group = new LayerGroup { Name = "Personaje", Opacity = 0.8f, Blend = BlendMode.Multiply, Collapsed = true };
        doc.Groups.Add(group);

        var sky = doc.CreateLayer("Cielo");
        using (var ds = sky.Surface.CreateDrawingSession())
            for (var y = 0; y < H; y += 4) ds.FillRectangle(0, y, W, 4, Color.FromArgb((byte)(80 + y), 40, (byte)(100 + y), 220));
        doc.Layers.Add(sky);

        var body = doc.CreateLayer("Cuerpo — ñ");
        body.GroupId = group.Id;
        body.Opacity = 0.75f;
        body.AlphaLock = true;
        using (var ds = body.Surface.CreateDrawingSession()) ds.FillCircle(70, 60, 35, Color.FromArgb(200, 220, 60, 30));
        body.Mask = Layer.CreateMask(Gpu.Device, W, H, Color.FromArgb(255, 255, 255, 255));
        using (var ds = body.Mask.CreateDrawingSession()) ds.FillRectangle(70, 0, 90, H, Color.FromArgb(255, 60, 60, 60));
        doc.Layers.Add(body);

        var shade = doc.CreateLayer("Sombra");
        shade.GroupId = group.Id;
        shade.Clip = true;
        shade.Blend = BlendMode.Overlay;
        using (var ds = shade.Surface.CreateDrawingSession()) ds.FillRectangle(40, 50, 90, 40, Color.FromArgb(160, 20, 20, 120));
        doc.Layers.Add(shade);

        var top = doc.CreateLayer("Brillo");
        top.Blend = BlendMode.Screen;
        top.Visible = true;
        using (var ds = top.Surface.CreateDrawingSession()) ds.DrawLine(10, 10, 150, 110, Color.FromArgb(128, 255, 255, 180), 9);
        doc.Layers.Add(top);

        var hidden = doc.CreateLayer("Oculta");
        hidden.Visible = false;
        using (var ds = hidden.Surface.CreateDrawingSession()) ds.Clear(Color.FromArgb(255, 0, 0, 0));
        doc.Layers.Add(hidden);
        return doc;
    }

    private static PsdInterop.ImportResult RoundTrip(PaintDocument doc)
    {
        using var ms = new MemoryStream();
        PsdInterop.Write(doc, ms);
        ms.Position = 0;
        return PsdInterop.Read(Gpu.Device, ms, "vuelta");
    }

    [Fact]
    public void PsdExportImportMatchesDocument()
    {
        using var doc = Sample(backgroundVisible: true);
        var (back, warnings) = RoundTrip(doc);
        using (back)
        {
            Assert.Empty(warnings);
            // estructura: el fondo llega como capa «Fondo» debajo de todo
            Assert.Equal(["Fondo", "Cielo", "Cuerpo — ñ", "Sombra", "Brillo", "Oculta"], back.Layers.Select(l => l.Name));
            for (var i = 0; i < doc.Layers.Count; i++)
            {
                var a = doc.Layers[i];
                var b = back.Layers[i + 1];
                Assert.Equal(a.Opacity, b.Opacity, 0.002f);
                Assert.Equal((a.Blend, a.Visible, a.Clip, a.AlphaLock, a.HasMask), (b.Blend, b.Visible, b.Clip, b.AlphaLock, b.HasMask));
                Assert.Equal(a.GroupId is null, b.GroupId is null);
                // los píxeles de la capa vuelven exactos (Psd.ToStraightRgba y ToPremultipliedBgra son inversas)
                Assert.Equal(a.Surface.GetPixelBytes(), b.Surface.GetPixelBytes());
                if (a.Mask is { } m) Assert.Equal(m.GetPixelBytes(), b.Mask!.GetPixelBytes());
            }
            var g = Assert.Single(back.Groups);
            Assert.Equal(("Personaje", BlendMode.Multiply, true, true), (g.Name, g.Blend, g.Visible, g.Collapsed));
            Assert.Equal(0.8f, g.Opacity, 0.003f);

            // y la imagen: la misma composición (± 1 nivel)
            using var fa = doc.Flatten();
            using var fb = back.Flatten();
            var pa = fa.GetPixelBytes();
            var pb = fb.GetPixelBytes();
            var maxDiff = Enumerable.Range(0, pa.Length).Max(i => Math.Abs(pa[i] - pb[i]));
            Assert.True(maxDiff <= 1, $"diferencia máxima {maxDiff}");
        }
    }

    [Fact]
    public void CompositeIsTheFlattenedDocument()
    {
        // con el fondo oculto no hay capa «Fondo» y la imagen compuesta va sobre blanco
        using var doc = Sample(backgroundVisible: false);
        var psd = PsdInterop.ToPsd(doc);
        Assert.DoesNotContain(psd.Layers, l => l.Name == PsdInterop.BackgroundLayerName);
        using var flat = doc.Flatten(includeBackground: false);
        var straight = new byte[W * H * 4];
        Psd.ToStraightRgba(flat.GetPixelBytes(), straight);
        for (var i = 0; i < straight.Length; i += 4)
        {
            var a = straight[i + 3];
            for (var c = 0; c < 3; c++)
            {
                var expected = (straight[i + c] * a + 255 * (255 - a)) / 255.0;
                Assert.True(Math.Abs(psd.Composite[i + c] - expected) <= 1, $"píxel {i / 4}");
            }
            Assert.Equal(255, psd.Composite[i + 3]);
        }
        // una capa vacía se exporta sin rectángulo y vuelve vacía
        var empty = doc.CreateLayer("Vacía");
        doc.Layers.Add(empty);
        var (back, _) = RoundTrip(doc);
        using (back) Assert.All(back.Layers[^1].Surface.GetPixelBytes(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void FlatAndOversizedPsdImports()
    {
        // sin capas: la imagen compuesta pasa a ser la única capa
        var flat = new PsdFile { Width = 32, Height = 16, Composite = Enumerable.Range(0, 32 * 16 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : i % 256)).ToArray() };
        var (doc, warnings) = PsdInterop.FromPsd(Gpu.Device, flat, "plano");
        using (doc)
        {
            Assert.Empty(warnings);
            var only = Assert.Single(doc.Layers);
            Assert.Equal("Imagen", only.Name);
            var expected = new byte[flat.Composite.Length];
            Psd.ToPremultipliedBgra(flat.Composite, expected);
            Assert.Equal(expected, only.Surface.GetPixelBytes());
        }
        // más grande de lo que admite Trazo: se rechaza con mensaje, sin crear texturas
        var huge = new PsdFile { Width = 20000, Height = 16, Composite = [] };
        Assert.Contains("no cabe en Trazo", Assert.Throws<InvalidDataException>(() => PsdInterop.FromPsd(Gpu.Device, huge, "x")).Message);
        // 16 bits: se avisa de la conversión
        flat.SourceDepth = 16;
        var (doc16, w16) = PsdInterop.FromPsd(Gpu.Device, flat, "16");
        using (doc16) Assert.Contains(w16, w => w.Contains("16 bits"));
    }
}
