using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var app=Path.GetFullPath(args.Length>1?args[1]:Path.Combine(root,"artifacts/app"));var cli=Path.Combine(app,"nomozip-cli.exe");
var own=Path.GetFullPath(Path.Combine(root,"artifacts/cl-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(own);
var checks=new Dictionary<string,bool>();void Check(string name,bool value) {checks.Add(name,value);Console.WriteLine((value?"PASS ":"FAIL ")+name);}
async Task<(int Exit,JsonElement Json,string Out,string Err)> Run(string[] arguments,string? input=null,Action<string>? stderr=null)
{
    if(arguments.Length>0 && arguments[0] is not ("--version" or "--help" or "-h" or "help" or "version"))
    {
        if(!arguments.Contains("--data-dir"))
        {
            var split=Array.IndexOf(arguments,"--");if(split<0) split=arguments.Length;
            arguments=[..arguments.Take(split),"--data-dir",Path.Combine(own,"default-profile"),..arguments.Skip(split)];
        }
        var index=Array.IndexOf(arguments,"--data-dir");
        if(index<0 || index+1>=arguments.Length || !Path.GetFullPath(arguments[index+1]).StartsWith(own+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("CLI test profile must be private.");
    }
    var start=new ProcessStartInfo(cli){WorkingDirectory=own,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    foreach(var argument in arguments) start.ArgumentList.Add(argument);
    using var process=Process.Start(start)!;
    var stdout=process.StandardOutput.ReadToEndAsync();var error=new StringBuilder();
    var drain=Task.Run(async()=>{while(await process.StandardError.ReadLineAsync() is { } line) {error.AppendLine(line);stderr?.Invoke(line);}});
    if(input is not null) await process.StandardInput.WriteAsync(input);process.StandardInput.Close();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));await drain;var output=await stdout;
    if(arguments[0] is "help" or "--help" or "-h") return(process.ExitCode,default,output,error.ToString());
    using var json=JsonDocument.Parse(output);return(process.ExitCode,json.RootElement.Clone(),output,error.ToString());
}
try
{
    var profile=Path.Combine(own,"profile");Directory.CreateDirectory(profile);
    var vault=new DpapiPasswordVault(Path.Combine(profile,"passwords.apv"));var id=Guid.NewGuid();const string secret="CLI密碼 $ ' unique-secret";
    await vault.SaveAsync(new(id,"fixture name",secret,"private note",true,DateTimeOffset.UtcNow));
    await vault.SaveAsync(new(Guid.NewGuid(),"disabled","disabled-secret","",false,DateTimeOffset.UtcNow));
    byte[] before=File.ReadAllBytes(Path.Combine(profile,"passwords.apv")),backup=File.ReadAllBytes(Path.Combine(profile,"passwords.apv.bak"));
    var content=Path.Combine(own,"中文 源文件.txt");await File.WriteAllTextAsync(content,"CLI exact bytes 中文");
    var folder=Path.Combine(own,"folder");Directory.CreateDirectory(Path.Combine(folder,"empty"));await File.WriteAllBytesAsync(Path.Combine(folder,"zero.txt"),[]);
    foreach(var version in new[]{"26.04","26.03"})
    {
        string[] Common()=>["--data-dir",profile,"--engine",version];
        var archive=Path.Combine(own,version+" encrypted.7z");
        var created=await Run(["create",content,folder,"--output",archive,"--encrypt","--password-stdin",..Common()],secret+"\n");
        Check(version+" actual create stdin password, exit and native architecture",created.Exit==0 && created.Json.GetProperty("code").GetString()=="ok" && created.Json.GetProperty("schema").GetInt32()==1 && created.Json.GetProperty("architecture").GetString()==EngineArchitecture.Current && created.Json.GetProperty("engineVersion").GetString()==version);
        var client=new ArchiveEngineClient(Path.Combine(app,"ArchiveEngineWorker.exe"),Path.Combine(app,"engines",version,"7z.dll"));
        Check(version+" created filenames encrypted and exact password",(await client.ListAsync(archive)).NeedsPassword && (await client.TestAsync(archive,secret)).IsSuccess);
        var tested=await Run(["test",archive,"--use-vault","--progress",..Common()]);
        Check(version+" saved password automatic test and progress schema",tested.Exit==0 && tested.Err.Split('\n',StringSplitOptions.RemoveEmptyEntries).All(line=>JsonDocument.Parse(line).RootElement.GetProperty("kind").GetString()=="progress"));
        var output=Path.Combine(own,version+" output");var extracted=await Run(["extract",archive,"--output",output,"--password-id",id.ToString(),..Common()]);
        Check(version+" actual extract library ID exact contents and empty directory",extracted.Exit==0 && File.ReadAllBytes(Path.Combine(output,Path.GetFileName(content))).SequenceEqual(File.ReadAllBytes(content)) && Directory.Exists(Path.Combine(output,"folder/empty")) && new FileInfo(Path.Combine(output,"folder/zero.txt")).Length==0);
        Check(version+" all results and progress omit archive secrets",new[]{created.Out,created.Err,tested.Out,tested.Err,extracted.Out,extracted.Err}.All(text=>!text.Contains(secret,StringComparison.Ordinal)));
        var collision=await Run(["extract",archive,"--output",output,"--password-stdin",..Common()],secret+"\n");
        Check(version+" default conflict fails without overwriting",collision.Exit==2 && collision.Json.GetProperty("code").GetString()=="output_exists");
        await File.WriteAllTextAsync(Path.Combine(output,Path.GetFileName(content)),"preserved target");
        var skipped=await Run(["extract",archive,"--output",output,"--use-vault","--on-conflict","skip",..Common()]);
        Check(version+" skip returns warning exit 1 and preserves target",skipped.Exit==1 && File.ReadAllText(Path.Combine(output,Path.GetFileName(content)))=="preserved target");
        var replaced=await Run(["extract",archive,"--output",output,"--use-vault","--on-conflict","replace",..Common()]);
        Check(version+" explicit extract replace restores exact bytes",replaced.Exit==0 && File.ReadAllBytes(Path.Combine(output,Path.GetFileName(content))).SequenceEqual(File.ReadAllBytes(content)));
        var original=File.ReadAllBytes(archive);var refused=await Run(["create",content,"--output",archive,..Common()]);
        Check(version+" create existing target never silently replaces",refused.Exit==2 && refused.Json.GetProperty("code").GetString()=="output_exists" && original.SequenceEqual(File.ReadAllBytes(archive)));
        var replace=await Run(["create",content,"--output",archive,"--replace",..Common()]);
        Check(version+" explicit create replacement yields clear archive",replace.Exit==0 && (await client.TestAsync(archive)).IsSuccess);
        var secured=Path.Combine(own,version+"-id.7z");var byId=await Run(["create",content,"--output",secured,"--encrypt","--password-id",id.ToString(),..Common()]);
        Check(version+" creating by library ID uses exact selected secret",byId.Exit==0 && (await client.TestAsync(secured,secret)).IsSuccess);
        var clearHeaders=Path.Combine(own,version+"-clear-headers.7z");var headerResult=await Run(["create",content,"--output",clearHeaders,"--encrypt","--no-encrypt-headers","--password-stdin",..Common()],secret+"\n");
        Check(version+" explicit clear filenames retain content encryption",headerResult.Exit==0 && (await client.ListAsync(clearHeaders)).IsSuccess && (await client.TestAsync(clearHeaders)).NeedsPassword && (await client.TestAsync(clearHeaders,secret)).IsSuccess);
        var wrong=await Run(["test",secured,"--password-stdin",..Common()],"wrong-secret\n");
        Check(version+" wrong password fails rather than cancellation",wrong.Exit==2 && wrong.Json.GetProperty("status").GetString()=="error" && !wrong.Out.Contains("wrong-secret"));
        var absent=await Run(["test",secured,..Common()]);Check(version+" unattended encrypted input never prompts",absent.Exit==2 && absent.Json.GetProperty("code").GetString()=="password_required" && absent.Err.Length==0);
        foreach(var format in new[]{"7z","zip","tar","gzip","xz"})
        {
            var target=Path.Combine(own,version+"-"+format+".archive");var result=await Run(["create",content,"--output",target,"--format",format,..Common()]);
            var listed=await client.ListAsync(target);Check(version+" "+format+" delegates real format creation",result.Exit==0 && listed.IsSuccess && listed.Format==format && (await client.TestAsync(target)).IsSuccess);
        }
        var mixed=Path.Combine(root,"tests/fixtures/generated/mixed-aes.zip");var mixedProfile=Path.Combine(own,version+"-mixed-profile");
        var mixedVault=new DpapiPasswordVault(Path.Combine(mixedProfile,"passwords.apv"));await mixedVault.SaveAsync(new(Guid.NewGuid(),"first","password","",true,DateTimeOffset.UtcNow));await mixedVault.SaveAsync(new(Guid.NewGuid(),"second","password2","",true,DateTimeOffset.UtcNow));var mixedBefore=File.ReadAllBytes(Path.Combine(mixedProfile,"passwords.apv"));
        var mixedResult=await Run(["extract",mixed,"--output",Path.Combine(own,version+"-mixed-output"),"--data-dir",mixedProfile,"--engine",version,"--use-vault"]);
        Check(version+" multiple source passwords extract without modifying library",mixedResult.Exit==0 && mixedBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(mixedProfile,"passwords.apv"))));
        var signal=Path.Combine(own,version+"-cancel.signal");
        var cancelled=await Run(["extract",secured,"--output",Path.Combine(own,version+"-cancel-output"),"--password-stdin","--progress","--cancel-file",signal,..Common()],secret+"\n",line=>{if(line.Contains("testing")) File.WriteAllText(signal,"");});
        Check(version+" actual in-flight cooperative cancellation returns 255",cancelled.Exit==255 && cancelled.Json.GetProperty("code").GetString()=="cancelled" && !Directory.GetFiles(own,".archive-password-*",SearchOption.AllDirectories).Any());
        Check(version+" cancelled extraction removes staging directories",!Directory.GetDirectories(Path.Combine(own,version+"-cancel-output"),".archive-password-*",SearchOption.TopDirectoryOnly).Any());
        var corrupt=Path.Combine(own,version+"-corrupt.7z");await File.WriteAllTextAsync(corrupt,"corrupt archive");
        var corrupted=await Run(["test",corrupt,..Common()]);Check(version+" corrupt archive returns operation error",corrupted.Exit==2);
    }
    Check("APV1 vault and recovery copy byte-identical after all commands",before.SequenceEqual(File.ReadAllBytes(Path.Combine(profile,"passwords.apv"))) && backup.SequenceEqual(File.ReadAllBytes(Path.Combine(profile,"passwords.apv.bak"))));
    var records=await Run(["vault-list","--data-dir",profile]);var fields=records.Json.GetProperty("records")[0].EnumerateObject().Select(value=>value.Name).ToHashSet();
    Check("vault-list exposes usable IDs and metadata only",records.Exit==0 && fields.SetEquals(["id","name","group","autoTry"]) && !records.Out.Contains(secret) && !records.Out.Contains("private note"));
    foreach(var bad in new string[][] { ["test",content,"--password","never-print-this-secret"],["test",content,"--unknown"],["create",content],["extract",content],["test",content,"--level","5"],["create",content,"--output",Path.Combine(own,"bad.rar"),"--format","rar"],["test",content,"--password-id","bad-id"],["test",content,"--progress","--progress"] })
    {
        var result=await Run(bad);Check("invalid syntax "+checks.Count,result.Exit==7 && !result.Out.Contains("never-print-this-secret") && !result.Err.Contains("never-print-this-secret"));
    }
    var eof=await Run(["test",content,"--password-stdin"]);Check("EOF stdin is an input error and does not hang",eof.Exit==7 && eof.Json.GetProperty("code").GetString()=="password_input_missing");
    var missingId=await Run(["test",content,"--password-id",Guid.NewGuid().ToString(),"--data-dir",profile]);Check("unknown library ID fails explicitly",missingId.Exit==2 && missingId.Json.GetProperty("code").GetString()=="password_record_not_found");
    const string master="Master CLI 密碼 unique";await vault.EnableMasterPasswordAsync(master);await vault.LockAsync();before=File.ReadAllBytes(Path.Combine(profile,"passwords.apv"));backup=File.ReadAllBytes(Path.Combine(profile,"passwords.apv.bak"));
    var locked=await Run(["vault-list","--data-dir",profile]);Check("APV2 locked process requires master password",locked.Exit==2 && locked.Json.GetProperty("code").GetString()=="vault_locked");
    var unlocked=await Run(["vault-list","--data-dir",profile,"--master-password-stdin"],master+"\n");Check("APV2 stdin unlock reads records without plaintext password output",unlocked.Exit==0 && unlocked.Json.GetProperty("records").GetArrayLength()==2 && !unlocked.Out.Contains(master) && !unlocked.Out.Contains(secret));
    var apv2=await Run(["test",Path.Combine(own,"26.04-id.7z"),"--use-vault","--data-dir",profile,"--master-password-stdin"],master+"\n");Check("APV2 unlocked saved password actually tests archive",apv2.Exit==0);
    var both=await Run(["create",content,"--output",Path.Combine(own,"two-input-lines.7z"),"--encrypt","--password-stdin","--use-vault","--data-dir",profile,"--master-password-stdin"],secret+"\n"+master+"\n");
    Check("unsupported create auto-try is rejected without consuming credentials",both.Exit==7 && !both.Out.Contains(secret) && !both.Out.Contains(master));
    var apv2Id=await Run(["create",content,"--output",Path.Combine(own,"apv2-id.7z"),"--encrypt","--password-id",id.ToString(),"--data-dir",profile,"--master-password-stdin"],master+"\n");Check("APV2 ID creates actual encrypted archive",apv2Id.Exit==0);
    var wrongMaster=await Run(["vault-list","--data-dir",profile,"--master-password-stdin"],"wrong-master\n");Check("wrong master fails and is never printed",wrongMaster.Exit==2 && !wrongMaster.Out.Contains("wrong-master"));
    var lockedAgain=await Run(["vault-list","--data-dir",profile]);Check("unlock never persists across CLI processes",lockedAgain.Exit==2);
    Check("APV2 and recovery bytes preserved across unlocks and failures",before.SequenceEqual(File.ReadAllBytes(Path.Combine(profile,"passwords.apv"))) && backup.SequenceEqual(File.ReadAllBytes(Path.Combine(profile,"passwords.apv.bak"))));
    var preCancelled=Path.Combine(own,"pre-cancel.signal");await File.WriteAllTextAsync(preCancelled,"");var pre=await Run(["test",content,"--cancel-file",preCancelled]);Check("pre-existing cancel signal returns 255 before input work",pre.Exit==255);
    var extra=Path.Combine(own,"stdin-order.7z");const string separate="Separate-stdin-secret";
    var extraCreate=await Run(["create",content,"--output",extra,"--encrypt","--password-stdin"],separate+"\n");
    var twoLines=await Run(["test",extra,"--use-vault","--password-stdin","--master-password-stdin","--data-dir",profile],separate+"\n"+master+"\n");
    Check("two stdin secrets have deterministic archive-then-master ordering",extraCreate.Exit==0 && twoLines.Exit==0 && !twoLines.Out.Contains(separate) && !twoLines.Out.Contains(master));
    var emptyProfile=Path.Combine(own,"never-created-profile");var info=await Run(["--version"]);
    var expectedVersion=System.Xml.Linq.XDocument.Load(Path.Combine(root,"src/Desktop/ArchivePassword.Desktop.csproj")).Root!.Element("PropertyGroup")!.Element("Version")!.Value;
    var binaryVersion=FileVersionInfo.GetVersionInfo(Path.Combine(app,"ArchivePassword.exe")).ProductVersion!.Split('+')[0];
    Check("console version reports native process architecture",info.Exit==0 && info.Json.GetProperty("architecture").GetString()==EngineArchitecture.Current && info.Json.GetProperty("version").GetString()==expectedVersion && binaryVersion==expectedVersion);
    var emptyList=await Run(["vault-list","--data-dir",emptyProfile]);Check("reading absent vault does not create profile files",emptyList.Exit==0 && !Directory.Exists(emptyProfile));
    var help=await Run(["--help"]);Check("help is available without any engine or vault access",help.Exit==0 && help.Out.Contains("--password-stdin") && help.Out.Contains("255"));
    await File.WriteAllTextAsync(Path.Combine(own,"-dash.txt"),"dash input");var dashed=await Run(["create","--output",Path.Combine(own,"-dash.7z"),"--","-dash.txt"]);var dashTest=await Run(["test","--","-dash.7z"]);Check("end-of-options supports literal leading-dash relative filenames",dashed.Exit==0 && dashTest.Exit==0);
    var selection=Path.Combine(profile,EngineArchitecture.SelectionFileName);await File.WriteAllTextAsync(selection,"{\"schema\":1,\"active\":\"26.03\",\"previous\":null}");var selectionBefore=File.ReadAllBytes(selection);
    var selected=await Run(["test",Path.Combine(own,"26.04 encrypted.7z"),"--data-dir",profile]);Check("CLI follows architecture-specific UI engine selection read-only",selected.Exit==0 && selected.Json.GetProperty("engineVersion").GetString()=="26.03" && selectionBefore.SequenceEqual(File.ReadAllBytes(selection)));
    var overridden=await Run(["test",Path.Combine(own,"26.04 encrypted.7z"),"--data-dir",profile,"--engine","26.04"]);Check("explicit engine overrides selection without saving it",overridden.Exit==0 && overridden.Json.GetProperty("engineVersion").GetString()=="26.04" && selectionBefore.SequenceEqual(File.ReadAllBytes(selection)));
    await File.WriteAllTextAsync(selection,"invalid");var malformed=await Run(["test",Path.Combine(own,"26.04 encrypted.7z"),"--data-dir",profile]);Check("malformed engine selection fails explicitly without rewriting",malformed.Exit==2 && malformed.Json.GetProperty("code").GetString()=="invalid_selection" && File.ReadAllText(selection)=="invalid");await File.WriteAllBytesAsync(selection,selectionBefore);
    await BatchAndTerminal();
}
catch(Exception error) {Check("exception "+error,false);}
var passed=checks.Count>0 && checks.Values.All(value=>value);
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/cli-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,architecture=EngineArchitecture.Current,osArchitecture=RuntimeInformation.OSArchitecture.ToString(),nativeProcess=RuntimeInformation.ProcessArchitecture==RuntimeInformation.OSArchitecture,actualVisibleConsoleTested=false},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Values.Count(value=>value)}/{checks.Count} passed");return passed?0:1;

