using System;

namespace PyonPix.Structs.Browser;

[Flags]
public enum DespawnBehaviour {
    None = 0,
    Hide = 1 << 1,
    Collapse = 1 << 2,
    Mute = 1 << 3,
    Shutdown = 1 << 4
}
