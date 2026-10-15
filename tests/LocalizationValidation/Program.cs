using System.Collections;
using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);
var folder=Path.Combine(root,"artifacts/localization-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var manager=new ResourceManager("ArchivePassword.Core.Localization.Strings",typeof(UiText).Assembly);
Dictionary<string,string> Read(string culture)=>manager.GetResourceSet(CultureInfo.GetCultureInfo(culture),true,false)!.Cast<DictionaryEntry>().ToDictionary(v=>(string)v.Key,v=>(string)v.Value!);
var chinese=Read("zh-CN");var english=Read("en");
Check("full English satellite has exact neutral resource keys",chinese.Count>=731 && chinese.Keys.Order().SequenceEqual(english.Keys.Order()));
Check("English resources contain no Chinese UI fallback",english.Values.All(v=>!Regex.IsMatch(v,"[\\u4e00-\\u9fff]")));
var placeholder=new Regex(@"(?<!\{)\{(\d+)(?:,[^{}:]+)?(?::[^{}]+)?\}(?!\})");
Check("all numbered placeholders preserved",chinese.All(pair=>placeholder.Matches(pair.Value).Select(v=>v.Groups[1].Value).Order().SequenceEqual(placeholder.Matches(english[pair.Key]).Select(v=>v.Groups[1].Value).Order())));
foreach(var language in new[]{"zh-CN","en"})
{
    UiText.Configure(language);var expected=language=="en"?english:chinese;
    Check(language+" actual ResourceManager resolves every translated value",expected.All(pair=>UiText.Get(pair.Key)==pair.Value));
    Check(language+" all composite messages format without lost arguments",expected.All(pair=>{try{UiText.Format(pair.Key,Enumerable.Repeat<object?>(1234.5,8).ToArray());return true;}catch(FormatException){return false;}}));
    Check(language+" UI and numeric cultures agree",CultureInfo.CurrentCulture.Name==(language=="en"?"en-US":"zh-CN") && CultureInfo.CurrentUICulture.Name==CultureInfo.CurrentCulture.Name && CultureInfo.DefaultThreadCurrentCulture?.Name==CultureInfo.CurrentCulture.Name);
    var date=new DateTime(2026,10,15,13,25,0).ToString("g");
    Check(language+" date numeric order is unambiguous for selected culture",date.Contains(language=="en"?"10/15/2026":"2026/10/15"));
    var data=Path.Combine(folder,language);Directory.CreateDirectory(data);
    var settings=Path.Combine(data,"settings.json");await File.WriteAllTextAsync(settings,"{\"Version\":99,\"Unknown\":\"保留\"}");
    var vaultPath=Path.Combine(data,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
    var password=new SavedPassword(Guid.NewGuid(),"解压", "中文 secret ' $ & 密码", "原始备注",true,DateTimeOffset.UtcNow);
    await vault.SaveAsync(password);var settingsHash=Hash(settings);var vaultHash=Hash(vaultPath);
    Check(language+" missing preference defaults to Chinese without creating file",LanguagePreferenceStore.Read(data)==new LanguagePreference("zh-CN",true) && !File.Exists(Path.Combine(data,LanguagePreferenceStore.FileName)));
    await LanguagePreferenceStore.SaveAsync(data,language);
    Check(language+" saved choice survives reopening",LanguagePreferenceStore.Read(data)==new LanguagePreference(language,true));
    Check(language+" settings and encrypted vault bytes unchanged",Hash(settings)==settingsHash && Hash(vaultPath)==vaultHash);
    var reopened=(await new DpapiPasswordVault(vaultPath).ReadAsync()).Single();
    Check(language+" names notes and secrets are not translated",reopened.Name==password.Name && reopened.Notes==password.Notes && reopened.Secret==password.Secret);
    var catalog=new EngineCatalog(Path.Combine(root,"engines"),Path.Combine(data,EngineArchitecture.InstalledDirectoryName));
    var updater=new EngineUpdateService(Path.Combine(data,EngineArchitecture.InstalledDirectoryName),Path.Combine(data,EngineArchitecture.SelectionFileName),"",Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"resources/update-probe/encrypted-header.7z"),catalog);
    foreach(var version in new[]{"26.03","26.04"})
    {
        await updater.ActivateAsync(version);
        Check(language+" real encrypted engine probe unchanged "+version,(await updater.ReadSelectionAsync())?.Active==version);
    }
}
var damaged=Path.Combine(folder,"damaged");Directory.CreateDirectory(damaged);
var preference=Path.Combine(damaged,LanguagePreferenceStore.FileName);
foreach(var text in new[]{"not json","null","{\"Version\":99,\"Language\":\"en\"}","{\"Version\":1,\"Language\":\"../other\"}"})
{
    await File.WriteAllTextAsync(preference,text);var oldHash=Hash(preference);
    Check("invalid preference falls back without claiming writable "+text,LanguagePreferenceStore.Read(damaged)==new LanguagePreference("zh-CN",false));
    var refused=false;try{await LanguagePreferenceStore.SaveAsync(damaged,"en");}catch(InvalidDataException){refused=true;}
    Check("invalid preference refused without replacement "+text,refused && Hash(preference)==oldHash);
}
var locked=Path.Combine(folder,"locked");await LanguagePreferenceStore.SaveAsync(locked,"zh-CN");
var lockedPath=Path.Combine(locked,LanguagePreferenceStore.FileName);var lockedHash=Hash(lockedPath);
using(var stream=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
{
    var refused=false;try{await LanguagePreferenceStore.SaveAsync(locked,"en");}catch(Exception error) when(error is IOException or UnauthorizedAccessException){refused=true;}
    Check("real sharing lock preserves previous preference",refused && Hash(lockedPath)==lockedHash);
}
Check("failed atomic save leaves no temporary files",!Directory.EnumerateFiles(locked,"*.tmp").Any());
var badChoice=false;try{await LanguagePreferenceStore.SaveAsync(Path.Combine(folder,"invalid-choice"),"fr");}catch(ArgumentException){badChoice=true;}
Check("unsupported requested language cannot be saved",badChoice && !Directory.Exists(Path.Combine(folder,"invalid-choice")));
UiText.Configure("unsupported");Check("unsupported startup culture falls back to Chinese",UiText.LanguageCode=="zh-CN" && UiText.Get("Language.Title")=="界面语言");
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/localization-validation.json"),JsonSerializer.Serialize(new{passed=checks.Values.All(v=>v),total=checks.Count,checks,resourceCount=chinese.Count,syntheticData=true,productionProfileAccessed=false},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Values.All(v=>v),total=checks.Count,resources=chinese.Count,failed=checks.Where(p=>!p.Value).Select(p=>p.Key)}));
return checks.Values.All(v=>v)?0:1;
