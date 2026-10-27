using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Trazo.Core.Colors;

namespace Trazo.Core.Tests;

/// <summary>CO-02 — Panel de color: armonía y paletas (docs/specs/CO-02-color-panel.md), parte sin GPU.</summary>
public sealed class CO02_ColorPanelTests
{
    // ---------- CO-02.1 ----------

    [Fact]
    public void HarmoniesFollowTheirAngles()
    {
        var main = new Hsv(350, 0.7, 0.6);
        var expected = new Dictionary<HarmonyMode, double[]>
        {
            [HarmonyMode.Complementary] = [350, 170],
            [HarmonyMode.SplitComplementary] = [350, 140, 200],
            [HarmonyMode.Analogous] = [350, 320, 20],
            [HarmonyMode.Triadic] = [350, 110, 230],
            [HarmonyMode.Tetradic] = [350, 50, 170, 230],
        };
        foreach (var (mode, hues) in expected)
        {
            var colors = ColorHarmony.Colors(main, mode);
            Assert.Equal(hues.Length, colors.Length);
            for (var i = 0; i < hues.Length; i++)
            {
                Assert.Equal(hues[i], colors[i].H, 6);
                Assert.Equal((0.7, 0.6), (colors[i].S, colors[i].V));
            }
            Assert.False(string.IsNullOrWhiteSpace(ColorHarmony.Name(mode)));
        }
        // tono fuera de rango a la entrada: se normaliza
        Assert.All(ColorHarmony.Colors(new Hsv(-30, 1, 1), HarmonyMode.Triadic), c => Assert.InRange(c.H, 0, 359.999));
        Assert.Equal(330, ColorHarmony.Colors(new Hsv(-30, 1, 1), HarmonyMode.Complementary)[0].H, 6);
    }

    // ---------- CO-02.2 ----------

    private static readonly Rgba[] Sample = [new(255, 0, 0), new(12, 200, 99), new(0, 0, 0), new(255, 255, 255), new(128, 64, 200), new(7, 7, 7)];

    [Fact]
    public void SwatchesRoundTripAndProcreateQuirks()
    {
        using var ms = new MemoryStream();
        PaletteFiles.WriteSwatches(new Palette("Atardecer ñ", Sample), ms);
        ms.Position = 0;
        var back = PaletteFiles.ReadSwatches(ms, "x");
        Assert.Equal("Atardecer ñ", back.Name);
        Assert.Equal(Sample.Length, back.Colors.Count);
        for (var i = 0; i < Sample.Length; i++)
        {
            Assert.InRange(Math.Abs(Sample[i].R - back.Colors[i].R), 0, 1);
            Assert.InRange(Math.Abs(Sample[i].G - back.Colors[i].G), 0, 1);
            Assert.InRange(Math.Abs(Sample[i].B - back.Colors[i].B), 0, 1);
        }

        // como lo escribe Procreate: huecos null, objeto suelto, valores fuera de rango y sin nombre
        var json = """[{"swatches":[null,{"hue":0,"saturation":1,"brightness":1,"alpha":1,"colorSpace":0},null,{"hue":1.5,"saturation":-2,"brightness":0.5}],"name":""}]""";
        var p = PaletteFiles.ReadSwatches(Zip("Swatches.json", json), "Importada");
        Assert.Equal("Importada", p.Name);
        Assert.Equal([new Rgba(255, 0, 0), new Rgba(128, 128, 128)], p.Colors);
        var single = PaletteFiles.ReadSwatches(Zip("carpeta/Swatches.json", """{"name":"Suelta","swatches":[]}"""), "x");
        Assert.Equal("Suelta", single.Name);
        Assert.Empty(single.Colors);
    }

