using Trazo.Core.Documents;
using Trazo.Core.Geometry;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>RS-01 — Duplicar la resolución con el motor real (WARP).</summary>
public class RS01_ResampleRenderTests
{
    private static byte[] Px(Microsoft.Graphics.Canvas.CanvasRenderTarget t, int x, int y) => t.GetPixelBytes(x, y, 1, 1);

    [Fact]
    public void DoublingKeepsContentPropertiesGroupsAndMasks()
    {
        using var doc = PaintDocument.CreateBlank(Gpu.Device, 120, 80, "Historieta");
        doc.Background = Color.FromArgb(255, 250, 240, 230);
        doc.Guide = new DrawingGuide { Kind = GuideKind.Grid, Size = 20 };
        var img = doc.Layers[0];
        img.Opacity = 0.23f; // la referencia tenue del usuario
        using (var ds = img.Surface.CreateDrawingSession()) ds.FillRectangle(10, 10, 30, 20, Color.FromArgb(255, 200, 0, 0));
        img.Mask = Layer.CreateMask(Gpu.Device, 120, 80, Color.FromArgb(255, 255, 255, 255));
        using (var ds = img.Mask.CreateDrawingSession()) ds.FillRectangle(0, 0, 60, 80, Color.FromArgb(255, 0, 0, 0));
        var group = new LayerGroup { Name = "Tinta" };
        doc.Groups.Add(group);
        var ink = doc.CreateLayer("Tinta");
        ink.GroupId = group.Id;
        ink.Blend = BlendMode.Multiply;
        using (var ds = ink.Surface.CreateDrawingSession()) ds.FillRectangle(70, 40, 10, 10, Color.FromArgb(255, 0, 0, 0));
        doc.Layers.Add(ink);
        doc.ActiveIndex = 1;

        using var big = doc.Resampled(2);
        Assert.Equal((240, 160), (big.Width, big.Height));
        Assert.True(big.IsDirty);
        Assert.Equal("Historieta", big.Name);
        Assert.Equal(doc.Background, big.Background);
        Assert.Equal(40, big.Guide!.Size);
        Assert.Equal(1, big.ActiveIndex);
        Assert.Equal(doc.Layers.Select(l => l.ToProperties()), big.Layers.Select(l => l.ToProperties()));
        Assert.Equal(group.Id, Assert.Single(big.Groups).Id);
        Assert.False(big.History.CanUndo);

        // el contenido está en el doble de las coordenadas
        Assert.Equal(200, Px(big.Layers[0].Surface, 50, 40)[2]);     // dentro del rectángulo rojo (25,20 → 50,40)
        Assert.Equal(0, Px(big.Layers[0].Surface, 100, 70)[3]);      // fuera
        Assert.Equal(255, Px(big.Layers[1].Surface, 150, 90)[3]);    // cuadrado negro (75,45 → 150,90)
        Assert.Equal(0, Px(big.Layers[1].Surface, 130, 70)[3]);
        // la máscara también: negra a la izquierda (0–60 → 0–120), blanca a la derecha
        Assert.NotNull(big.Layers[0].Mask);
        Assert.True(Px(big.Layers[0].Mask!, 40, 80)[2] < 10);
        Assert.True(Px(big.Layers[0].Mask!, 200, 80)[2] > 245);
        // el original sigue igual
        Assert.Equal((120, 80), (doc.Width, doc.Height));
    }

    [Fact]
    public void RefusesBeyondLimits()
    {
        using var doc = PaintDocument.CreateBlank(Gpu.Device, 9000, 16, "Ancho");
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Resampled(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Resampled(0));
    }
}
