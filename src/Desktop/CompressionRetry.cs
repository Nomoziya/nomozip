using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

// Session-only task inputs. Never contains a password or enters saved profiles.
internal sealed record CompressionRetryOptions(IReadOnlyList<string> Sources,string Output,CompressionProfile Profile,bool Sfx,bool Encrypt,bool Headers,
    string ExcludedDirectories,string ExcludedExtensions,Guid PasswordGroup);

public sealed partial class MainViewModel
{
    private Guid compressionAttemptGroup;
    private CompressionRetryOptions CaptureCompressionRetry(IReadOnlyList<string> sources,string output)=>new(sources.ToArray(),output,
        CaptureCompressionProfile(""),CompressionSfx,CompressionEncrypt,CompressionHeaders,compressionExcludedDirectories,compressionExcludedExtensions,compressionAttemptGroup);
    private static bool CanRetryCompression(ArchiveTaskRow row)=>row is {IsCompression:true,Running:false,Completed:false,Failure:not null,CompressionRetry:not null};
    private RelayCommand? editCompressionRetryCommand;
    public ICommand EditCompressionRetryCommand=>editCompressionRetryCommand ??= new RelayCommand(()=>
    {
        if(SelectedTask?.CompressionRetry is not {} options)return;
        CompressionSeparate=false;ApplyCompressionProfile(options.Profile);CompressionSfx=options.Sfx;CompressionEncrypt=options.Encrypt;CompressionHeaders=options.Headers;
        CompressionExcludedDirectories=options.ExcludedDirectories;CompressionExcludedExtensions=options.ExcludedExtensions;
        CompressionSources.Clear();foreach(var source in options.Sources)CompressionSources.Add(new(source));
        compressionPassword=null;Changed(nameof(CompressionPasswordStatus));Changed(nameof(CompressionPasswordSummary));
        CompressionOutput=RetryOutput(options.Output);CompressionMessage=UiText.Get("Compression.RetryLoaded");Page=3;
    },()=>IsIdle && SelectedTask is {} row && CanRetryCompression(row));
    private static string RetryOutput(string output)
    {
        var candidate=output;var number=2;
        while(VolumeCompressionService.HasExistingTarget(candidate))
            candidate=Path.Combine(Path.GetDirectoryName(output)!,Path.GetFileNameWithoutExtension(output)+$" ({number++})"+Path.GetExtension(output));
        return candidate;
    }
    private static Task<CompressionOutcome> CreateCompressionWithOptionsAsync(CompressionRetryOptions options,string output,SfxModule? module,
        ArchiveEngineClient client,string? password,bool replace,IProgress<TaskUpdate> progress,CancellationToken token)
    {
        if(!options.Profile.IsValid || !CompressionExclusions.TryCreate(options.ExcludedDirectories,options.ExcludedExtensions,out var rules)
            || !VolumeSizeOptions.TryGetBytes(options.Profile.VolumeOption,options.Profile.VolumeCustom,options.Profile.VolumeUnit,out var volumeSize))
            return Task.FromResult(new CompressionOutcome(new EngineResult{Code="invalid_compression_options"},output));
        var level=new[]{0,1,5,7,9}[options.Profile.Level];
        return module is not null
            ?new SfxCompressionService(client,client).CreateAsync(options.Sources,output,module,level,password,options.Headers,replace,progress,token,rules)
            :new CompressionService(client,client).CreateAsync(options.Sources,output,CompressionFormats.All[options.Profile.Format].Name,level,password,options.Headers,replace,progress,token,volumeSize,rules);
    }
    private async Task RetryFailedWorkspaceTasksAsync()
    {
        var extraction=Tasks.Where(row=>!row.Completed && row.Failure is {Status:not "cancelled"} && row.Checkpoint is null).ToArray();
        var compression=WorkspaceTasks.Where(row=>CanRetryCompression(row) && row.Failure!.Status!="cancelled").ToArray();
        if(extraction.Length>0){await ExtractTasksAsync(extraction);if(extraction.Any(row=>row.Failure?.Status=="cancelled"))return;}
        if(compression.Length>0)await RetryCompressionTasksAsync(compression);
    }
    private async Task RetryCompressionTasksAsync(IReadOnlyList<ArchiveTaskRow> rows)
    {
        if(!IsIdle || activeEngine.Length==0)return;
        IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();CompressionRunning=true;compressionActivityStarted=true;Changed(nameof(HasCompressionActivity));Page=0;
        PasswordPromptResponse? supplied=null;var suppliedGroup=Guid.Empty;bool? suppliedHeaders=null;ArchiveTaskRow? current=null;var completed=0;var failed=0;var cancelled=false;
        try
        {
            var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),engines.DllPath(activeEngine));
            foreach(var row in rows)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if(row.CompressionRetry is not {} options)continue;
                current=row;SelectedTask=row;row.Failure=null;row.Running=true;row.Percent=0;row.IsIndeterminate=true;row.Status=UiText.Get("Compression.RetryReading");
                var output=RetryOutput(options.Output);options=options with {Output=output};row.CompressionRetry=options;row.SetArchiveResult(output);
                if(options.Encrypt && (supplied is null || suppliedGroup!=options.PasswordGroup))
                {
                    var answer=await dialogs.AskCompressionPasswordAsync(row.Name,allPasswords,vaultAvailable,options.Profile.Format==0?options.Headers:null);
                    supplied=answer?.Password;
                    if(supplied is null){row.Failure=new EngineResult{Status="cancelled",Code="cancelled"};row.Status=UiText.Get("T_8e8fba138ab2");row.Running=false;cancelled=true;break;}
                    suppliedGroup=options.PasswordGroup;suppliedHeaders=answer!.EncryptHeaders;
                }
                if(options.Encrypt && suppliedHeaders is {} headers)options=options with {Headers=headers};
                row.CompressionRetry=options;
                SfxModule? module=options.Sfx?await engines.ReadSfxModuleAsync(activeEngine,cancellation.Token):null;
                var finished=false;
                var progress=new Progress<TaskUpdate>(value=>
                {
                    if(finished)return;
                    row.Status=CompressionMessage=value.Message;row.IsIndeterminate=CompressionIndeterminate=value.Progress is not {Total:>0};
                    row.Percent=CompressionPercent=value.Progress is {Total:>0} state?Math.Min(100,state.Completed*100d/state.Total):0;
                });
                var outcome=await CreateCompressionWithOptionsAsync(options,output,module,client,options.Encrypt?supplied?.Secret:null,false,progress,cancellation.Token);
                finished=true;row.Completed=outcome.Engine.IsSuccess;row.Failure=outcome.Engine.IsSuccess?null:outcome.Engine;row.Running=false;row.IsIndeterminate=false;
                row.Status=outcome.Engine.IsSuccess?UiText.Get("Compression.RetrySucceeded"):outcome.Engine.Status=="cancelled"?UiText.Get("T_8e8fba138ab2"):UiText.Format("Compression.RetryFailure",CompressionError(outcome.Engine.Code));
                if(row.Completed)
                {
                    completed++;row.SetArchiveResult(outcome.OutputPath);row.ResultOutputPath=Path.GetDirectoryName(Path.GetFullPath(outcome.OutputPath));
                    if(options.Encrypt && supplied is not null)supplied=await RecordCompressionPasswordAsync(outcome.OutputPath,supplied);
                }
                else if(outcome.Engine.Status=="cancelled"){cancelled=true;break;}
                else failed++;
            }
            CompressionMessage=cancelled?UiText.Get("T_8e8fba138ab2"):UiText.Format("Compression.RetrySummary",completed,failed);
        }
        catch(OperationCanceledException)
        {if(current is {Running:true}){current.Failure=new EngineResult{Status="cancelled",Code="cancelled"};current.Status=UiText.Get("T_8e8fba138ab2");}CompressionMessage=UiText.Get("T_8e8fba138ab2");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or UpdateException or System.Text.Json.JsonException)
        {if(current is not null){current.Failure=new EngineResult{Code="compression_retry_failed"};current.Status=UiText.Get("Compression.RetryReadFailed");}CompressionMessage=UiText.Get("Compression.RetryReadFailed");}
        finally
        {
            if(current is not null){current.Running=false;current.IsIndeterminate=false;}
            supplied=null;CompressionIndeterminate=false;CompressionRunning=false;Footer=CompressionMessage;
            cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult();CommandManager.InvalidateRequerySuggested();
        }
    }
}
