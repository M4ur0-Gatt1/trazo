using System.Numerics;
using Trazo.Core.Documents;
using Trazo.Core.Imaging;
using Trazo.Core.IO;
using Trazo.Core.Selection;

namespace Trazo.Core.Tests;

/// <summary>SE-01 — Selección (docs/specs/SE-01-selection.md), parte sin GPU.</summary>
public class SE01_SelectionTests
{
    private const int W = 400, H = 300;

    [Theory]
    [InlineData(10f, 20f, 110f, 70f)]
    [InlineData(10.25f, 20.5f, 110.75f, 70.25f)]   // bordes fraccionarios
    [InlineData(-50f, -50f, 30f, 30f)]             // recortado por el lienzo
    public void RectangleAreaIsExact(float x0, float y0, float x1, float y1)
    {
        var m = SelectionMask.Rectangle(W, H, x0, y0, x1, y1);
        var expected = (Math.Min(x1, W) - Math.Max(x0, 0)) * (Math.Min(y1, H) - Math.Max(y0, 0));
        Assert.Equal(expected, m.Area, 0.5);
    }

    private static double Shoelace(IReadOnlyList<Vector2> p)
    {
        double s = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            s += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return Math.Abs(s) / 2;
    }

    [Fact]
    public void PolygonAreaMatchesShoelace()
    {
        Vector2[] star = [new(200, 20), new(240, 120), new(350, 130), new(260, 190), new(300, 280), new(200, 220), new(100, 280), new(140, 190), new(50, 130), new(160, 120)];
        var m = SelectionMask.Polygon(W, H, star);
        Assert.Equal(Shoelace(star), m.Area, Shoelace(star) * 0.005);
    }

    [Fact]
    public void EllipseAreaMatchesFormula()
    {
        var m = SelectionMask.Ellipse(W, H, 50, 40, 350, 240);
        var expected = Math.PI * 150 * 100;
        Assert.Equal(expected, m.Area, expected * 0.005);
    }

    [Fact]
    public void EdgesAreAntialiased()
    {
        var m = SelectionMask.Polygon(W, H, [new(50, 50), new(350, 80), new(200, 250)]);
        Assert.Equal(255, m[200, 120]);
        Assert.Equal(0, m[10, 10]);
        var partial = m.Data.Count(v => v is > 0 and < 255);
        Assert.True(partial > 300, $"solo {partial} píxeles de borde con cobertura parcial");
    }

    [Fact]
    public void SelfIntersectingLassoUsesEvenOdd()
    {
        // pajarita: dos triángulos que se cruzan en el centro
        var m = SelectionMask.Polygon(W, H, [new(50, 50), new(350, 250), new(350, 50), new(50, 250)]);
        Assert.Equal(255, m[60, 150]);   // triángulo izquierdo
        Assert.Equal(255, m[340, 150]);  // triángulo derecho
        Assert.Equal(0, m[200, 60]);     // zona entre ambos (fuera)
    }

    [Fact]
    public void CombineModes()
    {
        var a = SelectionMask.Rectangle(W, H, 0, 0, 200, 300);
        var b = SelectionMask.Rectangle(W, H, 100, 0, 300, 300);
        Assert.Equal(300 * 300, a.Combine(b, SelectionCombine.Add).Area, 1);
        Assert.Equal(100 * 300, a.Combine(b, SelectionCombine.Intersect).Area, 1);
        Assert.Equal(100 * 300, a.Combine(b, SelectionCombine.Subtract).Area, 1);
        Assert.Equal(b.Data, a.Combine(b, SelectionCombine.Replace).Data);
        // máscaras suaves: 128 menos 128 = 128·(1 − 128/255) ≈ 64
        var soft = new SelectionMask(1, 1, [128]);
        Assert.InRange(soft.Combine(soft, SelectionCombine.Subtract).Data[0], 63, 65);
    }

    [Fact]
    public void InvertIsInvolution()
    {
        var m = SelectionMask.Ellipse(W, H, 30, 30, 300, 200);
        Assert.Equal(m.Data, m.Invert().Invert().Data);
        Assert.Equal(W * H - m.Area, m.Invert().Area, 1);
    }

