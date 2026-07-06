using System.Numerics;

namespace PyonPix.Config.UI.Properties;

public class UpdatesUIProperties {
    public bool IsOpen = false;
    public bool Collapsed = false;
    public Vector2 ExpandedSize;

    public bool ShowUpdates = true;
    public string LastVersion = string.Empty;
}
