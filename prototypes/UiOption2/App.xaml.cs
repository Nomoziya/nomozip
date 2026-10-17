using System.Windows;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

public partial class App : Application
{
    public bool Dark { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ApplyTheme(false);
        var capture = e.Args.Length == 2 && e.Args[0] == "--capture";
        if (capture && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Automated screenshot capture is restricted to isolated CI.");
        var window = new MainWindow(); MainWindow = window;
        if (capture)
            window.ContentRendered += async (_, _) =>
            {
                var passed = await Capture.RunAsync(window, e.Args[1]); Shutdown(passed ? 0 : 1);
            };
        window.Show();
    }
    public void ApplyTheme(bool dark, bool highContrast = false)
    {
        Dark = dark;
        var keys = new[] { "Canvas", "Surface", "Inset", "Text", "Muted", "Line", "ControlLine", "Accent", "OnAccent", "Selected", "SelectedText", "Hover", "Pressed", "AccentHover", "Disabled", "Success", "Danger" };
        var colors = dark
            ? new[] { "#17201C", "#202B24", "#1B251F", "#E4ECE5", "#B1C0B4", "#3C4D40", "#7D9381", "#91BDA1", "#152A1C", "#304A38", "#C2DEC9", "#2A382D", "#3B4F3E", "#ACD0B7", "#8D9F91", "#91CDA4", "#EFB7A9" }
            : new[] { "#ECEFEA", "#FAFBF8", "#F1F4EE", "#28372F", "#596B60", "#D6DED5", "#7B8D7F", "#386653", "#FFFFFF", "#E2EDE5", "#2D5845", "#EAF0E8", "#D8E5DA", "#2B5342", "#6F8072", "#386653", "#A64B39" };
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Resources["ActivitySurface"] = Resources["Selected"]; Resources["ActivityText"] = Resources["SelectedText"];
        if (highContrast || SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "Canvas", "Surface", "Inset", "Hover", "Pressed" }) Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Text", "Muted", "Line", "ControlLine", "Success", "Danger" }) Resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "Accent", "Selected", "AccentHover" }) Resources[key] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "OnAccent", "SelectedText" }) Resources[key] = SystemColors.HighlightTextBrush;
            Resources["Disabled"] = SystemColors.GrayTextBrush;
            Resources["ActivitySurface"] = SystemColors.WindowBrush; Resources["ActivityText"] = SystemColors.WindowTextBrush;
        }
    }
}
