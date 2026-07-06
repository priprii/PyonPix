using System.Runtime.InteropServices;

namespace PyonPix.Shared.Structs.Renderer;

[StructLayout(LayoutKind.Sequential)]
public struct LUID {
    public uint LowPart;
    public int HighPart;
}
