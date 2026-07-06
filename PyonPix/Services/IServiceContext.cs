using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace PyonPix.Services;

public interface IServiceContext {
    IClientState ClientState { get; }
    ICommandManager CommandManager { get; }
    ICondition Condition { get; }
    IDataManager DataManager { get; }
    IFramework Framework { get; }
    IGameInteropProvider GameInteropProvider { get; }
    IPluginLog Log { get; }
    IObjectTable Objects { get; }
    IPlayerState PlayerState { get; }
    IDalamudPluginInterface PluginInterface { get; }
    ITextureProvider TextureProvider { get; }

    TService Register<TService>(TService service) where TService : class;
    TService Get<TService>() where TService : class;
    bool TryGet<TService>(out TService? service) where TService : class;

    Task Initialize(Config.Configuration config);
    void Update(IFramework framework);
    Task Dispose();
}
