using System.IO.Compression;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var data=Path.GetFullPath(Path.Combine(root,"artifacts/extraction-tools",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
var fixtures=Path.Combine(root,"tests/fixtures/generated");var checks=new Dictionary<string,bool>();
void Check(string name,bool passed) {checks[name]=passed;Console.WriteLine((passed?"PASS ":"FAIL ")+name);if(!passed) throw new Exception(name);}
var vaultPath=Path.Combine(data,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
foreach(var secret in new[] {"never matches","password","password2","测试密碼 ' $ & 空格"}) await vault.SaveAsync(new(Guid.NewGuid(),secret,secret,"",true,DateTimeOffset.UtcNow));
var beforeVault=await File.ReadAllBytesAsync(vaultPath);
var archives=new Dictionary<string,IReadOnlyDictionary<string,string>> { ["单文件"]=new Dictionary<string,string>{{"readme.txt","one"}},["单根目录"]=new Dictionary<string,string>{{"photos/a.txt","a"},{"photos/sub/b.txt","b"}},["散放文件"]=new Dictionary<string,string>{{"a.txt","a"},{"b.txt","b"}},["混合根"]=new Dictionary<string,string>{{"photos/a.txt","a"},{"readme.txt","one"}} };
foreach(var archive in archives)
{
    using var zip=ZipFile.Open(Path.Combine(data,archive.Key+".zip"),ZipArchiveMode.Create);
    foreach(var entry in archive.Value) {using var writer=new StreamWriter(zip.CreateEntry(entry.Key).Open());writer.Write(entry.Value);}
}
foreach(var version in new[] {"26.04","26.03"})
{
    var engine=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,$"engines/{version}/7z.dll"));
    var verify=new ArchiveVerificationService(engine,new PasswordCandidateProvider(vault));
    foreach(var fixture in new[] {"plain.zip","encrypted-header.7z","mixed-traditional.zip","mixed-aes.zip","test_read_format_rar5_encrypted.rar"})
    {
        var beforeFiles=Directory.GetFiles(data,"*",SearchOption.AllDirectories).Order().ToArray();var prompts=0;
        var outcome=await verify.VerifyAsync(Path.Combine(fixtures,fixture),true,_=>{prompts++;return Task.FromResult<PasswordPromptResponse?>(null);});
        Check(version+" test reads real "+fixture,outcome.Engine.IsSuccess && prompts==0);
        Check(version+" test has no file output "+fixture,beforeFiles.SequenceEqual(Directory.GetFiles(data,"*",SearchOption.AllDirectories).Order()));
        Check(version+" test preserves full vault "+fixture,beforeVault.SequenceEqual(File.ReadAllBytes(vaultPath)));
    }
    var manualCount=0;
    var manual=await verify.VerifyAsync(Path.Combine(fixtures,"mixed-aes.zip"),false,_=>Task.FromResult<PasswordPromptResponse?>(new(manualCount++==0?"password":"password2",true,"never save on test")));
    Check(version+" manual mixed test combines passwords without save",manual.Engine.IsSuccess && manualCount==2 && beforeVault.SequenceEqual(File.ReadAllBytes(vaultPath)));
    var wrongCount=0;var wrongObserved=false;
    var wrong=await verify.VerifyAsync(Path.Combine(fixtures,"encrypted-header.7z"),false,failure=>{wrongObserved|=failure.Code is "wrong_password" or "password_or_corrupt";return Task.FromResult<PasswordPromptResponse?>(wrongCount++==0?new("wrong",true,"never save"):null);});
    Check(version+" wrong password is distinct before user cancellation",wrong.Engine.Status=="cancelled" && wrongObserved && wrongCount==2);
    var damaged=await verify.VerifyAsync(Path.Combine(fixtures,"corrupt.7z"),false,_=>Task.FromResult<PasswordPromptResponse?>(null));
    Check(version+" corrupt file is not success or password prompt",!damaged.Engine.IsSuccess && !damaged.Engine.NeedsPassword);
    var missingDir=Path.Combine(data,"missing-"+version);Directory.CreateDirectory(missingDir);File.Copy(Path.Combine(fixtures,"split.7z.001"),Path.Combine(missingDir,"split.7z.001"));
    var missing=await verify.VerifyAsync(Path.Combine(missingDir,"split.7z.001"),false,_=>Task.FromResult<PasswordPromptResponse?>(null));
    Check(version+" missing volumes distinct",missing.Engine.Code=="missing_volume");
    using var stop=new CancellationTokenSource();var stopped=false;
    var cancelled=await verify.VerifyAsync(Path.Combine(fixtures,"large.7z"),false,_=>Task.FromResult<PasswordPromptResponse?>(null),new ForwardProgress<TaskUpdate>(value=>{if(value.Progress is not null && !stopped) {stopped=true;stop.Cancel();}}),stop.Token);
    Check(version+" actual test cancellation",stopped && cancelled.Engine.Status=="cancelled");
    foreach(var archive in archives)
    foreach(var layout in new[] {ExtractionLayout.Direct,ExtractionLayout.ArchiveFolder,ExtractionLayout.Smart})
    {
        var archivePath=Path.Combine(data,archive.Key+".zip");var output=Path.Combine(data,$"{version}-{archive.Key}-{layout}");
        var rules=new ExtractionDirectoryRules(Layout:layout);var service=new ExtractionService(engine,vault,new PasswordCandidateProvider(vault));
        var outcome=await service.ExtractAsync(archivePath,output,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),resolveDestination:items=>ExtractionDirectoryPlanner.Resolve(archivePath,output,rules,items,new DateOnly(2026,10,13)));
        var container=layout==ExtractionLayout.ArchiveFolder || layout==ExtractionLayout.Smart && archive.Key is "散放文件" or "混合根";
        var expected=container?Path.Combine(output,archive.Key):output;
        Check(version+" real directory "+archive.Key+" "+layout,outcome.Engine.IsSuccess && outcome.OutputPath==expected && archive.Value.All(entry=>File.ReadAllText(Path.Combine(expected,entry.Key))==entry.Value));
        Check(version+" no redundant directory "+archive.Key+" "+layout,Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length==archive.Value.Count && !Directory.EnumerateDirectories(output,".archive-password-*",SearchOption.AllDirectories).Any());
    }
    var folderRoot=Path.Combine(data,"collision-"+version);Directory.CreateDirectory(folderRoot);Directory.CreateDirectory(Path.Combine(folderRoot,"散放文件"));
    var folderService=new ExtractionService(engine,vault,new PasswordCandidateProvider(vault));var named=new ExtractionDirectoryRules(Layout:ExtractionLayout.ArchiveFolder);
    async Task<ExtractionOutcome> ExtractCollision(ExtractionDirectoryRules rules,ExistingFilePolicy policy) => await folderService.ExtractAsync(Path.Combine(data,"散放文件.zip"),folderRoot,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(policy),resolveDestination:items=>ExtractionDirectoryPlanner.Resolve(Path.Combine(data,"散放文件.zip"),folderRoot,rules,items,new(2026,10,13)));
    var numbered=await ExtractCollision(named,ExistingFilePolicy.Replace);Check(version+" numbered container avoids existing",numbered.Engine.IsSuccess && numbered.OutputPath==Path.Combine(folderRoot,"散放文件 (2)"));
    var merge=named with {Collision=ContainerCollision.Merge};File.WriteAllText(Path.Combine(folderRoot,"散放文件/a.txt"),"existing");
    var skipped=await ExtractCollision(merge,ExistingFilePolicy.Skip);Check(version+" merging respects skip",skipped.Engine.IsSuccess && File.ReadAllText(Path.Combine(folderRoot,"散放文件/a.txt"))=="existing");
    var cancel=await ExtractCollision(merge,ExistingFilePolicy.Ask);Check(version+" conflict cancellation preserves existing",cancel.Engine.Status=="cancelled" && File.ReadAllText(Path.Combine(folderRoot,"散放文件/a.txt"))=="existing");
    var replaced=await ExtractCollision(merge,ExistingFilePolicy.Replace);Check(version+" merging respects replace",replaced.Engine.IsSuccess && File.ReadAllText(Path.Combine(folderRoot,"散放文件/a.txt"))=="a");
    var occupied=Path.Combine(data,"occupied-"+version);Directory.CreateDirectory(occupied);File.WriteAllText(Path.Combine(occupied,"散放文件"),"keep");
    var blocked=await folderService.ExtractAsync(Path.Combine(data,"散放文件.zip"),occupied,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),resolveDestination:items=>ExtractionDirectoryPlanner.Resolve(Path.Combine(data,"散放文件.zip"),occupied,merge,items,new(2026,10,13)));
    Check(version+" occupied container returns output error and preserves file",blocked.Engine.Code=="output_write_failed" && File.ReadAllText(Path.Combine(occupied,"散放文件"))=="keep" && Directory.GetFiles(occupied).Length==1);
}
var plannerRoot=Path.Combine(data,"planning");Directory.CreateDirectory(plannerRoot);
Check("empty archive does not add redundant smart directory",ExtractionDirectoryPlanner.Resolve("empty.zip",plannerRoot,new(Layout:ExtractionLayout.Smart),[],new(2026,10,13))==plannerRoot);
Check("root entries and backslash paths recognized",ExtractionDirectoryPlanner.HasSingleRoot([new(0,"photos",0,true,false),new(1,"photos\\a.txt",1,false,false)]));
Check("file and folder with same root not single folder",!ExtractionDirectoryPlanner.HasSingleRoot([new(0,"photos",1,false,false),new(1,"photos/a.txt",1,false,false)]));
Check("date naming preset",ExtractionDirectoryPlanner.Resolve("assets.zip",plannerRoot,new(Layout:ExtractionLayout.ArchiveFolder,Naming:ContainerNaming.ArchiveNameDate),[],new(2026,10,13))==Path.Combine(plannerRoot,"assets_2026-10-13"));
Check("split archive naming preset",ExtractionDirectoryPlanner.Resolve("assets.7z.001",plannerRoot,new(Layout:ExtractionLayout.ArchiveFolder),[],new(2026,10,13))==Path.Combine(plannerRoot,"assets"));
Check("each archive location",ExtractionDirectoryPlanner.BaseDirectory(Path.Combine(data,"assets.zip"),plannerRoot,ExtractionLocation.ArchiveParent)==data);
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/extraction-tools-validation.json"),JsonSerializer.Serialize(new {passed=checks.Values.All(value=>value),total=checks.Count,checks,syntheticData=true}));
return checks.Values.All(value=>value)?0:1;
