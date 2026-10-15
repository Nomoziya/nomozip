using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Test-owned ConPTY only; no window or input is attached to the user's terminal.
internal sealed class ConptySession:IDisposable
{
    private IntPtr console,process,thread,attributes,inputRead,outputWrite;
    private FileStream? input,output;
    private readonly StringBuilder text=new();private readonly object gate=new();private Task? drain;
    internal ConptySession(string application,IEnumerable<string> arguments)
    {
        try
        {
            if(!CreatePipe(out inputRead,out var inputWrite,IntPtr.Zero,0)) throw new Win32Exception();
            input=new FileStream(new SafeFileHandle(inputWrite,true),FileAccess.Write);
            if(!CreatePipe(out var outputRead,out outputWrite,IntPtr.Zero,0)) throw new Win32Exception();
            output=new FileStream(new SafeFileHandle(outputRead,true),FileAccess.Read);
            var code=CreatePseudoConsole(new Coord {X=2000,Y=100},inputRead,outputWrite,0,out console);if(code!=0) Marshal.ThrowExceptionForHR(code);
            drain=Task.Run(()=>
            {
                using var reader=new StreamReader(output,Encoding.UTF8);var buffer=new char[4096];int count;
                while((count=reader.Read(buffer,0,buffer.Length))>0) lock(gate) text.Append(buffer,0,count);
            });
            nuint size=0;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);attributes=Marshal.AllocHGlobal(checked((int)size));
            if(!InitializeProcThreadAttributeList(attributes,1,0,ref size) || !UpdateProcThreadAttribute(attributes,0,(IntPtr)0x20016,console,(nuint)IntPtr.Size,IntPtr.Zero,IntPtr.Zero)) throw new Win32Exception();
            // Explicit null standard handles let ConPTY assign console handles even when this
            // test runner itself has redirected pipes. See microsoft/terminal discussion #15814.
            var startup=new StartupEx {Info=new Startup {Size=Marshal.SizeOf<StartupEx>(),Flags=0x100},Attributes=attributes};
            var command=new StringBuilder(string.Join(' ',new[]{application}.Concat(arguments).Select(Quote)));
            if(!CreateProcess(application,command,IntPtr.Zero,IntPtr.Zero,false,0x80000,IntPtr.Zero,Path.GetDirectoryName(application)!,ref startup,out var info)) throw new Win32Exception();
            process=info.Process;thread=info.Thread;
            CloseHandle(inputRead);inputRead=IntPtr.Zero;CloseHandle(outputWrite);outputWrite=IntPtr.Zero;
        }
        catch {Dispose();throw;}
    }
    private string Text {get {lock(gate) return text.ToString();}}
    internal async Task WaitForTextAsync(string value)
    {
        var deadline=DateTime.UtcNow.AddSeconds(25);
        while(!Text.Contains(value,StringComparison.Ordinal))
        {if(DateTime.UtcNow>deadline || WaitForSingleObject(process,0)==0) throw new InvalidOperationException("Expected private terminal prompt was not received: "+value+"; "+Text);await Task.Delay(25);}
    }
    internal void Send(string value) {var bytes=Encoding.UTF8.GetBytes(value);input!.Write(bytes);input.Flush();}
    internal async Task<(int Exit,string Text)> FinishAsync()
    {
        var wait=await Task.Run(()=>WaitForSingleObject(process,30000));if(wait!=0) throw new TimeoutException("Private terminal child did not finish.");
        if(!GetExitCodeProcess(process,out var exit)) throw new Win32Exception();
        ClosePseudoConsole(console);console=IntPtr.Zero;
        if(drain is not null) await drain.WaitAsync(TimeSpan.FromSeconds(10));
        return(unchecked((int)exit),Text);
    }
    public void Dispose()
    {
        if(process!=IntPtr.Zero && WaitForSingleObject(process,0)!=0) {TerminateProcess(process,2);WaitForSingleObject(process,5000);}
        if(console!=IntPtr.Zero) {ClosePseudoConsole(console);console=IntPtr.Zero;}
        input?.Dispose();output?.Dispose();
        foreach(var handle in new[]{inputRead,outputWrite,thread,process}) if(handle!=IntPtr.Zero) CloseHandle(handle);
        inputRead=outputWrite=thread=process=IntPtr.Zero;
        if(attributes!=IntPtr.Zero) {DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);attributes=IntPtr.Zero;}
    }
    private static string Quote(string value)
    {
        var result=new StringBuilder("\"");int slashes=0;
        foreach(var character in value)
        {
            if(character=='\\') {slashes++;continue;}
            result.Append('\\',character=='"'?slashes*2+1:slashes);result.Append(character);slashes=0;
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Coord {internal short X,Y;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Startup
    {internal int Size;internal IntPtr Reserved,Desktop,Title;internal int X,Y,XSize,YSize,XChars,YChars,Fill,Flags;internal short Show,ReservedSize;internal IntPtr ReservedPointer,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential)] private struct StartupEx {internal Startup Info;internal IntPtr Attributes;}
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo {internal IntPtr Process,Thread;internal uint ProcessId,ThreadId;}
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool CreatePipe(out IntPtr read,out IntPtr write,IntPtr security,uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size,IntPtr input,IntPtr output,uint flags,out IntPtr console);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref nuint size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,nuint size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref StartupEx startup,out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr handle,out uint exit);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr handle,uint exit);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
