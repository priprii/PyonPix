using System.Collections.Generic;
using System.Numerics;

namespace PyonPix.Config.UI.Properties;

public class MainUIProperties {
    public bool IsOpen = false;
    public bool Collapsed = false;
    public Vector2 ExpandedSize;

    public HashSet<string> ExpandedTerritories = [];
}
