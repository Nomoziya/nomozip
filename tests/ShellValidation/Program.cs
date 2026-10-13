using System.Text.Json;
using Microsoft.Win32;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var scope = @"Software\nomozip-validation\Shell-" + Guid.NewGuid().ToString("N");
var executable = Path.GetFullPath(Path.Combine(root, "artifacts/app/ArchivePassword.exe"));
if (args.Length > 1) executable = Path.GetFullPath(args[1]);
if (args.Length > 2)
{
    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted") throw new Exception("Live Shell registration is limited to disposable hosted VMs.");
    var live = new ShellMenuService(executable);
    if (args[2] == "register-live") live.Register();
    else if (args[2] == "remove-live") live.Unregister();
    else throw new ArgumentException("Unknown live test action.");
    return;
}
var checks = new Dictionary<string, bool>();
void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) throw new Exception(name); }
var complete = false;
try
{
    using var classes = Registry.CurrentUser.CreateSubKey(scope);
    using (var previous = classes.CreateSubKey(".zip")) previous.SetValue("", "previous.zip.app");
    using (var previous = classes.CreateSubKey(@".zip\OpenWithProgids")) previous.SetValue("previous.zip.app", "");
    var menu = new ShellMenuService(executable, scope); menu.Register();
    Check("all menu and COM owners registered", menu.IsRegistered());
    foreach (var id in ShellMenuService.Classes)
    {
        using var server = classes.OpenSubKey(ShellMenuService.ClassPath(id) + @"\LocalServer32");
        Check("quoted local server " + id, server?.GetValue("") as string == $"\"{executable}\" --shell-server" && server.GetValue("ServerExecutable") as string == executable);
    }
    foreach (var path in ShellMenuService.MenuRoots)
    {
        using var menuKey = classes.OpenSubKey(path)!;
        Check("menu supports large batch " + path, menuKey.GetValue("MultiSelectModel") as string == "Player");
        using var verbs = menuKey.OpenSubKey("shell")!;
        var names = verbs.GetSubKeyNames();
        Check("scoped commands " + path, names.Length == (path.StartsWith("SystemFileAssociations") ? 3 : 1));
        Check("batch COM delegation " + path, names.All(name =>
        {
            using var verb = verbs.OpenSubKey(name)!; using var command = verb.OpenSubKey("command")!;
            return verb.GetValue("MultiSelectModel") as string == "Player" && Guid.TryParse(command.GetValue("DelegateExecute") as string, out var id) && ShellMenuService.Classes.Contains(id);
        }));
    }
    using (var previous = classes.OpenSubKey(".zip")) Check("zip default untouched", previous?.GetValue("") as string == "previous.zip.app");
    Check("another copy cannot remove menu", !new ShellMenuService(executable + ".other", scope).Unregister() && menu.IsRegistered());
    menu.Register(); Check("register repeat idempotent", menu.IsRegistered());
    Check("owner removes its own menus and COM server", menu.Unregister() && !menu.IsRegistered() && ShellMenuService.MenuRoots.All(path => classes.OpenSubKey(path) is null));
    using (var previous = classes.OpenSubKey(@".zip\OpenWithProgids")) Check("other app open-with entry preserved", previous?.GetValueNames().Contains("previous.zip.app") == true);
    var fixture = Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z");
    Check("duplicate selection deduplicates once", new ShellSelection(ShellAction.ExtractHere, [fixture, fixture]).Validate().Paths.Count == 1);
    Check("per-archive here destination", new ShellSelection(ShellAction.ExtractHere, [fixture]).OutputFor(fixture) == Path.GetDirectoryName(fixture));
    Check("same-name folder destination", new ShellSelection(ShellAction.ExtractFolder, [fixture]).OutputFor(fixture) == Path.Combine(Path.GetDirectoryName(fixture)!, "encrypted-header"));
    foreach (var selection in new[] { new ShellSelection(ShellAction.ExtractHere, []), new ShellSelection(ShellAction.ExtractHere, [fixture, Path.Combine(root, "does-not-exist.zip")]), new ShellSelection(ShellAction.ExtractHere, [Path.Combine(root, "README.md")]) })
    {
        var rejected = false; try { selection.Validate(); } catch (ArgumentException) { rejected = true; }
        Check("invalid mixed selection rejected as a whole " + checks.Count, rejected);
    }
    Check("file folder compression selection", new ShellSelection(ShellAction.Compress, [Path.Combine(root, "README.md"), Path.Combine(root, "docs")]).Validate().Paths.Count == 2);
    complete = true;
}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(scope, false);
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts/shell-registration-validation.json"), JsonSerializer.Serialize(new { passed = complete && checks.Values.All(value => value), total = checks.Count, isolatedRegistry = true, checks }));
}
