using ArchivePassword.Core;

namespace ArchivePassword.Cli;
internal sealed class CliOptions
{
    internal string Command {get;private set;}="help";
    internal string DataDirectory {get;private set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ArchivePassword");
    internal string? Engine {get;private set;}
    internal string? Output {get;private set;}
    internal string Format {get;private set;}="7z";
    internal int Level {get;private set;}=5;
    internal bool Encrypt {get;private set;}
    internal bool EncryptHeaders {get;private set;}=true;
    internal bool Replace {get;private set;}
    internal bool UseVault {get;private set;}
    internal Guid? PasswordId {get;private set;}
    internal bool PasswordStdin {get;private set;}
    internal bool MasterStdin {get;private set;}
    internal bool Interactive {get;private set;}
    internal bool Progress {get;private set;}
    internal string Conflict {get;private set;}="fail";
    internal string? CancelFile {get;private set;}
    internal List<string> Paths {get;}=[];
    internal static CliOptions Parse(string[] args)
    {
        var value=new CliOptions();if(args.Length==0) return value;
        value.Command=args[0] switch {"--help" or "-h"=>"help","--version"=>"version",var command=>command};
        if(value.Command is not ("help" or "version" or "extract" or "test" or "create" or "vault-list")) throw new ArgumentException();
        if(value.Command is "help" or "version") {if(args.Length!=1) throw new ArgumentException();return value;}
        var options=new Dictionary<string,string?>(StringComparer.Ordinal);bool positional=false;
        var flags=new HashSet<string>{"--encrypt","--no-encrypt-headers","--replace","--use-vault","--password-stdin","--master-password-stdin","--interactive","--progress"};
        var keys=new HashSet<string>{"--output","--format","--level","--data-dir","--engine","--password-id","--on-conflict","--cancel-file"};
        for(int i=1;i<args.Length;i++)
        {
            var arg=args[i];if(!positional && arg=="--") {positional=true;continue;}
            if(!positional && arg.StartsWith('-'))
            {
                if(!flags.Contains(arg) && !keys.Contains(arg) || options.ContainsKey(arg)) throw new ArgumentException();
                string? content=null;if(keys.Contains(arg)) {if(++i>=args.Length || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException();content=args[i];}
                options.Add(arg,content);
            }
            else value.Paths.Add(arg);
        }
        bool Has(string key)=>options.ContainsKey(key);
        string Text(string key,string fallback)=>options.GetValueOrDefault(key)??fallback;
        value.DataDirectory=Path.GetFullPath(Text("--data-dir",value.DataDirectory));
        value.Engine=options.GetValueOrDefault("--engine");if(value.Engine is not null && !System.Text.RegularExpressions.Regex.IsMatch(value.Engine,@"^\d{2}\.\d{2}$")) throw new ArgumentException();
        value.Output=options.GetValueOrDefault("--output");if(value.Output is not null) value.Output=Path.GetFullPath(value.Output);
        value.Format=Text("--format","7z");if(CompressionFormats.Find(value.Format) is null) throw new ArgumentException();
        if(!int.TryParse(Text("--level","5"),out var level) || level is not (0 or 1 or 3 or 5 or 7 or 9)) throw new ArgumentException();value.Level=level;
        value.Encrypt=Has("--encrypt");value.EncryptHeaders=!Has("--no-encrypt-headers");value.Replace=Has("--replace");
        value.UseVault=Has("--use-vault");value.PasswordStdin=Has("--password-stdin");value.MasterStdin=Has("--master-password-stdin");
        value.Interactive=Has("--interactive");value.Progress=Has("--progress");value.Conflict=Text("--on-conflict","fail");
        value.CancelFile=options.GetValueOrDefault("--cancel-file");if(value.CancelFile is not null) value.CancelFile=Path.GetFullPath(value.CancelFile);
        if(Has("--password-id")) {if(!Guid.TryParse(options["--password-id"],out var id) || id==Guid.Empty) throw new ArgumentException();value.PasswordId=id;}
        if(value.Conflict is not ("fail" or "skip" or "replace") || value.PasswordStdin && value.PasswordId is not null || value.UseVault && value.PasswordId is not null) throw new ArgumentException();
        if(value.MasterStdin && !value.UseVault && value.PasswordId is null && value.Command!="vault-list") throw new ArgumentException();
        if(value.Command=="vault-list")
        {if(value.Paths.Count>0 || options.Keys.Any(key=>key is not ("--data-dir" or "--master-password-stdin" or "--interactive" or "--cancel-file"))) throw new ArgumentException();}
        else if(value.Command=="create")
        {
            if(value.Paths.Count==0 || value.Output is null || Has("--on-conflict") || value.UseVault
                || !value.Encrypt && (value.PasswordStdin || value.PasswordId is not null || Has("--no-encrypt-headers"))
                || value.Encrypt && !CompressionFormats.Find(value.Format)!.Encryption
                || Has("--no-encrypt-headers") && value.Format!="7z") throw new ArgumentException();
        }
        else
        {
            if(value.Paths.Count!=1 || options.Keys.Any(key=>key is "--format" or "--level" or "--encrypt" or "--no-encrypt-headers" or "--replace")
                || value.Command=="extract" && value.Output is null || value.Command=="test" && (value.Output is not null || Has("--on-conflict"))) throw new ArgumentException();
        }
        return value;
    }
}
