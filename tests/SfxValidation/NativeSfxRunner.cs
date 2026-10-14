using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Run only owned synthetic EXEs on a private, never switched-to Windows desktop.
// The password argument is a fixture constant, never a user credential.
internal static class NativeSfxRunner
{
    internal static int Run(string exe,string output,string? fixturePassword)
    {
        var desktopName="nomozip-sfx-"+Guid.NewGuid().ToString("N");var desktop=CreateDesktop(desktopName,null,IntPtr.Zero,0,0x10000000,IntPtr.Zero);
        if(desktop==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var system=Environment.GetEnvironmentVariable("SystemRoot")!;
        var environment=Marshal.StringToHGlobalUni($"SystemRoot={system}\0WINDIR={system}\0PATH={system}\\System32\0TEMP={Path.GetDirectoryName(exe)}\0TMP={Path.GetDirectoryName(exe)}\0\0");
        var process=new ProcessInformation();
        try
        {
            var startup=new StartupInfo {cb=Marshal.SizeOf<StartupInfo>(),desktop=desktopName,flags=1,showWindow=0};
            var command=new StringBuilder($"\"{exe}\" -y -o\"{output}\""+(fixturePassword is null?"":" -p\""+fixturePassword+"\""));
            if(!CreateProcess(exe,command,IntPtr.Zero,IntPtr.Zero,false,0x400,environment,Path.GetDirectoryName(exe),ref startup,out process)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if(WaitForSingleObject(process.process,60000)!=0) {TerminateProcess(process.process,99);WaitForSingleObject(process.process,5000);throw new TimeoutException("Owned SFX fixture did not complete.");}
            if(!GetExitCodeProcess(process.process,out var result)) throw new Win32Exception(Marshal.GetLastWin32Error());return (int)result;
        }
        finally {if(process.thread!=IntPtr.Zero) CloseHandle(process.thread);if(process.process!=IntPtr.Zero) CloseHandle(process.process);Marshal.FreeHGlobal(environment);CloseDesktop(desktop);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct StartupInfo {public int cb;public string? reserved,desktop,title;public int x,y,xSize,ySize,xCount,yCount,fillAttribute,flags;public short showWindow,reservedSize;public IntPtr reservedPointer,stdInput,stdOutput,stdError;}
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation {public IntPtr process,thread;public int processId,threadId;}
    [DllImport("user32.dll",EntryPoint="CreateDesktopW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateDesktop(string name,string? device,IntPtr mode,int flags,uint access,IntPtr security);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,int flags,IntPtr environment,string? directory,ref StartupInfo startup,out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
