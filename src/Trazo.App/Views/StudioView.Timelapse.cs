using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Trazo.App.Services;
using Trazo.Core.IO;
using Trazo.Rendering;
using Windows.Storage.Streams;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>TL-01: grabación del time-lapse mientras se pinta, reproductor y exportación a vídeo.</summary>
public sealed partial class StudioView
{
    private TimelapseRecorder? _timelapse;
    private DispatcherQueueTimer? _timelapseTimer;

    private void StartTimelapse(PaintDocument doc)
    {
        StopTimelapse();
        if (doc.FilePath is null) return;
        _timelapse = new TimelapseRecorder(doc, doc.FilePath, () => _settings.RecordTimelapse, (delay, action) =>
        {
            _timelapseTimer ??= DispatcherQueue.CreateTimer();
            _timelapseTimer.Stop();
            _timelapseTimer.Interval = delay;
            _timelapseTimer.IsRepeating = false;
            _timelapseTimer.Tick -= OnTimelapseTick;
            _timelapseTimer.Tick += OnTimelapseTick;
            _timelapseAction = action;
            _timelapseTimer.Start();
        });
    }

    private Action? _timelapseAction;

    private void OnTimelapseTick(DispatcherQueueTimer sender, object args)
    {
        _timelapseAction?.Invoke();
        if (_timelapse?.LastError is { } e)
        {
            Log.Error("Grabación del time-lapse", e);
            StopTimelapse(); // no se insiste: el trabajo sigue sin grabación
        }
    }

    private void StopTimelapse()
    {
        _timelapseTimer?.Stop();
        _timelapse?.Dispose();
        _timelapse = null;
    }

    private async Task ShowTimelapseAsync()
    {
        var doc = _doc;
        if (doc?.FilePath is null) return;
        if (_timelapse is not null)
        {
            _timelapse.Flush();
            await _timelapse.ReleaseAsync();
        }
        var path = TimelapseFile.SidecarPath(doc.FilePath);
        TimelapseReader reader;
        try
        {
            reader = TimelapseReader.Open(path);
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidDataException)
        {
            Window.ShowMessage(_settings.RecordTimelapse
                ? T("Esta obra todavía no tiene time-lapse: se graba mientras pintas.")
                : T("La grabación del time-lapse está desactivada en Acciones › Preferencias."));
            return;
        }
        using (reader)
        {
            if (reader.Frames.Count == 0)
            {
                Window.ShowMessage(T("Esta obra todavía no tiene time-lapse: se graba mientras pintas."));
                return;
            }
            await Dialogs.ShowAsync(XamlRoot, T("Time-lapse"), TimelapsePlayer(reader, doc.Name), T("Cerrar"), "");
        }
    }

    private FrameworkElement TimelapsePlayer(TimelapseReader reader, string name)
    {
        var n = reader.Frames.Count;
        var maxW = 560.0;
        var image = new Image { Width = maxW, Height = maxW * reader.Height / reader.Width, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        var slider = new Slider { Minimum = 0, Maximum = Math.Max(0, n - 1), StepFrequency = 1, Value = n - 1 };
        var play = new Button { Content = new SymbolIcon(Symbol.Play), MinWidth = 44 };
        var info = new TextBlock
        {
            Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Text = F("{0} fotogramas · {1} de trabajo · {2:0.0} MB", n, Duration(reader.Recorded), reader.Bytes / 1048576.0),
        };
        var shown = -1;
        var loading = false;
        async Task ShowFrame(int i)
        {
            if (i == shown || loading) return;
            loading = true;
            try
            {
                var bmp = new BitmapImage();
                using var ms = new InMemoryRandomAccessStream();
                await ms.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(reader.ReadFrame(i)));
                ms.Seek(0);
                await bmp.SetSourceAsync(ms);
                image.Source = bmp;
                shown = i;
            }
            finally
            {
                loading = false;
            }
        }
        slider.ValueChanged += (_, e) => _ = ShowFrame((int)e.NewValue);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.0 / TimelapseSchedule.Fps);
        timer.Tick += (_, _) =>
        {
            if (slider.Value >= n - 1)
            {
                timer.Stop();
                play.Content = new SymbolIcon(Symbol.Play);
                return;
            }
            if (!loading) slider.Value++;
        };
        play.Click += (_, _) =>
        {
            if (timer.IsRunning)
            {
                timer.Stop();
                play.Content = new SymbolIcon(Symbol.Play);
                return;
            }
            if (slider.Value >= n - 1) slider.Value = 0;
            play.Content = new SymbolIcon(Symbol.Pause);
            timer.Start();
        };
        image.Unloaded += (_, _) => timer.Stop();
        _ = ShowFrame(n - 1);

        var progress = new ProgressBar { Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
        Button Export(string label, Func<int[]> schedule, string suffix)
        {
            var b = new Button { Content = label };
            b.Click += async (_, _) =>
            {
                var file = await Window.SavePicker($"{name} {suffix}", T("Vídeo MP4"), ".mp4").PickSaveFileAsync();
                if (file is null) return;
                timer.Stop();
                progress.Visibility = Visibility.Visible;
                progress.Value = 0;
                try
                {
                    await using (var fs = File.Create(file.Path))
                        await TimelapseVideo.ExportAsync(MainWindow.Device, reader, schedule(), fs, new Progress<double>(p => progress.Value = p));
                    Window.ShowMessage(T("Exportado: ") + file.Name);
                }
                catch (Exception e)
                {
                    Log.Error("Exportación del time-lapse", e);
                    Window.ShowMessage(T("No se pudo exportar el vídeo: ") + e.Message, InfoBarSeverity.Error);
                }
                finally
                {
                    progress.Visibility = Visibility.Collapsed;
                }
            };
            return b;
        }
        var controls = new Grid { ColumnSpacing = 10 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.Children.Add(play);
        Grid.SetColumn(slider, 1);
        controls.Children.Add(slider);
        return new StackPanel
        {
            Spacing = 10, Width = maxW,
            Children =
            {
                new Border { CornerRadius = new CornerRadius(8), Child = image, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) },
                controls,
                info,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        Export(T("Exportar vídeo completo…"), () => TimelapseSchedule.Full(n), T("time-lapse")),
                        Export(T("Exportar 30 segundos…"), () => TimelapseSchedule.Short(n), T("time-lapse 30 s")),
                    },
                },
                progress,
            },
        };
    }

    private static string Duration(TimeSpan t) => t.TotalHours >= 1 ? F("{0} h {1:00} min", (int)t.TotalHours, t.Minutes) : F("{0} min {1:00} s", (int)t.TotalMinutes, t.Seconds);
}
