using Microsoft.Win32;
using System.Text.Json;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var scope = @"Software\nomozip-validation\Associations-" + Guid.NewGuid().ToString("N");
var app = Path.GetFullPath(Path.Combine(root, "artifacts/association validation/ArchivePassword.exe"));
Directory.CreateDirectory(Path.GetDirectoryName(app)!); File.Copy(Path.Combine(root, "artifacts/app/ArchivePassword.exe"), app, true);
var checks = new Dictionary<string, bool>();
try
{
    using var key = Registry.CurrentUser.CreateSubKey(scope);
    foreach (var extension in new[] { ".7z", ".zip", ".rar" })
    {
        using var original = key.CreateSubKey(extension); original.SetValue("", "previous.application");
        using var choices = original.CreateSubKey("OpenWithProgids"); choices.SetValue("previous.application", "");
    }
    var service = new FileAssociationService(app, scope); service.Register();
    checks["registeredOwner"] = service.IsRegistered();
    using (var command = key.OpenSubKey(FileAssociationService.ProgId + @"\shell\open\command")) checks["commandQuotesExecutableAndArchiveArgument"] = command?.GetValue("") as string == $"\"{app}\" \"%1\"";
    checks["openWithOfferedForSupportedFormats"] = new[] { ".7z", ".zip", ".rar" }.All(extension => { using var choices = key.OpenSubKey(extension + @"\OpenWithProgids"); return choices!.GetValueNames().Contains(FileAssociationService.ProgId); });
    checks["existingDefaultsPreserved"] = new[] { ".7z", ".zip", ".rar" }.All(extension => { using var original = key.OpenSubKey(extension); return original?.GetValue("") as string == "previous.application"; });
    checks["anotherCopyCannotRemoveOwner"] = !new FileAssociationService(app + ".other", scope).Unregister() && service.IsRegistered();
    checks["ownerCanRemoveEntry"] = service.Unregister() && !service.IsRegistered();
    checks["otherApplicationEntriesPreserved"] = new[] { ".7z", ".zip", ".rar" }.All(extension => { using var choices = key.OpenSubKey(extension + @"\OpenWithProgids"); return choices!.GetValueNames().Contains("previous.application") && !choices.GetValueNames().Contains(FileAssociationService.ProgId); });
}
finally { Registry.CurrentUser.DeleteSubKeyTree(scope, false); }
var passed = checks.Values.Count(value => value);
await File.WriteAllTextAsync(Path.Combine(root, "artifacts/association-validation.json"), JsonSerializer.Serialize(new { passed, total = checks.Count, isolatedRegistry = true, checks }));
Console.WriteLine($"{passed}/{checks.Count} association checks passed."); return passed == checks.Count ? 0 : 1;
