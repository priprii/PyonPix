using PyonPix.Structs.Browser;

namespace PyonPix.Config.Global.Properties;

public class BrowserGlobalProperties {
    public HomeUriType HomeUriType = HomeUriType.Starry;
    public string HomeUri = string.Empty;

    public SpawnBehaviour TerritorySpawnBehaviour = SpawnBehaviour.Navigate;
    public DespawnBehaviour TerritoryDespawnBehaviour = DespawnBehaviour.Shutdown;

    public bool CheckUpdateExtensions = false;
    public bool AutoUpdateExtensions = false;
}
