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
            ? new[] { "#11151C", "#191F29", "#151B24", "#EEF2F8", "#ABB8CB", "#344052", "#73849D", "#8AB4FF", "#101E38", "#253B5B", "#B8D2FF", "#242D3B", "#344155", "#ABC9FF", "#8A98AD", "#7FCEA3", "#FFB4AB" }
            : new[] { "#F3F5F8", "#FFFFFF", "#F8FAFC", "#172033", "#586579", "#DEE4ED", "#8390A5", "#1D4ED8", "#FFFFFF", "#EAF1FF", "#1D4ED8", "#F0F3F8", "#E0E7F2", "#1E40AF", "#707C8E", "#21734B", "#B42318" };
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        if (highContrast || SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "Canvas", "Surface", "Inset", "Hover", "Pressed" }) Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Text", "Muted", "Line", "ControlLine", "Success", "Danger" }) Resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "Accent", "Selected", "AccentHover" }) Resources[key] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "OnAccent", "SelectedText" }) Resources[key] = SystemColors.HighlightTextBrush;
            Resources["Disabled"] = SystemColors.GrayTextBrush;
        }
    }
}
