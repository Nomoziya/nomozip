namespace ArchivePassword.Cli;
internal static class TerminalSecret
{
    internal static async Task<string?> ReadAsync(string label,CancellationToken token)
    {
        if(Console.IsInputRedirected) throw new ArgumentException();
        Console.Error.Write(label+": ");var buffer=new List<char>();
        try
        {
            while(true)
            {
                token.ThrowIfCancellationRequested();
                if(!Console.KeyAvailable) {await Task.Delay(25,token);continue;}
                var key=Console.ReadKey(true);
                if(key.Key==ConsoleKey.Escape) return null;
                if(key.Key==ConsoleKey.Enter) return new string(buffer.ToArray());
                if(key.Key==ConsoleKey.Backspace) {if(buffer.Count>0) {buffer.RemoveAt(buffer.Count-1);Console.Error.Write("\b \b");}continue;}
                if(!char.IsControl(key.KeyChar)) {buffer.Add(key.KeyChar);Console.Error.Write('*');}
            }
        }
        finally {for(int i=0;i<buffer.Count;i++) buffer[i]='\0';buffer.Clear();Console.Error.WriteLine();}
    }
}
