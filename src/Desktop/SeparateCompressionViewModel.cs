using System.IO;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private bool compressionSeparate;
    public bool CompressionSeparate
    {
        get=>compressionSeparate;
        set
        {
            if(!IsIdle || !Set(ref compressionSeparate,value))return;
            InvalidateCompressionContents();
            CompressionCapabilitiesChanged();Changed(nameof(CompressionSourceSummary));Changed(nameof(CompressionOutputFileName));
            Changed(nameof(CompressionOutputTarget));Changed(nameof(CompressionNameInput));
        }
    }
    public bool CanNameCompression=>IsIdle && !CompressionSeparate;
    public string CompressionNameInput {get=>CompressionSeparate?UiText.Get("Compression.AutomaticNames"):CompressionOutputName;set {if(!CompressionSeparate)CompressionOutputName=value;}}
    public string CompressionOutputTarget=>CompressionSeparate?CompressionOutputDirectory:CompressionOutput;
    private string SeparateSourceError
    {
        get
        {
            if(CompressionOptions.SingleFile && CompressionSources.Any(source=>source.IsDirectory))return UiText.Get("Compression.SeparateFilesOnly");
            if(string.IsNullOrWhiteSpace(CompressionOutputDirectory))return "";
            try
            {
                return CompressionSources.Any(source=>source.IsDirectory && SeparateCompressionPlan.IsWithin(CompressionOutputDirectory,source.Path))
                    ?UiText.Get("Compression.SeparateOutsideSources"):"";
            }
            catch(Exception error) when(error is ArgumentException or NotSupportedException or PathTooLongException)
            {return UiText.Get("Compression.SeparateInvalidDirectory");}
        }
    }
    private Task<CompressionOutcome> CreateCompressionItemAsync(IReadOnlyList<string> sources,string output,SfxModule? module,
        ArchiveEngineClient client,bool replace,ulong volumeSize,IProgress<TaskUpdate> progress,CancellationToken token)
        =>CreateCompressionWithOptionsAsync(CaptureCompressionRetry(sources,output),output,module,client,CompressionEncrypt?compressionPassword?.Secret:null,replace,progress,token);

    private async Task RecordCompressionPasswordAsync(string output)
    {
        if(!CompressionEncrypt || compressionPassword is not {} supplied)return;
        compressionPassword=await RecordCompressionPasswordAsync(output,supplied);
    }
    private async Task<PasswordPromptResponse> RecordCompressionPasswordAsync(string output,PasswordPromptResponse supplied)
    {
        if(!supplied.Save && supplied.SavedId is null)return supplied;
        try
        {
            var id=supplied.SavedId ?? Guid.NewGuid();
            if(supplied.SavedId is null)await vault.SaveAsync(new SavedPassword(id,string.IsNullOrWhiteSpace(supplied.Name)?Path.GetFileName(output):supplied.Name,supplied.Secret,"",true,DateTimeOffset.UtcNow));
            await vault.RecordSuccessAsync(id,output);await RefreshVaultAsync();
            return supplied with {SavedId=id};
        }
        catch(Exception error) when(error is VaultException or IOException or UnauthorizedAccessException) {Banner=UiText.Get("T_0346e5dcf690");}
        return supplied;
    }
    private async Task CreateSeparateArchivesAsync()
    {
        if(CompressionSourceError.Length>0){CompressionMessage=CompressionSourceError;return;}
        if(!VolumeSizeOptions.TryGetBytes(volumeOption,volumeCustom,volumeUnit,out var volumeSize)){CompressionMessage=CompressionVolumeError;return;}
        IReadOnlyList<SeparateCompressionItem> plan;
        try {plan=SeparateCompressionPlan.Create(CompressionSources.Select(source=>source.Path),CompressionOutputDirectory,CompressionExtension);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {CompressionMessage=error.Message=="batch_output_inside_source"?UiText.Get("Compression.SeparateOutsideSources"):UiText.Get("Compression.SeparateInvalidDirectory");return;}
        lastCompressionProfile=CaptureCompressionProfile("");IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();
        var completed=0;var failed=0;ArchiveTaskRow? current=null;
        try
        {
            if(!await PersistCompressionProfilesAsync())Banner=UiText.Get("Compression.ProfileSaveFailed");
            SfxModule? module=null;
            if(CompressionSfx)
            {
                try {module=await engines.ReadSfxModuleAsync(activeEngine,cancellation.Token);}
                catch(Exception error) when(error is UpdateException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {CompressionMessage=UiText.Get("T_0c3a93b2ceab");return;}
            }
            if(CompressionEncrypt && compressionPassword is null)await SetCompressionPasswordAsync();
            if(CompressionEncrypt && compressionPassword is null){CompressionMessage=UiText.Get("T_d2011de64a34");return;}
            compressionAttemptGroup=Guid.NewGuid();compressionActivityStarted=true;Changed(nameof(HasCompressionActivity));Page=0;createdCompressionPath="";CompressionRunning=true;CompressionPercent=0;
            var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),engines.DllPath(activeEngine));
            for(var index=0;index<plan.Count;index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var item=plan[index];var position=index;var row=AddCompressionTask(item.OutputPath,[item.SourcePath]);current=row;
                var progress=new Progress<TaskUpdate>(value=>
                {
                    row.Status=value.Message;row.IsIndeterminate=value.Progress is not {Total:>0};
                    row.Percent=value.Progress is {Total:>0} state?Math.Min(100,state.Completed*100.0/state.Total):0;
                    CompressionIndeterminate=row.IsIndeterminate;CompressionPercent=(position+row.Percent/100)*100/plan.Count;
                    CompressionMessage=UiText.Format("Compression.SeparateProgress",position+1,plan.Count,Path.GetFileName(item.OutputPath),value.Message);
                });
                var outcome=await CreateCompressionItemAsync([item.SourcePath],item.OutputPath,module,client,false,volumeSize,progress,cancellation.Token);
                row.Completed=outcome.Engine.IsSuccess;row.Failure=row.Completed?null:outcome.Engine;
                row.Running=false;row.IsIndeterminate=false;
                row.Status=row.Completed?UiText.Get("T_1bb010c7351a"):outcome.Engine.Status=="cancelled"?UiText.Get("T_8e8fba138ab2"):CompressionError(outcome.Engine.Code);
                if(row.Completed)
                {
                    completed++;createdCompressionPath=outcome.OutputPath;row.SetArchiveResult(outcome.OutputPath);
                    row.ResultOutputPath=Path.GetDirectoryName(Path.GetFullPath(outcome.OutputPath));await RecordCompressionPasswordAsync(outcome.OutputPath);
                }
                else if(outcome.Engine.Status=="cancelled")throw new OperationCanceledException(cancellation.Token);
                else failed++;
                current=null;
            }
            CompressionPercent=100;CompressionMessage=UiText.Format("Compression.SeparateCompleted",completed,plan.Count,failed);
        }
        catch(OperationCanceledException)
        {
            if(current is not null){current.Failure=new EngineResult{Status="cancelled",Code="cancelled"};current.Running=false;current.IsIndeterminate=false;current.Status=UiText.Get("T_8e8fba138ab2");}
            CompressionMessage=UiText.Format("Compression.SeparateCancelled",completed,plan.Count,failed);
        }
        finally
        {
            if(current is not null){current.Running=false;current.IsIndeterminate=false;}
            compressionPassword=null;Changed(nameof(CompressionPasswordStatus));Changed(nameof(CompressionPasswordSummary));
            CompressionIndeterminate=false;Footer=CompressionMessage;cancellation.Dispose();cancellation=null;CompressionRunning=false;IsBusy=false;idle.TrySetResult();
        }
    }
}
