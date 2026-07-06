using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;
using PyonPix.Config.Global;
using PyonPix.Config.Pix;
using PyonPix.Config.Sync;
using PyonPix.Config.UI;
using PyonPix.Structs.Browser;

namespace PyonPix.Config;

[Serializable]
public class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 1;

    public bool Enabled = true;

    public SyncProperties Sync = new();

    public GlobalProperties Global = new();

    public UIProperties UI = new();

    public Dictionary<long, Dictionary<string, PixVariant>> PixVariants = [];

    public List<LocalPix> LocalPixs = [];

    public Dictionary<string, Extension> Extensions = [];

    [NonSerialized] private IDalamudPluginInterface PluginInterface = null!;

    public void Initialize(IDalamudPluginInterface pi) {
        PluginInterface = pi;
    }

    public void Save() {
        PluginInterface.SavePluginConfig(this);
    }

    public string GetConfigPath() => PluginInterface!.GetPluginConfigDirectory();
}
