using System.Windows.Input;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    public ICommand FileHashCommand { get; private set; } = null!;
    private void InitializeFileHashCommands() => FileHashCommand = new AsyncCommand(() =>
    {
        IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource();
        try { dialogs.ShowFileHashes(SelectedTask?.Path, cancellation.Token); }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; idle.TrySetResult(); }
        return Task.CompletedTask;
    }, () => IsIdle);
}
