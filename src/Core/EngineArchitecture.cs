using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ArchivePassword.Core;
public static class EngineArchitecture
{
    public static string Current => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => throw new PlatformNotSupportedException("This product requires x64 or ARM64.")
    };
    public static bool IsNativeImage(ReadOnlySpan<byte> bytes, string? architecture = null)
    {
        var target = architecture ?? Current;
        var machine = target switch { "x64" => 0x8664, "arm64" => 0xaa64, _ => 0 };
        if (machine == 0 || bytes.Length < 88 || bytes[0] != 'M' || bytes[1] != 'Z') return false;
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes[60..]);
        return offset >= 64 && offset <= bytes.Length - 24 && bytes.Slice(offset,4).SequenceEqual("PE\0\0"u8)
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset+4)..]) == machine;
    }
    public static string InstalledDirectoryName => Current == "x64" ? "engines" : "engines-arm64";
    public static string SelectionFileName => Current == "x64" ? "active-engine.json" : "active-engine-arm64.json";
}
