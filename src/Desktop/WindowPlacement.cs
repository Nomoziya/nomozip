using System.Windows;

namespace ArchivePassword.Desktop;

internal static class WindowPlacement
{
    internal static void FitWorkArea(Window window, Rect workArea)
    {
        window.MinWidth = Math.Min(window.MinWidth, workArea.Width);
        window.MinHeight = Math.Min(window.MinHeight, workArea.Height);
        window.Width = Math.Min(window.Width, workArea.Width);
        window.Height = Math.Min(window.Height, workArea.Height);
    }
}
