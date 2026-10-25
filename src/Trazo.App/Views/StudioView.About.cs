using System.Reflection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Trazo.App.Views;

/// <summary>Ventana «Acerca de Trazo»: versión, licencia, enlaces y avisos de terceros.</summary>
public sealed partial class StudioView
{
    /// <summary>Versión legible: «1.0.0 (399ed9c)» a partir de la versión informativa «1.0.0+399ed9c…».</summary>
    public static string DisplayVersion
    {
        get
        {
            var info = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var parts = info.Split('+');
            var version = parts[0].Length > 0 ? parts[0] : typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";
            return parts.Length > 1 && parts[1].Length >= 7 ? $"{version} ({parts[1][..7]})" : version;
        }
    }

    private async Task AboutAsync()
    {
        var legal = Path.Combine(AppContext.BaseDirectory, "Assets", "Legal");
        var content = new StackPanel { Spacing = 10, Width = 460 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        header.Children.Add(new Image { Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"))), Width = 64, Height = 64 });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Trazo", FontSize = 24, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new TextBlock { Text = "Versión " + DisplayVersion, Opacity = 0.7, IsTextSelectionEnabled = true });
        header.Children.Add(title);
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = "Estudio de pintura digital para Windows.\n© 2026 Mauro Gatti · Software libre con licencia MIT.",
            TextWrapping = TextWrapping.Wrap,
        });

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        void Link(string text, string url) =>
            links.Children.Add(new HyperlinkButton { Content = text, NavigateUri = new Uri(url), Padding = new Thickness(6, 2, 6, 2) });
        Link("Código y novedades", "https://github.com/M4ur0-Gatt1/trazo");
        Link("Informar un problema", "https://github.com/M4ur0-Gatt1/trazo/issues/new/choose");
        Link("Privacidad", "https://m4ur0-gatt1.github.io/trazo/privacidad.html");
        content.Children.Add(links);

        // avisos de terceros, leídos del archivo que acompaña a la app
        var notices = Path.Combine(legal, "THIRD-PARTY-NOTICES.md");
        var text = File.Exists(notices) ? await File.ReadAllTextAsync(notices) : "No se encontraron los avisos de terceros.";
        content.Children.Add(new Expander
        {
            Header = "Avisos de terceros y licencias",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer
            {
                MaxHeight = 260,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextSelectionEnabled = true },
            },
        });
        var logs = new Button { Content = "Abrir la carpeta de registros", Padding = new Thickness(10, 4, 10, 5) };
        ToolTipService.SetToolTip(logs, "Adjunta el registro más reciente al informar un problema");
        logs.Click += (_, _) =>
        {
            Directory.CreateDirectory(Services.Log.Folder);
            System.Diagnostics.Process.Start("explorer.exe", Services.Log.Folder);
        };
        content.Children.Add(logs);
        await Dialogs.ShowAsync(XamlRoot, "Acerca de Trazo", content, "Cerrar", "");
    }
}
