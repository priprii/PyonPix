using PyonPix.Config.Global.Properties;

namespace PyonPix.Config.Global;

public class GlobalProperties {
    public GeneralGlobalProperties General { get; set; } = new();
    public BrowserGlobalProperties Browser { get; set; } = new();
    public RendererGlobalProperties Renderer { get; set; } = new();
    public LightGlobalProperties Light { get; set; } = new();
    public AudioGlobalProperties Audio { get; set; } = new();
}
