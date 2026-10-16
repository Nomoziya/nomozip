using System.Windows;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed record AppearanceOption(string Code, string Label);
public sealed partial class MainViewModel
{
    public IReadOnlyList<AppearanceOption> AppearanceOptions { get; } = [new("system", UiText.Get("Appearance.System")), new("light", UiText.Get("Appearance.Light")), new("dark", UiText.Get("Appearance.Dark")),new("soft",UiText.Get("Appearance.Soft"))];
    private AppearanceOption selectedAppearance = null!;
    public AppearanceOption SelectedAppearance { get => selectedAppearance; set { if (value is not null && AppearanceOptions.Contains(value)) Set(ref selectedAppearance, value); } }
    private string appearanceMessage = "";
    public string AppearanceMessage { get => appearanceMessage; private set => Set(ref appearanceMessage, value); }
    public ICommand SaveAppearanceCommand { get; private set; } = null!;
    private void InitializeAppearanceCommands()
    {
        var stored = AppearancePreferenceStore.Read(dataDirectory);
        selectedAppearance = AppearanceOptions.Single(option => option.Code == stored.Mode);
        if (!stored.CanWrite) appearanceMessage = UiText.Get("Appearance.Preserved");
        SaveAppearanceCommand = new AsyncCommand(async () =>
        {
            try
            {
                await AppearancePreferenceStore.SaveAsync(dataDirectory, SelectedAppearance.Code);
                ((App)Application.Current).ApplyAppearanceMode(SelectedAppearance.Code);
                AppearanceMessage = UiText.Get("Appearance.Saved");
            }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
            { AppearanceMessage = UiText.Get("Appearance.Failed"); }
        }, () => IsIdle);
    }
}
