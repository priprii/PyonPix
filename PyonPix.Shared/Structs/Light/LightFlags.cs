namespace PyonPix.Shared.Structs.Light;

[Flags]
public enum LightFlags : uint {
    Reflections = 0x01,
    DynamicShadows = 0x02,
    CharacterShadows = 0x04,
    ObjectShadows = 0x08
}
