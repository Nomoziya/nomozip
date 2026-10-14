namespace ArchivePassword.Core;

// An in-memory pause request, independent from cancellation and never persisted as a checkpoint.
public sealed class ExtractionPause
{
    private readonly object gate=new();
    private TaskCompletionSource? resumed;
    public event Action? Changed;
    public bool IsRequested {get {lock(gate) return resumed is not null;}}
    public void Pause() {lock(gate) {if(resumed is not null) return;resumed=new(TaskCreationOptions.RunContinuationsAsynchronously);}Changed?.Invoke();}
    public void Resume() {TaskCompletionSource? pending;lock(gate) {pending=resumed;resumed=null;}if(pending is null) return;pending.TrySetResult();Changed?.Invoke();}
    public Task WaitAsync(CancellationToken token=default) {Task wait;lock(gate) wait=resumed?.Task ?? Task.CompletedTask;return wait.WaitAsync(token);}
}
