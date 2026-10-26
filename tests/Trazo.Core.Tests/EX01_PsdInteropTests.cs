using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Trazo.Core.Documents;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>
/// EX-01.8 — Verificación cruzada con ag-psd (tools/psd-interop). Necesita Node y <c>npm ci</c> en esa carpeta; si no
/// están, la prueba no comprueba nada salvo que <c>TRAZO_REQUIRE_INTEROP=1</c> (así se ejecuta en CI).
/// </summary>
public sealed class EX01_PsdInteropTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trazo-ex01-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string? ToolDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var tool = Path.Combine(d.FullName, "tools", "psd-interop");
            if (File.Exists(Path.Combine(tool, "interop.mjs")))
                return Directory.Exists(Path.Combine(tool, "node_modules", "ag-psd")) ? tool : Missing("falta npm ci en tools/psd-interop");
        }
        return Missing("no se encontró tools/psd-interop");
    }

    private static string? Missing(string why) =>
        Environment.GetEnvironmentVariable("TRAZO_REQUIRE_INTEROP") == "1" ? throw new InvalidOperationException(why) : null;

    private static string Node(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo("node") { WorkingDirectory = tool, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        psi.ArgumentList.Add("interop.mjs");
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"node interop.mjs {string.Join(' ', args)}: {stderr}");
        return stdout.Result;
    }

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static readonly Dictionary<string, string> AgNames = new()
    {
        ["norm"] = "normal", ["mul "] = "multiply", ["scrn"] = "screen", ["lum "] = "luminosity",
    };

    [Fact]
    public void AgPsdReadsTrazoExport()
    {
        if (ToolDir() is not { } tool) return;
        var src = EX01_PsdTests.Sample();
        var file = Path.Combine(_dir, "trazo.psd");
        File.WriteAllBytes(file, EX01_PsdTests.Save(src));
        using var json = JsonDocument.Parse(Node(tool, "read", file));
        var root = json.RootElement;
        Assert.Equal(src.Width, root.GetProperty("width").GetInt32());
        Assert.Equal(src.Height, root.GetProperty("height").GetInt32());
        Assert.Equal(Sha(src.Composite), root.GetProperty("composite").GetString());

        // ag-psd da un árbol: Fondo y la carpeta Personaje con Línea y Sombra
        var top = root.GetProperty("children").EnumerateArray().ToList();
        Assert.Equal(2, top.Count);
        AssertLayer(src.Layers[0], top[0]);
        var folder = top[1];
        var f = src.Layers[4];
        Assert.Equal(f.Name, folder.GetProperty("name").GetString());
        Assert.Equal(f.Opacity, folder.GetProperty("opacity").GetInt32());
        Assert.True(folder.GetProperty("hidden").GetBoolean());
        Assert.Equal("screen", folder.GetProperty("blendMode").GetString());
        Assert.False(folder.GetProperty("opened").GetBoolean());
        var inner = folder.GetProperty("children").EnumerateArray().ToList();
        Assert.Equal(2, inner.Count);
        AssertLayer(src.Layers[2], inner[0]);
        AssertLayer(src.Layers[3], inner[1]);
    }

    private static void AssertLayer(PsdLayer l, JsonElement e)
    {
        Assert.Equal(l.Name, e.GetProperty("name").GetString());
        Assert.Equal(l.Opacity, e.GetProperty("opacity").GetInt32());
        Assert.Equal(!l.Visible, e.GetProperty("hidden").GetBoolean());
        Assert.Equal(AgNames[l.BlendKey], e.GetProperty("blendMode").GetString());
        Assert.Equal(l.Clipping, e.GetProperty("clipping").GetBoolean());
        Assert.Equal([l.Left, l.Top, l.Right, l.Bottom], e.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(Sha(l.Rgba), e.GetProperty("rgba").GetString());
        if (l.Mask is { } m)
        {
            var me = e.GetProperty("mask");
            Assert.Equal([m.Left, m.Top, m.Right, m.Bottom], me.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()));
            Assert.Equal(m.DefaultColor, me.GetProperty("defaultColor").GetInt32());
            Assert.Equal(Sha(m.Gray), me.GetProperty("gray").GetString());
        }
        else Assert.False(e.TryGetProperty("mask", out _));
    }

    [Fact]
    public void TrazoReadsAgPsdOutput()
    {
        if (ToolDir() is not { } tool) return;
        var file = Path.Combine(_dir, "ag.psd");
        Node(tool, "write", file);
        var psd = Psd.Read(File.ReadAllBytes(file));
        const int w = 40, h = 30;
        Assert.Equal((w, h), (psd.Width, psd.Height));
        // las mismas fórmulas que interop.mjs
        Assert.Equal(EX01_PsdTests.Rgba(w, h, (x, y) => ((byte)(x * 6), (byte)(y * 8), 90, 255)), psd.Composite);

        var t = Psd.ToTrazo(psd);
        Assert.Equal(["Fondo", "Línea — ñandú", "Sombra", "Luz lineal"], t.Layers.Select(l => l.Layer.Name));
        Assert.Equal([null, 0, 0, null], t.Layers.Select(l => l.Group));
        var g = Assert.Single(t.Groups);
        Assert.Equal(("Personaje", (byte)128, "scrn", true, true), (g.Name, g.Opacity, g.BlendKey, g.Visible, g.Collapsed));

        var fondo = t.Layers[0].Layer;
        Assert.Equal(EX01_PsdTests.Rgba(w, h, (_, _) => (250, 245, 235, 255)), fondo.Rgba);

        var linea = t.Layers[1].Layer;
        Assert.Equal((3, 4, 23, 14), (linea.Left, linea.Top, linea.Right, linea.Bottom));
        Assert.Equal((byte)200, linea.Opacity);
        Assert.Equal((BlendMode.Multiply, false), Psd.FromBlendKey(linea.BlendKey));
        Assert.Equal(EX01_PsdTests.Rgba(20, 10, (x, y) => ((byte)(x * 12), (byte)(y * 25), 7, (byte)(x * 13))), linea.Rgba);
        var m = Assert.IsType<PsdMask>(linea.Mask);
        Assert.Equal((0, 0, w, h, (byte)255), (m.Left, m.Top, m.Right, m.Bottom, m.DefaultColor));
        Assert.Equal(Enumerable.Range(0, w * h).Select(i => (byte)(i % 256)), m.Gray);

        var sombra = t.Layers[2].Layer;
        Assert.True(sombra.Clipping);
        Assert.False(sombra.Visible);
        Assert.Equal((BlendMode.Luminosity, false), Psd.FromBlendKey(sombra.BlendKey));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], sombra.Rgba);

        // luz lineal no existe en Trazo: se aproxima a luz fuerte y se avisa
        Assert.Equal((BlendMode.HardLight, true), Psd.FromBlendKey(t.Layers[3].Layer.BlendKey));
        Assert.Contains(t.Warnings, x => x.Contains("aproximaron"));
    }
}
