using System.IO;
using System.Windows.Input;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private bool notifyExtractionSuccess;
    private string notificationMessage = "默认关闭；仅整批解压成功时提醒一次，不显示文件名、目录或密码。";
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
            NotificationMessage = settingsWritable ? "已保存通知设置。" : "设置仅在本次使用，原设置文件保持不变。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { NotificationMessage = "本次设置已生效，但无法保存，请检查设置目录权限。"; }
        finally { IsBusy = false; idle.TrySetResult(); }
    }, () => IsIdle);

    private void NotifyCompletedBatch(int count)
    {
        if (!NotifyExtractionSuccess) return;
        var accepted = notificationOverride?.Invoke(count) ?? dialogs.NotifyExtractionComplete(count);
        if (!accepted) NotificationMessage = "解压已完成，但系统未接受通知；结果可在任务列表查看。";
    }
    internal void ConfigureNotificationValidation(Func<int, bool> notify) => notificationOverride = notify;
}
