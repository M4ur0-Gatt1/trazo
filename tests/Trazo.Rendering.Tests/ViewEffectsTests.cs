using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// Regresión (2026-10-25): con el zoom muy alejado, el desenfoque de la sombra del lienzo (12 / zoom)
/// superaba el máximo de Direct2D, el efecto lanzaba una excepción y el lienzo dejaba de redibujarse.
/// </summary>
public class ViewEffectsTests
{
    [Theory]
    [InlineData(0.02f)]   // zoom mínimo (ventana minimizada o pellizco extremo)
    [InlineData(0.0001f)]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    [InlineData(1f)]
    [InlineData(64f)]     // zoom máximo
    public void ShadowBlurIsAlwaysAcceptedByDirect2D(float scale)
    {
        var blur = ViewEffects.ShadowBlur(scale);
        Assert.InRange(blur, 0, ViewEffects.MaxShadowBlur);
        using var target = new CanvasRenderTarget(Gpu.Device, 64, 64, 96);
        using var src = new CanvasCommandList(Gpu.Device);
        using (var ds = src.CreateDrawingSession()) ds.FillRectangle(0, 0, 32, 32, Color.FromArgb(255, 0, 0, 0));
        using var shadow = new ShadowEffect { Source = src, BlurAmount = blur };
        using (var ds = target.CreateDrawingSession()) ds.DrawImage(shadow);
    }

    [Fact]
    public void RawFormulaWouldHaveBeenRejected()
    {
        // documenta el fallo original: 12 / 0,02 = 600 > 250
        using var src = new CanvasCommandList(Gpu.Device);
        var shadow = new ShadowEffect { Source = src };
        Assert.ThrowsAny<Exception>(() => shadow.BlurAmount = 12 / 0.02f);
    }
}
