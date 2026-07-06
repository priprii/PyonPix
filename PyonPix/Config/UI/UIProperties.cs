using PyonPix.Config.UI.Properties;

namespace PyonPix.Config.UI;

public class UIProperties {
    public MainUIProperties Main { get; set; } = new();
    public BrowserUIProperties Browser { get; set; } = new();
    public ExtensionsUIProperties Extensions { get; set; } = new();
    public DataUIProperties Data { get; set; } = new();
    public SyncSearchUIProperties SyncSearch { get; set; } = new();
    public PixConfigUIProperties PixConfig { get; set; } = new();
    public PixMembersUIProperties PixMembers { get; set; } = new();
    public ConfigUIProperties Config { get; set; } = new();
    public UserUIProperties User { get; set; } = new();
    public UpdatesUIProperties Updates { get; set; } = new();
}
