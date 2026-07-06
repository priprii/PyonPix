using System;

namespace PyonPix.Structs.Browser;

[Flags]
public enum SpawnBehaviour {
    None = 0,
    Show = 1 << 1,
    Expand = 1 << 2,
    Unmute = 1 << 3,
    Navigate = 1 << 4
}
