using System.Collections.Generic;
using PyonPix.Services.Game;
using PyonPix.Shared.Structs;

namespace PyonPix.Config.Sync;

public class SyncProperties {
    public bool AutoConnect = false;
    public string SecretKey = string.Empty;
    public Dictionary<long, CharacterProperties> Characters = [];

    public CharacterProperties GetCurrentCharacterProperties(Configuration config, StateService state) {
        if(!Characters.TryGetValue(state.LocalPlayerContentId, out var c)) {
            c = new();
            Characters[state.LocalPlayerContentId] = c;
            config.Save();
        }
        return c;
    }
}
