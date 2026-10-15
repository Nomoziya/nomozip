using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts/appearance-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var settings=Path.Combine(folder,"settings.json");await File.WriteAllTextAsync(settings,"{\"Version\":99,\"Original\":\"原文\"}");
await LanguagePreferenceStore.SaveAsync(folder,"en");var language=Path.Combine(folder,LanguagePreferenceStore.FileName);
var vaultPath=Path.Combine(folder,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);var record=new SavedPassword(Guid.NewGuid(),"旧密码","中文 secret","原始备注",true,DateTimeOffset.UtcNow);await vault.SaveAsync(record);
var before=new[]{settings,language,vaultPath}.ToDictionary(p=>p,Hash);var preference=Path.Combine(folder,AppearancePreferenceStore.FileName);
Check("missing preference follows system without creating file",AppearancePreferenceStore.Read(folder)==new AppearancePreference("system",true) && !File.Exists(preference));
foreach(var mode in new[]{"system","light","dark"})
{
    await AppearancePreferenceStore.SaveAsync(folder,mode);
    Check(mode+" stored mode survives reopening",AppearancePreferenceStore.Read(folder)==new AppearancePreference(mode,true));
    Check(mode+" original settings language and vault bytes preserved",before.All(pair=>Hash(pair.Key)==pair.Value));
}
var reopened=(await new DpapiPasswordVault(vaultPath).ReadAsync()).Single();Check("original secret and metadata still usable",reopened.Secret==record.Secret && reopened.Name==record.Name && reopened.Notes==record.Notes);
foreach(var invalid in new[]{"not json","null","{\"Version\":99,\"Mode\":\"dark\"}","{\"Version\":1,\"Mode\":\"unknown\"}"})
{
    await File.WriteAllTextAsync(preference,invalid);var hash=Hash(preference);Check("unknown preference falls back and is read only "+invalid,AppearancePreferenceStore.Read(folder)==new AppearancePreference("system",false));
    var refused=false;try{await AppearancePreferenceStore.SaveAsync(folder,"light");}catch(InvalidDataException){refused=true;}
    Check("unknown preference preserved "+invalid,refused && Hash(preference)==hash);
}
var locked=Path.Combine(folder,"locked");await AppearancePreferenceStore.SaveAsync(locked,"dark");var lockedPath=Path.Combine(locked,AppearancePreferenceStore.FileName);var lockedHash=Hash(lockedPath);
using(var stream=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
{
    var refused=false;try{await AppearancePreferenceStore.SaveAsync(locked,"light");}catch(Exception error) when(error is IOException or UnauthorizedAccessException){refused=true;}
    Check("real sharing lock preserves existing mode",refused && Hash(lockedPath)==lockedHash);
}
Check("atomic failure removes temporary file",!Directory.EnumerateFiles(locked,"*.tmp").Any());
var bad=false;try{await AppearancePreferenceStore.SaveAsync(Path.Combine(folder,"bad"),"../other");}catch(ArgumentException){bad=true;}
Check("unsupported requested mode refused before writes",bad && !Directory.Exists(Path.Combine(folder,"bad")));
var result=new{passed=checks.Values.All(v=>v),total=checks.Count,checks,syntheticData=true,productionSettingsChanged=false};
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/appearance-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));return result.passed?0:1;
