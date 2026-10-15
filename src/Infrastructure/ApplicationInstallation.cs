using System.Diagnostics;
using Microsoft.Win32;

namespace ArchivePassword.Infrastructure;

public static class ApplicationInstallation
{
    public static bool IsRegistered(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\nomozip");
        return key?.GetValue("InstallLocation") is string location && Matches(executable, location);
    }
    public static bool Matches(string executable, string location) => Path.IsPathFullyQualified(location) &&
        string.Equals(Path.GetFullPath(executable), Path.GetFullPath(Path.Combine(location, "ArchivePassword.exe")), StringComparison.OrdinalIgnoreCase);

    public static ProcessStartInfo CreateStart(string installer, string directory)
    {
        if (!Path.IsPathFullyQualified(installer) || !Path.IsPathFullyQualified(directory) || directory.Contains('"'))
            throw new ArgumentException("Installer paths must be absolute.");
        // NSIS /D must be the last argument, without quotes even when it contains spaces.
        return new ProcessStartInfo(Path.GetFullPath(installer))
        { UseShellExecute = false, Arguments = "/S /UPDATE /D=" + Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) };
    }
}
