using System.IO;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class ArchiveConversionViewModel(string archive,ArchiveConversionService service,bool autoTry,
    Func<EngineResult,Task<PasswordPromptResponse?>> askPassword,Func<string,bool> confirmReplace,CancellationToken parentToken=default) : ObservableObject
{
    private string output=Path.Combine(Path.GetDirectoryName(archive)!,Path.GetFileNameWithoutExtension(archive)+"-converted.7z"),format="7z",message="";
    private bool encrypt,working;
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource idle=Completed();
    private static TaskCompletionSource Completed() {var value=new TaskCompletionSource();value.SetResult();return value;}
    public Task WaitForIdleAsync()=>idle.Task;
    public string ArchivePath=>archive;
    public string[] Formats {get;}=["7z","zip"];
    public int[] Levels {get;}=[0,1,3,5,7,9];
    public int Level {get;set;}=5;
    public bool EncryptHeaders {get;set;}=true;
    public string Output {get=>output;set=>Set(ref output,value);}
    public string Format {get=>format;set {if(Set(ref format,value)) {Output=Path.ChangeExtension(Output,value);Changed(nameof(CanEncryptHeaders));}}}
    public bool Encrypt {get=>encrypt;set {if(Set(ref encrypt,value)) Changed(nameof(CanEncryptHeaders));}}
    public bool CanEncryptHeaders=>Encrypt && Format=="7z";
    public bool IsWorking {get=>working;private set {if(Set(ref working,value)) Changed(nameof(CanEdit));}}
    public bool CanEdit=>!IsWorking;
    public string Message {get=>message;private set=>Set(ref message,value);}
    public ArchiveConversionOutcome? Outcome {get;private set;}
    public void Cancel()=>cancellation?.Cancel();
    public async Task ConvertAsync(string password,string confirmation)
    {
        if(IsWorking) return;
        Outcome=null;
        if(string.IsNullOrWhiteSpace(Output)) {Message=UiText.Get("Conversion.OutputRequired");return;}
        if(Encrypt && (password.Length==0 || password!=confirmation)) {Message=UiText.Get("Conversion.PasswordMismatch");return;}
        bool replace=File.Exists(Output);
        if(replace && !confirmReplace(Output)) {Message=UiText.Get("Conversion.Cancelled");return;}
        IsWorking=true;idle=new TaskCompletionSource();cancellation=CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        try
        {
            Outcome=await service.ConvertAsync(archive,Output,Format,Level,Encrypt,password,EncryptHeaders,replace,autoTry,askPassword,
                new Progress<TaskUpdate>(update=>{if(IsWorking) Message=update.Message;}),cancellation.Token);
            Message=Outcome.Engine.IsSuccess?UiText.Get("Conversion.Success"):Outcome.Engine.Status=="cancelled"?UiText.Get("Conversion.Cancelled"):
                Outcome.Engine.Code switch
                {
                    "conversion_same_path"=>UiText.Get("Conversion.SamePath"),
                    "conversion_container_unsupported"=>UiText.Get("Conversion.Unsupported"),
                    "zip_password_ascii_only"=>UiText.Get("Conversion.ZipPassword"),
                    _=>UiText.Get("Conversion.Failed")
                };
            if(!string.IsNullOrEmpty(Outcome.Warning)) Message+="\n"+Outcome.Warning;
        }
        finally {cancellation.Dispose();cancellation=null;IsWorking=false;idle.TrySetResult();}
    }
}
