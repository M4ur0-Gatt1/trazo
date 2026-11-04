using System.Numerics;
using Trazo.Core.Documents;
using Trazo.Core.Geometry;
using Xunit;

namespace Trazo.Core.Tests;

/// <summary>RS-01: resolución del lienzo y escalado en pantalla (docs/specs/RS-01-resolution.md).</summary>
public class RS01_ResolutionTests
{
    /// <summary>Regresión del 2026-11-04: la historieta de 1536×1024 creaba un lienzo de 1536×1024.</summary>
    [Theory]
    [InlineData(1536, 1024, 2)] // el caso del usuario → 3072×2048
    [InlineData(1024, 768, 3)]
    [InlineData(1920, 1080, 2)]
    [InlineData(640, 480, 4)]   // como mucho ×4
    [InlineData(3000, 2000, 1)]
    [InlineData(4000, 3000, 1)]
    [InlineData(100, 16000, 1)] // ya muy largo
    public void RS01_1_ImportScale(int w, int h, int expected) => Assert.Equal(expected, CanvasLimits.ImportScale(w, h));

    [Fact]
    public void RS01_2_ImportScaleStaysWithinLimits()
    {
        // 1500² pide ×2 (3000²); 1450² pide ×3 (4350² = 18,9 M px, cabe); 3000×2900 a ×2 no cabría, pero ya no hace falta
        Assert.Equal(2, CanvasLimits.ImportScale(1500, 1500));
        Assert.Equal(3, CanvasLimits.ImportScale(1450, 1450));
        Assert.Equal(1, CanvasLimits.ImportScale(0, 0));
        for (var side = 50; side < 3000; side += 37)
        {
            var k = CanvasLimits.ImportScale(side, side / 2 + 1);
            Assert.InRange(k, 1, 4);
            Assert.True(CanvasLimits.CanScale(side, side / 2 + 1, k, 1));
        }
    }

    [Fact]
    public void RS01_3_CanScaleChecksSideAreaAndLayers()
    {
        Assert.True(CanvasLimits.CanScale(1536, 1024, 2, 2));
        Assert.False(CanvasLimits.CanScale(9000, 100, 2, 1));        // lado > 16384
        Assert.False(CanvasLimits.CanScale(6000, 6000, 2, 1));       // área > 64 M px
        var maxAtDouble = CanvasLimits.MaxLayers(3072, 2048);
        Assert.True(CanvasLimits.CanScale(1536, 1024, 2, maxAtDouble));
        Assert.False(CanvasLimits.CanScale(1536, 1024, 2, maxAtDouble + 1)); // demasiadas capas para el nuevo tamaño
    }

    [Fact]
    public void RS01_4_GuideScalesWithCanvas()
    {
        var g = new DrawingGuide { Kind = GuideKind.Perspective, Size = 80, Thickness = 2, VanishingPoints = [new Vector2(10, 20), new Vector2(-5, 300)] };
        var s = g.Scaled(2);
        Assert.Equal(160, s.Size);
        Assert.Equal(2, s.Thickness); // en px de pantalla: no cambia
        Assert.Equal([new Vector2(20, 40), new Vector2(-10, 600)], s.VanishingPoints);
        Assert.Equal(80, g.Size); // el original no cambia
    }

    [Theory]
    [InlineData(0.3, 1, ZoomFilter.Smooth)]
    [InlineData(1, 1, ZoomFilter.Linear)]
    [InlineData(2.5, 1, ZoomFilter.Linear)]   // antes: píxeles a partir de 2,5
    [InlineData(4, 1, ZoomFilter.Linear)]
    [InlineData(6, 1, ZoomFilter.Pixels)]
    [InlineData(4, 1.5, ZoomFilter.Pixels)]   // 4 × 150 % = 6 píxeles de pantalla
    [InlineData(0.4, 1.5, ZoomFilter.Linear)]
    public void RS01_5_ZoomFilterCountsScreenPixels(double scale, double raster, ZoomFilter expected) =>
        Assert.Equal(expected, ZoomFilters.For(scale, raster));
}
