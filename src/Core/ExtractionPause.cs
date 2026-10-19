namespace ArchivePassword.Core;

// An in-memory pause request, independent from cancellation and never persisted as a checkpoint.
public sealed class ExtractionPause : IDisposable
{
    private readonly ExtractionPause? parent;
    public ExtractionPause(ExtractionPause? parent = null)
    {
        this.parent = parent;
        if (parent is not null) parent.Changed += ParentChanged;
    }
    private void ParentChanged() => Changed?.Invoke();
    private readonly object gate=new();
    private TaskCompletionSource? resumed;
    public event Action? Changed;
    public bool IsRequested {get {lock(gate) return resumed is not null || parent?.IsRequested == true;}}
    public void Pause() {lock(gate) {if(resumed is not null) return;resumed=new(TaskCreationOptions.RunContinuationsAsynchronously);}Changed?.Invoke();}
    public void Resume() {TaskCompletionSource? pending;lock(gate) {pending=resumed;resumed=null;}if(pending is null) return;pending.TrySetResult();Changed?.Invoke();}
    public async Task WaitAsync(CancellationToken token=default)
    {
        do
        {
            Task wait; lock(gate) wait=resumed?.Task ?? Task.CompletedTask;
            await wait.WaitAsync(token);
            if (parent is not null) await parent.WaitAsync(token);
        } while (IsRequested);
    }
    public void Dispose() { if (parent is not null) parent.Changed -= ParentChanged; Resume(); }
}
