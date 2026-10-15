using System.Text.Json;
using System.Reflection;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Cli;
internal static class CliRunner
{
    private const string Help="""
nomozip-cli — console entry / 命令行入口
  extract ARCHIVE --output DIRECTORY [--on-conflict fail|skip|replace]
  test ARCHIVE
  create SOURCE... --output ARCHIVE [--format 7z|zip|tar|gzip|xz] [--level 0|1|3|5|7|9]
  vault-list [--data-dir DIRECTORY]
Credentials: --password-stdin OR --password-id GUID; --use-vault (extract/test only)
  --encrypt (create); --no-encrypt-headers (encrypted 7z only)
  --master-password-stdin unlocks an APV2 vault. If both stdin flags are used:
  archive password on the first UTF-8 line, master password on the second.
  --interactive enables masked terminal password prompts; Escape cancels.
Common: --data-dir DIRECTORY --engine NN.NN --progress --cancel-file PATH
  --replace (create only); -- terminates options for names beginning with '-'.
stdout: one schema-1 JSON result; --progress emits JSON lines on stderr.
Exit: 0 success, 1 completed with warning, 2 operation failed, 7 arguments,
      8 out of memory, 255 cancellation. Default: no prompts, no overwrite.
Passwords are never accepted as command-line values. CLI vault access is read-only.
See CLI-guide.md / 使用说明.txt / README.en.txt for batch examples.
""";
    internal static async Task<int> RunAsync(string[] args,CancellationToken parentToken)
    {
        string command="",engineVersion="";CliOptions? options=null;DpapiPasswordVault? vault=null;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        using var stopWatcher=new CancellationTokenSource();Task? watcher=null;
        int Report(EngineResult result,string? output=null,string? warning=null,object? records=null)
        {
            var exit=result.Status=="cancelled"?255:!result.IsSuccess?2:string.IsNullOrEmpty(warning)?0:1;
            Console.WriteLine(JsonSerializer.Serialize(new {schema=1,command,status=exit==1?"warning":result.Status,code=result.IsSuccess?exit==1?"completed_with_warnings":"ok":result.Code,exitCode=exit,engineVersion,architecture=EngineArchitecture.Current,output,warning,records}));
            return exit;
        }
        int Error(string code,int exit=2)
        {Console.WriteLine(JsonSerializer.Serialize(new {schema=1,command,status=exit==255?"cancelled":"error",code,exitCode=exit,engineVersion,architecture=EngineArchitecture.Current}));return exit;}
        try
        {
            options=CliOptions.Parse(args);command=options.Command;
            if(command=="help") {Console.WriteLine(Help);return 0;}
            if(command=="version") {Console.WriteLine(JsonSerializer.Serialize(new{schema=1,version=typeof(CliRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0],architecture=EngineArchitecture.Current}));return 0;}
            if(options.Interactive && Console.IsInputRedirected || (options.PasswordStdin || options.MasterStdin) && !Console.IsInputRedirected) return Error("invalid_input_mode",7);
            var token=cancellation.Token;
            if(options.CancelFile is not null)
            {
                if(File.Exists(options.CancelFile)) cancellation.Cancel();
                watcher=Task.Run(async()=>{while(!stopWatcher.IsCancellationRequested) {if(File.Exists(options.CancelFile)) {cancellation.Cancel();return;}await Task.Delay(50,stopWatcher.Token);}},stopWatcher.Token);
            }
            token.ThrowIfCancellationRequested();
            string? secret=options.PasswordStdin?await Console.In.ReadLineAsync(token):null;
            if(options.PasswordStdin && string.IsNullOrEmpty(secret)) return Error("password_input_missing",7);
            string? master=options.MasterStdin?await Console.In.ReadLineAsync(token):null;
            if(options.MasterStdin && string.IsNullOrEmpty(master)) return Error("master_password_input_missing",7);
            IPasswordVault readonlyVault=new EmptyVault();
            if(options.UseVault || options.PasswordId is not null || command=="vault-list")
            {
                vault=new DpapiPasswordVault(Path.Combine(options.DataDirectory,"passwords.apv"));
                if(await vault.GetProtectionStateAsync(token)==VaultProtectionState.Locked)
                {
                    if(master is null && options.Interactive) master=await TerminalSecret.ReadAsync("Master password",token);
                    if(master is null) return Error(options.Interactive?"cancelled":"vault_locked",options.Interactive?255:2);
                    await vault.UnlockAsync(master,token);
                }
                readonlyVault=new ReadOnlyVault(vault);
                if(options.PasswordId is { } id)
                {
                    var selected=(await vault.ReadAsync(token)).FirstOrDefault(value=>value.Id==id);
                    if(selected is null) return Error("password_record_not_found");secret=selected.Secret;
                }
                if(command=="vault-list")
                    return Report(new EngineResult {Status="success"},records:(await vault.ReadAsync(token)).Select(value=>new{id=value.Id,name=value.Name,group=value.Group,autoTry=value.AutoTry}).ToArray());
            }
            var directory=AppContext.BaseDirectory;var publicKeyPath=Path.Combine(directory,"update-public-key.pem");
            var publicKey=File.Exists(publicKeyPath)?await File.ReadAllTextAsync(publicKeyPath,token):null;
            var installed=Path.Combine(options.DataDirectory,EngineArchitecture.InstalledDirectoryName);
            var catalog=new EngineCatalog(Path.Combine(directory,"engines"),installed,publicKey);
            var versions=await catalog.ReadVerifiedVersionsAsync();if(versions.Count==0) return Error("engine_not_available");
            if(options.Engine is not null) engineVersion=options.Engine;
            else
            {
                var selection=await new EngineUpdateService(installed,Path.Combine(options.DataDirectory,EngineArchitecture.SelectionFileName),publicKey??"",Path.Combine(directory,"ArchiveEngineWorker.exe"),"",catalog).ReadSelectionAsync(token);
                engineVersion=selection is not null && versions.Contains(selection.Active)?selection.Active:versions[0];
            }
            if(!versions.Contains(engineVersion)) return Error("engine_not_available");
            var client=new ArchiveEngineClient(Path.Combine(directory,"ArchiveEngineWorker.exe"),catalog.DllPath(engineVersion));
            IPasswordCandidateProvider candidates=options.UseVault?new PasswordCandidateProvider(readonlyVault):new EmptyCandidates();
            var supplied=false;EngineResult? unmetPassword=null;bool userCancelled=false,conflict=false;
            async Task<PasswordPromptResponse?> Ask(EngineResult failure)
            {
                token.ThrowIfCancellationRequested();string? password=null;
                if(!supplied && secret is not null) {supplied=true;password=secret;}
                else if(options.Interactive) {password=await TerminalSecret.ReadAsync("Archive password",token);userCancelled=password is null;}
                if(password is null) {unmetPassword=failure;return null;}
                return new(password,false,"");
            }
            var progress=options.Progress?new ForwardProgress<TaskUpdate>(update=>Console.Error.WriteLine(JsonSerializer.Serialize(new{schema=1,kind="progress",state=update.State,completed=update.Progress?.Completed,total=update.Progress?.Total}))):null;
            EngineResult Normalize(EngineResult result)=>!userCancelled && !token.IsCancellationRequested && result.Status=="cancelled"
                ? conflict?new EngineResult {Code="output_exists"}:unmetPassword??result:result;
            if(command=="test")
            {
                var result=await new ArchiveVerificationService(client,candidates).VerifyAsync(options.Paths[0],options.UseVault,Ask,progress,token);
                return Report(Normalize(result.Engine),warning:result.Warning);
            }
            if(command=="extract")
            {
                Task<ExistingFilePolicy> Conflict(string _)
                {if(options.Conflict=="fail") conflict=true;return Task.FromResult(options.Conflict switch {"skip"=>ExistingFilePolicy.Skip,"replace"=>ExistingFilePolicy.Replace,_=>ExistingFilePolicy.Ask});}
                var result=await new ExtractionService(client,readonlyVault,candidates,new WindowsExtractionOrigin()).ExtractAsync(options.Paths[0],options.Output!,options.UseVault,Ask,Conflict,progress,token);
                return Report(Normalize(result.Engine),result.OutputPath,result.Warning);
            }
            if(options.Encrypt && secret is null)
            {
                if(!options.Interactive) return Error("password_required");
                secret=await TerminalSecret.ReadAsync("New archive password",token);
                if(secret is null) return Error("cancelled",255);
                var confirm=await TerminalSecret.ReadAsync("Confirm new password",token);
                if(confirm is null) return Error("cancelled",255);
                if(secret.Length==0 || secret!=confirm) return Error("password_confirmation_mismatch",7);
            }
            var created=await new CompressionService(client,client).CreateAsync(options.Paths,options.Output!,options.Format,options.Level,options.Encrypt?secret:null,options.EncryptHeaders,options.Replace,progress,token);
            return Report(created.Engine,created.OutputPath);
        }
        catch(OperationCanceledException) {return Error("cancelled",255);}
        catch(ArgumentException) {return Error("invalid_arguments",7);}
        catch(OutOfMemoryException) {return Error("out_of_memory",8);}
        catch(VaultException error) {return Error(error.Code);}
        catch(UpdateException error) {return Error(error.Code);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {return Error("source_or_output_unavailable");}
        catch(Exception) {return Error("unexpected_error");}
        finally
        {
            stopWatcher.Cancel();if(watcher is not null) try {await watcher;} catch(OperationCanceledException) {}
            if(vault is not null) await vault.LockAsync(CancellationToken.None);
        }
    }
    private sealed class EmptyCandidates:IPasswordCandidateProvider
    {public Task<IReadOnlyList<PasswordCandidate>> GetAsync(string archivePath,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<PasswordCandidate>>([]);}
    private class EmptyVault:IPasswordVault
    {
        public virtual Task<IReadOnlyList<SavedPassword>> ReadAsync(CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<SavedPassword>>([]);
        public virtual Task<Guid?> GetAssociatedPasswordAsync(string path,CancellationToken cancellationToken=default)=>Task.FromResult<Guid?>(null);
        public Task RecordSuccessAsync(Guid id,string path,CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public Task SaveAsync(SavedPassword value,CancellationToken cancellationToken=default)=>throw new InvalidOperationException();
        public Task DeleteAsync(Guid id,CancellationToken cancellationToken=default)=>throw new InvalidOperationException();
        public Task RestoreBackupAsync(CancellationToken cancellationToken=default)=>throw new InvalidOperationException();
        public Task<PasswordImportPlan> ImportAsync(IReadOnlyList<SavedPassword> values,PasswordImportMode mode,CancellationToken cancellationToken=default)=>throw new InvalidOperationException();
    }
    private sealed class ReadOnlyVault(IPasswordVault inner):EmptyVault
    {
        public override Task<IReadOnlyList<SavedPassword>> ReadAsync(CancellationToken cancellationToken=default)=>inner.ReadAsync(cancellationToken);
        public override Task<Guid?> GetAssociatedPasswordAsync(string path,CancellationToken cancellationToken=default)=>inner.GetAssociatedPasswordAsync(path,cancellationToken);
    }
}