    [Fact]
    public void FeatherPreservesAreaAndSmoothsEdge()
    {
        var m = SelectionMask.Rectangle(W, H, 100, 100, 300, 200);
        var f = m.Feather(18);
        Assert.Equal(m.Area, f.Area, m.Area * 0.01);
        // a lo largo de una fila, la cobertura sube de forma monótona hacia dentro
        var row = Enumerable.Range(80, 40).Select(x => f[x, 150]).ToList();
        for (var i = 1; i < row.Count; i++) Assert.True(row[i] >= row[i - 1]);
        Assert.True(row.Count(v => v is > 5 and < 250) >= 10, "el borde debería ocupar varios píxeles");
        Assert.Equal(m.Data, m.Feather(0).Data);
    }

    [Fact]
    public void BoundsAndEmpty()
    {
        Assert.True(new SelectionMask(W, H).IsEmpty);
        Assert.Equal(new PixelRect(10, 20, 100, 50), SelectionMask.Rectangle(W, H, 10, 20, 110, 70).Bounds);
        Assert.Equal(W * H, SelectionMask.All(W, H).Area, 1);
    }

    [Fact]
    public void MagicWandFollowsColorRegion()
    {
        // imagen: mitad izquierda roja, derecha azul
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var i = (y * W + x) * 4;
            if (x < 200) px[i + 2] = 255; else px[i] = 255;
            px[i + 3] = 255;
        }
        var m = SelectionMask.FromFill(FloodFill.Compute(px, W, H, 50, 50, 20)!, W, H);
        Assert.Equal(255, m[100, 150]);
        Assert.Equal(0, m[300, 150]);
        Assert.InRange(m.Area, 200 * H, 201 * H); // incluye 1 px de dilatación
    }
}

/// <summary>LY-03 — reglas de grupos (docs/specs/LY-02-LY-03-masks-groups.md).</summary>
public class LY03_GroupRulesTests
{
    [Theory]
    [InlineData(new[] { "a", "a", "", "b" }, true)]
    [InlineData(new[] { "a", "", "a" }, false)]
    [InlineData(new[] { "", "", "" }, true)]
    [InlineData(new[] { "a", "b", "a" }, false)]
    public void Contiguity(string[] ids, bool expected) =>
        Assert.Equal(expected, GroupRules.AreContiguous(ids.Select(i => i == "" ? null : i).ToList()));

    [Fact]
    public void MovingIntoGroupInteriorJoinsIt()
    {
        string?[] after = ["a", null, "a", "a"]; // capa movida a la posición 1
        Assert.Equal("a", GroupRules.GroupAfterMove(after, 1, ownGroup: null));
    }

    [Fact]
    public void MovingAwayFromGroupLeavesIt()
    {
        // capa del grupo b movida abajo del todo, lejos de la otra capa de b (arriba)
        string?[] after = ["b", null, "a", "a", "b"];
        Assert.Null(GroupRules.GroupAfterMove(after, 0, ownGroup: "b"));
        // en cambio, si queda junto a otra capa de su grupo, lo conserva
        string?[] adjacent = ["b", "b", "a", "a"];
        Assert.Equal("b", GroupRules.GroupAfterMove(adjacent, 0, ownGroup: "b"));
    }

    [Fact]
    public void InsertJoinsGroupOfLayerBelow()
    {
        string?[] before = [null, "a", "a"];
        Assert.Equal("a", GroupRules.GroupForInsert(before, 2));
        Assert.Null(GroupRules.GroupForInsert(before, 0));
    }

    // Prueba de propiedades: miles de movimientos e inserciones aleatorios nunca rompen la contigüidad
    [Fact]
    public void GroupsStayContiguousUnderRandomMoves()
    {
        var rnd = new Random(2026);
        for (var run = 0; run < 200; run++)
        {
            var ids = new List<string?> { null, "a", "a", "a", null, "b", "b", null };
            for (var step = 0; step < 60; step++)
            {
                if (rnd.NextDouble() < 0.2)
                {
                    var at = rnd.Next(ids.Count + 1);
                    ids.Insert(at, GroupRules.GroupForInsert(ids, at));
                }
                else
                {
                    int from = rnd.Next(ids.Count), to = rnd.Next(ids.Count);
                    var own = ids[from];
                    ids.RemoveAt(from);
                    ids.Insert(to, own);
                    ids[to] = GroupRules.GroupAfterMove(ids, to, own);
                }
                Assert.True(GroupRules.AreContiguous(ids), $"ejecución {run}, paso {step}: {string.Join(",", ids.Select(i => i ?? "-"))}");
            }
        }
    }