async Task BatchAndTerminal()
{
    var script=Path.Combine(own,"batch.cmd");await File.WriteAllTextAsync(script,"@echo off\r\n\"%NOMOZIP_CLI%\" test \"%NOMOZIP_ARCHIVE%\" --data-dir \"%NOMOZIP_PROFILE%\"\r\nexit /b %errorlevel%\r\n",Encoding.ASCII);
    var ps=Path.Combine(own,"batch.ps1");await File.WriteAllTextAsync(ps,"& $env:NOMOZIP_CLI test $env:NOMOZIP_ARCHIVE --data-dir $env:NOMOZIP_PROFILE\nexit $LASTEXITCODE\n");
    foreach(var shell in new[]{"cmd.exe","powershell.exe"}) foreach(var corrupt in new[]{false,true})
    {
        var start=new ProcessStartInfo(shell){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in shell=="cmd.exe"?new[]{"/d","/c",script}:new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",ps}) start.ArgumentList.Add(arg);
        start.Environment["NOMOZIP_CLI"]=cli;start.Environment["NOMOZIP_ARCHIVE"]=Path.Combine(own,corrupt?"26.04-corrupt.7z":"26.04 encrypted.7z");start.Environment["NOMOZIP_PROFILE"]=Path.Combine(own,"batch-profile");
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));var output=await stdout;await stderr;
        using var json=JsonDocument.Parse(output);Check(shell+" actual batch waits and propagates "+(corrupt?"error":"success"),process.ExitCode==(corrupt?2:0) && json.RootElement.GetProperty("exitCode").GetInt32()==process.ExitCode);
    }
    var archive=Path.Combine(own,"26.04-id.7z");
    using(var terminal=new ConptySession(cli,["test",archive,"--interactive","--data-dir",Path.Combine(own,"terminal-profile")]))
    {
        await terminal.WaitForTextAsync("Archive password:");terminal.Send("CLI密碼 $ ' unique-secret\r");var result=await terminal.FinishAsync();
        Check("real ConPTY masked input completes encrypted test without echo",result.Exit==0 && !result.Text.Contains("unique-secret") && result.Text.Contains('*') && result.Text.Contains("\"exitCode\":0"));
    }
    using(var terminal=new ConptySession(cli,["test",archive,"--interactive","--data-dir",Path.Combine(own,"terminal-profile")]))
    {await terminal.WaitForTextAsync("Archive password:");terminal.Send("\x1b");var result=await terminal.FinishAsync();Check("real ConPTY Escape cancels with 255",result.Exit==255 && result.Text.Contains("cancelled"));}
    using(var terminal=new ConptySession(cli,["test",archive,"--interactive","--data-dir",Path.Combine(own,"terminal-profile")]))
    {await terminal.WaitForTextAsync("Archive password:");terminal.Send("\x03");var result=await terminal.FinishAsync();Check("real ConPTY Ctrl+C cancels with 255",result.Exit==255 && result.Text.Contains("cancelled"));}
    var target=Path.Combine(own,"terminal-created.7z");
    using(var terminal=new ConptySession(cli,["create",Path.Combine(own,"中文 源文件.txt"),"--output",target,"--encrypt","--interactive","--data-dir",Path.Combine(own,"terminal-profile")]))
    {
        await terminal.WaitForTextAsync("New archive password:");terminal.Send("terminal-secretX\b\r");await terminal.WaitForTextAsync("Confirm new password:");terminal.Send("terminal-secret\r");var result=await terminal.FinishAsync();
        Check("real ConPTY creation confirms masked password and handles backspace",result.Exit==0 && !result.Text.Contains("terminal-secret") && File.Exists(target));
    }
    var verified=await Run(["test",target,"--password-stdin","--data-dir",Path.Combine(own,"terminal-profile")],"terminal-secret\n");Check("real terminal-created archive independently reads with exact secret",verified.Exit==0);
}
