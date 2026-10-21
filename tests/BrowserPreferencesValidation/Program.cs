using System.IO;
using System.Text.Json;
using System.Windows;
using ArchivePassword.Core;
using ArchivePassword.Desktop;
using ArchivePassword.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Isolated CI only; no real profile.");
        var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        application.Startup+=async(_,_)=>
        {
            string? error=null;var passed=false;
            try
            {
                UiText.Configure("en");var owner=new Window();
                var model=new MainViewModel(args[0],args[1],new DpapiPasswordVault(Path.Combine(args[1],"synthetic.apv")),new WindowDialogs(owner));
                await model.InitializeAsync();
                var expected=JsonSerializer.Deserialize<BrowserPreferences>(await File.ReadAllTextAsync(args[2]))!;
                passed=JsonSerializer.Serialize(expected)==JsonSerializer.Serialize(model.BrowserPreferences);
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(args[3],JsonSerializer.Serialize(new{passed,error,processId=Environment.ProcessId,scope="Fresh process initializes the production MainViewModel from the same synthetic settings file; no window shown."}));
            application.Shutdown(passed?0:1);
        };
        return application.Run();
    }
}
