using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.App.Services;
using Trazo.Core.Documents;
using Trazo.Rendering;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>RS-01: duplicar la resolución del lienzo para que lo que se dibuje no se vea en escalones al acercarse.</summary>
public sealed partial class StudioView
{
    private async Task DoubleResolutionAsync()
    {
        var doc = _doc;
        if (doc is null) return;
        if (!CanvasLimits.CanScale(doc.Width, doc.Height, 2, doc.Layers.Count))
        {
            Window.ShowMessage(F("El lienzo ({0}×{1}, {2} capas) no se puede duplicar: pasaría del tamaño máximo.", doc.Width, doc.Height, doc.Layers.Count), InfoBarSeverity.Warning);
            return;
        }
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
            Text = F("El lienzo pasa de {0}×{1} a {2}×{3} píxeles. Lo que dibujes desde ahora se verá nítido al acercarte; lo ya dibujado se agranda tal como está.", doc.Width, doc.Height, doc.Width * 2, doc.Height * 2)
                + "\n\n" + T("El historial de deshacer se vacía. La versión anterior queda en la copia .bak hasta el próximo guardado."),
        };
        if (await Dialogs.ShowAsync(XamlRoot, T("Duplicar la resolución"), text, T("Duplicar")) != ContentDialogResult.Primary) return;
        if (_doc != doc) return;
        EndOpenTools();
        CancelInput();
        await SaveAsync(); // lo último queda en el archivo antes de cambiar de documento
        if (_doc != doc) return;
        PaintDocument bigger;
        try
        {
            bigger = doc.Resampled(2);
        }
        catch (Exception e)
        {
            Log.Error("Duplicar la resolución", e);
            Window.ShowMessage(T("No se pudo duplicar la resolución: ") + e.Message, InfoBarSeverity.Error);
            return;
        }
        var (scale, offset, rotation) = (_view.Scale, _view.Offset, _view.Rotation);
        StopTimelapse();
        _composition.Dispose();
        PruneThumbnails(null);
        Open(bigger); // libera el documento anterior
        // misma vista: el doble de píxeles a la mitad de zoom
        _fitPending = false;
        (_view.Scale, _view.Offset, _view.Rotation) = (scale / 2, offset, rotation);
        RefreshLayersPanel();
        Canvas.Invalidate();
        await SaveAsync();
        Log.Info($"Resolución duplicada: {bigger.Width}×{bigger.Height}");
        Window.ShowMessage(F("Resolución duplicada: {0}×{1}.", bigger.Width, bigger.Height));
    }
}