    [Fact]
    public void RangeFindsGroupSpan()
    {
        string?[] ids = [null, "a", "a", "a", null];
        Assert.Equal((1, 3), GroupRules.Range(ids, "a"));
        Assert.Null(GroupRules.Range(ids, "z"));
    }
}

/// <summary>FI-01 ampliado: formato v3 con máscaras y grupos.</summary>
public class TrazoFileV3Tests
{
    private static byte[] Png(int w, int h, byte marker = 0) => FI01_PersistenceTests.Png(w, h, marker);

    [Fact]
    public void V3RoundTripWithMasksAndGroups()
    {
        var group = new GroupProperties { Id = "g1", Name = "Personaje", Opacity = 0.7f, Blend = BlendMode.Multiply, Collapsed = true };
        var p = new ProjectData
        {
            Width = 64, Height = 48,
            Groups = [group],
            Layers =
            [
                new(new LayerProperties { Id = "a", Name = "Fondo" }, Png(64, 48, 1)),
                new(new LayerProperties { Id = "b", Name = "Cuerpo", GroupId = "g1" }, Png(64, 48, 2), Png(64, 48, 9)),
                new(new LayerProperties { Id = "c", Name = "Pelo", GroupId = "g1" }, Png(64, 48, 3)),
            ],
        };
        var file = Path.Combine(Path.GetTempPath(), $"trazo-v3-{Guid.NewGuid():N}.trazo");
        try
        {
            ProjectSaver.Save(file, p);
            var r = ProjectSaver.Load(file).Project;
            Assert.Equal([group], r.Groups);
            Assert.Equal("g1", r.Layers[1].Properties.GroupId);
            Assert.Null(r.Layers[0].Properties.GroupId);
            Assert.Equal(9, r.Layers[1].MaskPng![32]);
            Assert.Null(r.Layers[2].MaskPng);
        }
        finally
        {
            foreach (var f in new[] { file, ProjectSaver.BackupPath(file) }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public void MaskWithWrongSizeIsRejected()
    {
        var p = new ProjectData { Width = 64, Height = 48, Layers = [new(new LayerProperties(), Png(64, 48), Png(10, 10))] };
        Assert.Throws<InvalidDataException>(() => ProjectSaver.ValidateLayers(p));
    }

    [Fact]
    public void LayerWithUnknownGroupIsUngroupedOnRead()
    {
        var p = new ProjectData { Width = 64, Height = 48, Layers = [new(new LayerProperties { GroupId = "fantasma" }, Png(64, 48))] };
        using var ms = new MemoryStream();
        TrazoFile.Write(ms, p);
        ms.Position = 0;
        Assert.Null(TrazoFile.Read(ms).Layers[0].Properties.GroupId);
    }

    [Fact]
    public void NonContiguousGroupsAreRejected()
    {
        var p = new ProjectData
        {
            Width = 64, Height = 48, Groups = [new GroupProperties { Id = "g" }],
            Layers =
            [
                new(new LayerProperties { GroupId = "g" }, Png(64, 48)),
                new(new LayerProperties(), Png(64, 48)),
                new(new LayerProperties { GroupId = "g" }, Png(64, 48)),
            ],
        };
        using var ms = new MemoryStream();
        TrazoFile.Write(ms, p);
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => TrazoFile.Read(ms));
    }

    [Fact]
    public void FutureVersionIsRejectedWithClearMessage()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            w.Write("""{"format":"trazo","version":99,"width":64,"height":48,"layers":[]}""");
        ms.Position = 0;
        var e = Assert.Throws<InvalidDataException>(() => TrazoFile.Read(ms));
        Assert.Contains("más nueva", e.Message);
    }
}
