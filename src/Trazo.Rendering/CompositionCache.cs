using Microsoft.Graphics.Canvas;

namespace Trazo.Rendering;

/// <summary>
/// Caché de composición para la vista (PF-01). Mientras se pinta una capa, las unidades de debajo no cambian:
/// se componen una vez en una textura y en cada fotograma solo se componen la unidad modificada y las de encima.
/// Sin vista previa en curso se guarda la imagen completa. Solo afecta a lo que se muestra: guardar y exportar
/// componen siempre sin caché.
/// </summary>
public sealed class CompositionCache : IDisposable
{
    private CanvasRenderTarget? _prefix;
    private PaintDocument? _doc;
    private List<object>? _signature;

    /// <summary>Veces que se ha vuelto a componer la parte fija (para pruebas y mediciones).</summary>
    public int Rebuilds { get; private set; }

    public ICanvasImage Compose(PaintDocument doc, IReadOnlyDictionary<object, ICanvasImage>? overrides, List<IDisposable> owned)
    {
        var units = doc.Units();
        // la parte fija acaba justo debajo de la primera unidad que tiene una capa (o máscara) sustituida
        var fixedUnits = units.Count;
        if (overrides is { Count: > 0 })
        {
            for (var u = 0; u < units.Count && fixedUnits == units.Count; u++)
                for (var i = units[u].From; i <= units[u].To; i++)
                {
                    var l = doc.Layers[i];
                    if (overrides.ContainsKey(l) || overrides.ContainsKey(new MaskKey(l)))
                    {
                        fixedUnits = u;
                        break;
                    }
                }
        }
        if (fixedUnits == 0) return doc.Compose(overrides, owned);

        var signature = new List<object> { fixedUnits, doc.Width, doc.Height, doc.Background, doc.BackgroundVisible };
        for (var u = 0; u < fixedUnits; u++) signature.AddRange(doc.UnitSignature(units[u]));
        if (_prefix is null || !ReferenceEquals(_doc, doc) || _signature is null || !_signature.SequenceEqual(signature) ||
            !ReferenceEquals(_prefix.Device, doc.Device))
        {
            Build(doc, units, fixedUnits);
            _signature = signature;
        }

        ICanvasImage acc = _prefix!;
        for (var u = fixedUnits; u < units.Count; u++) acc = doc.ComposeUnit(units[u], acc, overrides, owned);
        return acc;
    }

    private void Build(PaintDocument doc, List<CompositionUnit> units, int count)
    {
        if (_prefix is null || !ReferenceEquals(_prefix.Device, doc.Device) || _prefix.SizeInPixels.Width != doc.Width || _prefix.SizeInPixels.Height != doc.Height)
        {
            _prefix?.Dispose();
            _prefix = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        }
        var owned = new List<IDisposable>();
        var acc = doc.Backdrop(includeBackground: true, owned);
        for (var u = 0; u < count; u++) acc = doc.ComposeUnit(units[u], acc, null, owned);
        using (var ds = _prefix.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(acc);
        }
        owned.ForEach(o => o.Dispose());
        _doc = doc;
        Rebuilds++;
    }

    /// <summary>Libera la textura (por ejemplo al cerrar el documento o perder la GPU); se recrea al volver a usarla.</summary>
    public void Dispose()
    {
        _prefix?.Dispose();
        _prefix = null;
        _doc = null;
        _signature = null;
    }
}
