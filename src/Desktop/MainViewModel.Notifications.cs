using System.IO;
using System.Windows.Input;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private bool notifyExtractionSuccess;
    private string notificationMessage = UiText.Get("T_ac936a188f6d");
    private Func<int, bool>? notificationOverride;
    public bool NotifyExtractionSuccess
    {
        get => notifyExtractionSuccess;
        set { if (!IsBusy) Set(ref notifyExtractionSuccess, value); }
    }
    public string NotificationMessage { get => notificationMessage; private set => Set(ref notificationMessage, value); }
    public ICommand SaveNotificationSettingsCommand { get; private set; } = null!;
    private void InitializeNotificationCommands() => SaveNotificationSettingsCommand = new AsyncCommand(async () =>
    {
        IsBusy = true; idle = new TaskCompletionSource();
        try
        {
            await SaveSettingsAsync();
            NotificationMessage = settingsWritable ? UiText.Get("T_a9e77935df83") : UiText.Get("T_fbda214c62b2");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { NotificationMessage = UiText.Get("T_b93945612611"); }
        finally { IsBusy = false; idle.TrySetResult(); }
    }, () => IsIdle);

    private void NotifyCompletedBatch(int count)
    {
        if (!NotifyExtractionSuccess) return;
        var accepted = notificationOverride?.Invoke(count) ?? dialogs.NotifyExtractionComplete(count);
        if (!accepted) NotificationMessage = UiText.Get("T_7600be19c5ac");
    }
    internal void ConfigureNotificationValidation(Func<int, bool> notify) => notificationOverride = notify;
}
