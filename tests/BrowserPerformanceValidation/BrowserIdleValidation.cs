using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Desktop;

internal static class BrowserIdleValidation
{
    internal static void Run()
    {
        var previous=SynchronizationContext.Current;
        var dispatcher=Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var finished=new TaskCompletionSource();
        _=Task.Run(async()=>{await Task.WhenAny(finished.Task,Task.Delay(5000));if(!finished.Task.IsCompleted){Console.Error.WriteLine("Working-state observer blocked while waiting for idle.");Environment.Exit(1);}});
        try
        {
            var frame=new DispatcherFrame();var task=Observe();
            task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false));
            Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
        }
        finally {finished.TrySetResult();SynchronizationContext.SetSynchronizationContext(previous);}
    }
    private static async Task Observe()
    {
        var model=new ArchiveContentsViewModel("C:/synthetic/selection.zip",[new ArchiveEntry(0,"synthetic.txt",1,false,false)],
            async(_,_)=>{await Task.Delay(20);return "C:/synthetic/output";});
        Task? observed=null;
        model.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(model.IsWorking) && model.IsWorking)observed=model.WaitForIdleAsync();};
        model.Rows[0].Selected=true;model.ExtractSelectedCommand.Execute(null);
        if(observed is null)throw new Exception("No working-state notification.");
        await observed;
        if(model.IsWorking || model.OutputPath.Length==0)throw new Exception("Idle notification preceded the selection result.");
    }
}
