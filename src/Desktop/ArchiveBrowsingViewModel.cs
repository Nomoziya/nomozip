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
            var client = CreateReadClient(task);
            var service = new ArchiveBrowsingService(client, new PasswordCandidateProvider(vault));
            var prompts = 0;
            var progress = new Progress<TaskUpdate>(value => { Footer = UiText.Format("Common.TaskMessage", task.Name, value.Message); CanSkipPasswords = value.State == "password"; });
            var outcome = await service.BrowseAsync(task.Path, AutoTry,
                async () => (await dialogs.AskPasswordAsync(task.Name, prompts++ > 0, allPasswords, false, cancellation.Token, browsing: true))?.Secret,
                progress, cancellation.Token, skipPasswords.Token);
            CanSkipPasswords = false;
            if (outcome.Warning is not null) Banner = outcome.Warning;
            Footer = outcome.Engine.IsSuccess ? UiText.Format("T_58db5ae70926", task.Name, outcome.Engine.Items.Count) : outcome.Engine.Status == "cancelled" ? UiText.Get("T_f23f6d1addfc") : ErrorText(outcome.Engine.Code);
            if (outcome.Engine.IsSuccess) dialogs.ShowArchiveContents(new ArchiveContentsViewModel(task.Path,outcome.Engine.Items,
                (selection,status)=>ExtractSelectionAsync(task,client,selection,status),()=>cancellation?.Cancel(),path=>OpenResultDirectories([path]),
                outcome.Engine.Format,(action,snapshot,selection,status)=>EditArchiveAsync(task,client,action,snapshot,selection,status)));
        }
        catch (OperationCanceledException) { Footer = UiText.Get("T_f23f6d1addfc"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Banner = UiText.Get("T_00882210530c"); }
        finally
        {
            CanSkipPasswords = false; cancellation.Dispose(); skipPasswords.Dispose(); cancellation = null; skipPasswords = null;
            IsBusy = false; idle.TrySetResult();
        }
    }
}
