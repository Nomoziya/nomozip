using System.IO;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private async Task BrowseArchiveAsync()
    {
        if (SelectedTask is not { } task || !IsIdle) return;
        IsBusy = true; idle = new TaskCompletionSource();
        cancellation = new CancellationTokenSource(); skipPasswords = new CancellationTokenSource();
        try
        {
            var client = new ArchiveEngineClient(Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), engines.DllPath(activeEngine));
            var service = new ArchiveBrowsingService(client, new PasswordCandidateProvider(vault));
            var prompts = 0;
            var progress = new Progress<TaskUpdate>(value => { Footer = $"{task.Name}：{value.Message}"; CanSkipPasswords = value.State == "password"; });
            var outcome = await service.BrowseAsync(task.Path, AutoTry,
                async () => (await dialogs.AskPasswordAsync(task.Name, prompts++ > 0, allPasswords, false, cancellation.Token, browsing: true))?.Secret,
                progress, cancellation.Token, skipPasswords.Token);
            CanSkipPasswords = false;
            if (outcome.Warning is not null) Banner = outcome.Warning;
            Footer = outcome.Engine.IsSuccess ? $"{task.Name}：已读取 {outcome.Engine.Items.Count} 项内容" : outcome.Engine.Status == "cancelled" ? "已取消查看内容。" : ErrorText(outcome.Engine.Code);
            if (outcome.Engine.IsSuccess) dialogs.ShowArchiveContents(new ArchiveContentsViewModel(task.Path,outcome.Engine.Items,
                (selection,status)=>ExtractSelectionAsync(task,client,selection,status),()=>cancellation?.Cancel(),path=>OpenResultDirectories([path])));
        }
        catch (OperationCanceledException) { Footer = "已取消查看内容。"; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Banner = "无法读取压缩包，请检查文件位置与访问权限。"; }
        finally
        {
            CanSkipPasswords = false; cancellation.Dispose(); skipPasswords.Dispose(); cancellation = null; skipPasswords = null;
            IsBusy = false; idle.TrySetResult();
        }
    }
}
