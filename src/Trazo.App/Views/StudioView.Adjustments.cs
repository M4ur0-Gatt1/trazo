using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Trazo.Core.Imaging;
using Trazo.Rendering;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>Controles de los ajustes de AD-02: curvas por canal, biblioteca de degradados y equilibrio por zonas.</summary>
public sealed partial class StudioView
{
    /// <summary>Añade al panel los controles propios del ajuste; devuelve false si solo lleva deslizadores.</summary>
    private bool AddAdjustmentEditor(AdjustmentSession session)
    {
        switch (session.Adjustment.Editor)
        {
            case AdjustEditor.Curves:
                AddCurvesEditor(session);
                return true;
            case AdjustEditor.GradientMap:
                AddGradientPicker(session);
                return false; // y debajo, el deslizador de intensidad
            case AdjustEditor.ColorBalance:
                AddBalanceEditor(session);
                return true;
            default:
                return false;
        }
    }

    private void AddCurvesEditor(AdjustmentSession session)
    {
        var channel = 0;
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Center };
        void Show() => host.Content = CurveEditor(
            () => session.Inputs.Curves[channel],
            c =>
            {
                session.Inputs.Curves = session.Inputs.Curves.With(channel, c);
                Canvas.Invalidate();
            });
        SheetBody.Children.Add(ChoiceRow([(0, T("Gamma")), (1, T("Rojo")), (2, T("Verde")), (3, T("Azul"))], () => channel, ch =>
        {
            channel = ch;
            Show();
        }));
        Show();
        SheetBody.Children.Add(host);
    }

    private void AddGradientPicker(AdjustmentSession session)
    {
        var reversed = false;
        var base_ = session.Inputs.Gradient;
        var options = GradientMap.Library.Append(GradientMap.FromColor(PaintDocument.ToRgba(CurrentColor))).ToList();
        var panel = new GridView { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0), IsItemClickEnabled = true };
        foreach (var g in options) panel.Items.Add(GradientSwatch(g));
        panel.SelectedIndex = 0;
        void Update()
        {
            session.Inputs.Gradient = reversed ? base_.Reversed() : base_;
            Canvas.Invalidate();
        }
        panel.SelectionChanged += (_, _) =>
        {
            if (panel.SelectedIndex < 0) return;
            base_ = options[panel.SelectedIndex];
            Update();
        };
        var invert = new ToggleSwitch { OnContent = T("Invertir degradado"), OffContent = T("Invertir degradado"), MinWidth = 0 };
        invert.Toggled += (_, _) =>
        {
            reversed = invert.IsOn;
            Update();
        };
        SheetBody.Children.Add(panel);
        SheetBody.Children.Add(invert);
    }

    /// <summary>Muestra de un degradado: la barra con sus colores y el nombre debajo.</summary>
    private static FrameworkElement GradientSwatch(GradientMap g)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        foreach (var s in g.Stops) brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = s.Position, Color = PaintDocument.ToColor(s.Color) });
        return new StackPanel
        {
            Width = 104, Spacing = 4, Margin = new Thickness(2),
            Children =
            {
                new Border { Height = 26, CornerRadius = new CornerRadius(6), Background = brush },
                new TextBlock { Text = g.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
    }

    private void AddBalanceEditor(AdjustmentSession session, string zone = "m")
    {
        var rows = new StackPanel { Spacing = 2 };
        void Show()
        {
            rows.Children.Clear();
            for (var axis = 0; axis < 3; axis++)
            {
                var key = zone + axis;
                var p = session.Adjustment.Params.First(x => x.Key == key);
                rows.Children.Add(SliderRow(p.Label, p.Min, p.Max, session.Values[key], v => F("{0:+0;-0;0}", v), v => session.Values[key] = v));
            }
        }
        SheetBody.Children.Add(ChoiceRow([("s", T("Sombras")), ("m", T("Medios tonos")), ("h", T("Luces"))], () => zone, z =>
        {
            zone = z;
            Show();
        }));
        Show();
        SheetBody.Children.Add(rows);
    }
}
