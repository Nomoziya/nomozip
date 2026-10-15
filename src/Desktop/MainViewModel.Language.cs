using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed record LanguageOption(string Code, string Label);
public sealed partial class MainViewModel
{
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = [new("zh-CN", "简体中文"), new("en", "English")];
    private LanguageOption selectedLanguage = new("zh-CN", "简体中文");
    public LanguageOption SelectedLanguage { get => selectedLanguage; set { if (value is not null && LanguageOptions.Contains(value)) Set(ref selectedLanguage, value); } }
    private string languageMessage = UiText.Get("Language.RestartHint");
    public string LanguageMessage { get => languageMessage; private set => Set(ref languageMessage, value); }
    public ICommand SaveLanguageCommand { get; private set; } = null!;
    private void InitializeLanguageCommands()
    {
        var stored = LanguagePreferenceStore.Read(dataDirectory);
        selectedLanguage = LanguageOptions.Single(option => option.Code == stored.Language);
        if (!stored.CanWrite) languageMessage = UiText.Get("Language.Preserved");
        SaveLanguageCommand = new AsyncCommand(async () =>
        {
            try
            {
                await LanguagePreferenceStore.SaveAsync(dataDirectory, SelectedLanguage.Code);
                LanguageMessage = UiText.Get("Language.Saved");
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            { LanguageMessage = UiText.Get("Language.SaveFailed"); }
        }, () => IsIdle);
    }
}
