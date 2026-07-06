using System.Collections.Generic;
using System.Numerics;
using PyonPix.Shared.Structs.Pix;

namespace PyonPix.Config.UI.Properties;

public class SyncSearchUIProperties {
    public bool IsOpen = false;
    public bool Collapsed = false;
    public Vector2 ExpandedSize;

    public bool ShowNsfw = true;
    public bool SameTerritoryOnly = false;
    public HashSet<PixType> TypeFilters = [];
    public int RegionActiveTabIndex = 0;
    public HashSet<ushort> WorldFilters = [];
}