    private static MemoryStream Zip(string entry, string content)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(z.CreateEntry(entry).Open())) w.Write(content);
        ms.Position = 0;
        return ms;
    }

    // ---------- CO-02.3 ----------

    /// <summary>Escritor de .ase a mano, para fabricar lo que escriben otras aplicaciones.</summary>
    private sealed class Ase
    {
        private readonly List<byte[]> _blocks = [];

        private static byte[] Name(string s)
        {
            var n = Encoding.BigEndianUnicode.GetBytes(s + "\0");
            var o = new byte[2 + n.Length];
            BinaryPrimitives.WriteUInt16BigEndian(o, (ushort)(n.Length / 2));
            n.CopyTo(o, 2);
            return o;
        }

        private Ase Block(ushort type, byte[] body)
        {
            var o = new byte[6 + body.Length];
            BinaryPrimitives.WriteUInt16BigEndian(o, type);
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(2), (uint)body.Length);
            body.CopyTo(o, 6);
            _blocks.Add(o);
            return this;
        }

        public Ase Group(string name) => Block(0xC001, Name(name));
        public Ase End() => Block(0xC002, []);

        public Ase Color(string name, string model, params float[] v)
        {
            var body = new List<byte>(Name(name));
            body.AddRange(Encoding.ASCII.GetBytes(model));
            foreach (var f in v)
            {
                var b = new byte[4];
                BinaryPrimitives.WriteSingleBigEndian(b, f);
                body.AddRange(b);
            }
            body.AddRange(new byte[] { 0, 2 });
            return Block(0x0001, [.. body]);
        }

        public byte[] Bytes()
        {
            var head = new byte[12];
            "ASEF"u8.CopyTo(head);
            BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), (uint)_blocks.Count);
            return [.. head, .. _blocks.SelectMany(b => b)];
        }
    }

    [Fact]
    public void AseRoundTripGroupsAndModels()
    {
        using var ms = new MemoryStream();
        PaletteFiles.WriteAse(new Palette("Piel", Sample), ms);
        var back = PaletteFiles.ReadAse(ms.ToArray(), "x");
        var p = Assert.Single(back.Palettes);
        Assert.Equal("Piel", p.Name);
        Assert.Equal(Sample, p.Colors);
        Assert.False(back.Approximated);

        // colores sueltos y dos grupos, con todos los modelos
        var file = new Ase()
            .Color("suelto", "RGB ", 0, 0, 1)
            .Group("Cálidos").Color("rojo", "CMYK", 0, 1, 1, 0).Color("gris", "Gray", 0.5f).End()
            .Group("Lab").Color("neutro", "LAB ", 0.5f, 0, 0).Color("blanco", "LAB ", 1, 0, 0).End()
            .Bytes();
        var r = PaletteFiles.ReadAse(file, "Mis muestras");
        Assert.True(r.Approximated);
        Assert.Equal(["Mis muestras", "Cálidos", "Lab"], r.Palettes.Select(x => x.Name));
        Assert.Equal([new Rgba(0, 0, 255)], r.Palettes[0].Colors);
        Assert.Equal([new Rgba(255, 0, 0), new Rgba(128, 128, 128)], r.Palettes[1].Colors);
        // L = 50 neutro es el gris sRGB 119 (± 1); L = 100 es blanco
        var neutral = r.Palettes[2].Colors[0];
        Assert.InRange(neutral.R, 118, 120);
        Assert.Equal(neutral.R, neutral.G);
        Assert.InRange(Math.Abs(neutral.B - neutral.R), 0, 1);
        Assert.Equal(new Rgba(255, 255, 255), r.Palettes[2].Colors[1]);
    }

    // ---------- CO-02.4 ----------

    [Fact]
    public void MalformedPaletteFilesFailCleanly()
    {
        using var ms = new MemoryStream();
        PaletteFiles.WriteAse(new Palette("x", Sample), ms);
        var ase = ms.ToArray();
        var rng = new Random(27);
        var cases = 0;
        void Try(Action a)
        {
            cases++;
            try { a(); }
            catch (InvalidDataException) { }
        }
        for (var n = 0; n < ase.Length; n++) Try(() => PaletteFiles.ReadAse(ase[..n], "x"));
        for (var k = 0; k < 3000; k++)
        {
            var bad = (byte[])ase.Clone();
            bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            Try(() => PaletteFiles.ReadAse(bad, "x"));
        }
        using var sw = new MemoryStream();
        PaletteFiles.WriteSwatches(new Palette("x", Sample), sw);
        var swatches = sw.ToArray();
        for (var n = 0; n < swatches.Length; n += 3) Try(() => PaletteFiles.ReadSwatches(new MemoryStream(swatches[..n]), "x"));
        for (var k = 0; k < 1500; k++)
        {
            var bad = (byte[])swatches.Clone();
            bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            Try(() => PaletteFiles.ReadSwatches(new MemoryStream(bad), "x"));
        }
        // JSON raro dentro de un ZIP válido
        foreach (var json in new[] { "", "{", "42", "[]", """[{"swatches":{"a":1}}]""", """{"name":5,"swatches":[1,"a",true]}""" })
            Try(() => PaletteFiles.ReadSwatches(Zip("Swatches.json", json), "x"));
        Assert.Throws<InvalidDataException>(() => PaletteFiles.ReadSwatches(Zip("otra.json", "{}"), "x"));
        Assert.True(cases > 4000);

        // nombres y número de colores acotados
        var many = new Ase().Group(new string('a', 500) + "\u0007");
        for (var i = 0; i < 300; i++) many.Color("c", "RGB ", 0.1f, 0.2f, 0.3f);
        var big = PaletteFiles.ReadAse(many.End().Bytes(), "x");
        Assert.Equal(PaletteFiles.MaxName, big.Palettes[0].Name.Length);
        Assert.Equal(PaletteFiles.MaxColors, big.Palettes[0].Colors.Count);
        Assert.Throws<InvalidDataException>(() => PaletteFiles.ReadAse(new Ase().Group("vacío").End().Bytes(), "x"));
        Assert.Throws<InvalidDataException>(() => PaletteFiles.ReadAse(new Ase().Color("raro", "HSV ", 1, 1, 1).Bytes(), "x"));
    }

    // ---------- CO-02.5 ----------

    [Fact]
    public void OldPaletteMigratesAndDefaultIsKept()
    {
        var palettes = new List<PaletteData>();
        string? def = null;
        PaletteSet.Migrate(palettes, ["#ff0000", "#00ff00"], ["#000000", "#ffffff"], ref def);
        Assert.Equal(2, palettes.Count);
        Assert.Equal(["#000000", "#ffffff"], palettes[0].Colors);
        Assert.Equal(["#ff0000", "#00ff00"], palettes[1].Colors);
        // la propia, con colores, pasa a ser la predeterminada
        Assert.Equal(palettes[1].Id, def);
        // migrar otra vez no duplica
        PaletteSet.Migrate(palettes, ["#123456"], ["#000000"], ref def);
        Assert.Equal(2, palettes.Count);
        // renombrar no cambia la predeterminada; eliminar otra tampoco
        palettes[1].Name = "Otra";
        palettes.RemoveAt(0);
        Assert.Equal("Otra", PaletteSet.Default(palettes, def)!.Name);
        // si se elimina la predeterminada, pasa a ser la primera
        palettes.Add(new PaletteData { Name = "Nueva" });
        palettes.RemoveAt(0);
        Assert.Equal("Nueva", PaletteSet.Default(palettes, def)!.Name);
        // sin paleta propia, la predeterminada es la de Trazo
        var fresh = new List<PaletteData>();
        string? d2 = null;
        PaletteSet.Migrate(fresh, [], ["#000000"], ref d2);
        Assert.Equal(fresh[0].Id, d2);
        // ida y vuelta entre preferencias y paleta
        var round = PaletteSet.ToPalette(PaletteSet.From(new Palette("p", Sample)));
        Assert.Equal(Sample, round.Colors);
    }
}
