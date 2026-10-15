using System.Text;
using ArchivePassword.Cli;

Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
using var cancellation=new CancellationTokenSource();
ConsoleCancelEventHandler cancel=(_,args)=>{args.Cancel=true;cancellation.Cancel();};Console.CancelKeyPress+=cancel;
try {return await CliRunner.RunAsync(args,cancellation.Token);}
finally {Console.CancelKeyPress-=cancel;}
