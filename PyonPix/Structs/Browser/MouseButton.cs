using System;

namespace PyonPix.Structs.Browser;

[Flags]
public enum MouseButton : uint {
    None = 0,
    Left = 1 << 1,
    Right = 1 << 2,
    Middle = 1 << 3,
}
