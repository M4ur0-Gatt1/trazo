using System.Numerics;
using Trazo.Core.Geometry;

namespace Trazo.Core.Tests;

public class ViewTransformTests
{
    private static void Near(Vector2 a, Vector2 b) => Assert.True(Vector2.Distance(a, b) < 1e-3f, $"{a} ≠ {b}");

    [Fact]
    public void ScreenAndDocumentAreInverse()
    {
        var v = new ViewTransform { Offset = new(120, -40), Scale = 2.5f, Rotation = 0.7f };
        var p = new Vector2(33, 77);
        Near(p, v.ToDocument(v.ToScreen(p)));
    }

    [Fact]
    public void ZoomKeepsPointUnderCursor()
    {
        var v = new ViewTransform { Offset = new(10, 10), Scale = 1, Rotation = 0.3f };
        var cursor = new Vector2(400, 300);
        var before = v.ToDocument(cursor);
        v.ZoomAt(cursor, 3);
        Near(before, v.ToDocument(cursor));
        Assert.Equal(3, v.Scale, 4);
    }

    [Fact]
    public void FitCentersDocument()
    {
        var v = new ViewTransform();
        v.Fit(new Vector2(1000, 500), new Vector2(1100, 600), margin: 50);
        Assert.Equal(1, v.Scale, 4);
        Near(new Vector2(550, 300), v.ToScreen(new Vector2(500, 250)));
    }

    [Fact]
    public void SnapsNearRightAngle()
    {
        var v = new ViewTransform { Rotation = 92 * MathF.PI / 180 };
        Assert.True(v.SnapRotation(Vector2.Zero));
        Assert.Equal(MathF.PI / 2, v.Rotation, 4);
        v.Rotation = 1;
        Assert.False(v.SnapRotation(Vector2.Zero));
    }
}
