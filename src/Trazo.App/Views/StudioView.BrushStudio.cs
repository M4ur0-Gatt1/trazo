using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Trazo.Core.Brushes;
using Windows.Foundation;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace Trazo.App.Views;

/// <summary>Editor de curvas de dinámica (BR-06) para los ajustes del pincel.</summary>
public sealed partial class StudioView
{
    private const double CurveW = 260, CurveH = 150;

    /// <summary>
    /// Editor de una curva: arrastra los puntos (los extremos solo en vertical), toca dos veces un hueco para
    /// añadir un punto (hasta 8) o un punto interior para quitarlo.
    /// </summary>
    private FrameworkElement CurveEditor(Func<DynamicsCurve> get, Action<DynamicsCurve> set)
    {
        var canvas = new XCanvas
        {
            Width = CurveW, Height = CurveH,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 31, 35)),
        };
        var root = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
        // margen para que las asas de los extremos no queden cortadas
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8), Child = canvas, Padding = new Thickness(9), HorizontalAlignment = HorizontalAlignment.Left,
            Background = canvas.Background, BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
        });

        List<CurvePoint> Points()
        {
            var c = get();
            return c.IsIdentity ? [new(0, 0), new(1, 1)] : [.. c.Points];
        }
        Point ToScreen(CurvePoint p) => new(p.X * CurveW, (1 - p.Y) * CurveH);
        CurvePoint FromScreen(Point s) => new((float)Math.Clamp(s.X / CurveW, 0, 1), (float)Math.Clamp(1 - s.Y / CurveH, 0, 1));

        var dragging = -1;
        void Redraw()
        {
            canvas.Children.Clear();
            var grid = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255));
            for (var k = 1; k < 4; k++)
            {
                canvas.Children.Add(new Line { X1 = CurveW * k / 4, Y1 = 0, X2 = CurveW * k / 4, Y2 = CurveH, Stroke = grid });
                canvas.Children.Add(new Line { X1 = 0, Y1 = CurveH * k / 4, X2 = CurveW, Y2 = CurveH * k / 4, Stroke = grid });
            }
            var curve = get();
            var line = new Polyline { Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 229, 255)), StrokeThickness = 2 };
            for (var i = 0; i <= 64; i++)
            {
                var x = i / 64f;
                line.Points.Add(new Point(x * CurveW, (1 - curve.Evaluate(x)) * CurveH));
            }
            canvas.Children.Add(line);
            foreach (var p in Points())
            {
                var s = ToScreen(p);
                var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Colors.White), Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 229, 255)), StrokeThickness = 2 };
                XCanvas.SetLeft(dot, s.X - 7);
                XCanvas.SetTop(dot, s.Y - 7);
                canvas.Children.Add(dot);
            }
        }

        int Hit(Point s)
        {
            var pts = Points();
            for (var i = 0; i < pts.Count; i++)
            {
                var p = ToScreen(pts[i]);
                if (Math.Abs(p.X - s.X) < 12 && Math.Abs(p.Y - s.Y) < 12) return i;
            }
            return -1;
        }

        void Commit(List<CurvePoint> pts)
        {
            // una recta de (0,0) a (1,1) es la identidad: se guarda vacía
            var identity = pts.Count == 2 && pts[0] == new CurvePoint(0, 0) && pts[1] == new CurvePoint(1, 1);
            if (identity) set(DynamicsCurve.Identity);
            else if (DynamicsCurve.IsValid(pts)) set(new DynamicsCurve { Points = pts });
            Redraw();
        }

        canvas.PointerPressed += (_, e) =>
        {
            dragging = Hit(e.GetCurrentPoint(canvas).Position);
            if (dragging >= 0) canvas.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (dragging < 0) return;
            var pts = Points();
            var p = FromScreen(e.GetCurrentPoint(canvas).Position);
            // los extremos se quedan en x = 0 y x = 1; los demás no cruzan a sus vecinos
            var x = dragging == 0 ? 0 : dragging == pts.Count - 1 ? 1
                : Math.Clamp(p.X, pts[dragging - 1].X + 0.02f, pts[dragging + 1].X - 0.02f);
            pts[dragging] = new CurvePoint(x, p.Y);
            Commit(pts);
            e.Handled = true;
        };
        canvas.PointerReleased += (_, e) =>
        {
            dragging = -1;
            canvas.ReleasePointerCaptures();
        };
        canvas.DoubleTapped += (_, e) =>
        {
            var s = e.GetPosition(canvas);
            var pts = Points();
            var hit = Hit(s);
            if (hit > 0 && hit < pts.Count - 1) pts.RemoveAt(hit);
            else if (hit < 0 && pts.Count < DynamicsCurve.MaxPoints)
            {
                var p = FromScreen(s);
                var at = pts.FindIndex(q => q.X > p.X);
                if (at > 0 && p.X - pts[at - 1].X > 0.02f && pts[at].X - p.X > 0.02f) pts.Insert(at, p);
            }
            Commit(pts);
        };

        var reset = new Button { Content = "Curva lineal", Padding = new Thickness(8, 3, 8, 4), FontSize = 12 };
        reset.Click += (_, _) => { set(DynamicsCurve.Identity); Redraw(); };
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { reset, new TextBlock { Text = "Doble toque: añadir o quitar un punto", Opacity = 0.6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center } },
        });
        Redraw();
        return root;
    }
}
