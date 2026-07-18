using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PyonPix.Config;
using PyonPix.Services.Core;
using PyonPix.Services.Game;

namespace PyonPix.Services;

public sealed class ServiceContext : IServiceContext {
    private readonly ConcurrentDictionary<Type, object> _services = new();

    [PluginService] public IClientState ClientState { get; private set; } = null!;
    [PluginService] public ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public ICondition Condition { get; private set; } = null!;
    [PluginService] public IDataManager DataManager { get; private set; } = null!;
    [PluginService] public IFramework Framework { get; private set; } = null!;
    [PluginService] public IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public IPluginLog Log { get; private set; } = null!;
    [PluginService] public IObjectTable Objects { get; private set; } = null!;
    [PluginService] public IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public ITextureProvider TextureProvider { get; private set; } = null!;

    public static ServiceContext Instance { get; private set; } = null!;

    public ServiceContext(IDalamudPluginInterface pi) {
        pi.Inject(this);
        Instance = this;
    }

    public TService Register<TService>(TService service) where TService : class {
        _services[typeof(TService)] = service;
        return service;
    }

    public TService Get<TService>() where TService : class {
        if(TryGet<TService>(out var service) && service != null) return service;
        throw new InvalidOperationException($"Service Failure: {typeof(TService).Name}");
    }

    public bool TryGet<TService>(out TService? service) where TService : class {
        if(_services.TryGetValue(typeof(TService), out var value) && value is TService typed) {
            service = typed;
            return true;
        }

        object? pluginService = typeof(TService).Name switch {
            nameof(IClientState) => ClientState,
            nameof(ICommandManager) => CommandManager,
            nameof(ICondition) => Condition,
            nameof(IDataManager) => DataManager,
            nameof(IFramework) => Framework,
            nameof(IGameInteropProvider) => GameInteropProvider,
            nameof(IPluginLog) => Log,
            nameof(IObjectTable) => Objects,
            nameof(IPlayerState) => PlayerState,
            nameof(IDalamudPluginInterface) => PluginInterface,
            nameof(ITextureProvider) => TextureProvider,
            _ => null
        };

        if(pluginService is TService svc) {
            service = svc;
            return true;
        }

        service = null;
        return false;
    }

    public async Task Initialize(Configuration config) {
        Register(new StateService(config, this));
        Register(new SyncService(config, this));
        Register(new PixService(config, this));
        Register(new DXService(config, this));
        Register(new ExtensionsService(config, this));
        Register(new DataService(config, this));
        Register(new BrowserService(config, this));
        Register(new LightService(config, this));
        Register(new RendererService(config, this));
        Register(new PixInputService(config, this));

        await Get<StateService>().Initialize();
        await Get<SyncService>().Initialize();
        await Get<PixService>().Initialize();
        await Get<DXService>().Initialize();
        await Get<ExtensionsService>().Initialize();
        await Get<DataService>().Initialize();
        await Get<BrowserService>().Initialize();
        await Get<LightService>().Initialize();
        await Get<RendererService>().Initialize();
        await Get<PixInputService>().Initialize();

        Framework.Update += Update;
    }

    public void Update(IFramework framework) {
        if(TryGet<StateService>(out var state)) state!.Update();
        if(TryGet<SyncService>(out var sync)) sync!.Update();
        if(TryGet<BrowserService>(out var browser)) browser!.Update();
        if(TryGet<RendererService>(out var renderer)) renderer!.Update();
        if(TryGet<PixInputService>(out var input)) input!.Update();
    }

    public async Task Dispose() {
        Framework.Update -= Update;

        if(TryGet<PixInputService>(out var input)) await input!.Dispose();
        if(TryGet<StateService>(out var state)) await state!.Dispose();
        if(TryGet<SyncService>(out var sync)) await sync!.Dispose();
        if(TryGet<RendererService>(out var renderer)) await renderer!.Dispose();
        if(TryGet<LightService>(out var light)) await light!.Dispose();
        if(TryGet<BrowserService>(out var browser)) await browser!.Dispose();
        if(TryGet<ExtensionsService>(out var extensions)) await extensions!.Dispose();
        if(TryGet<DataService>(out var data)) await data!.Dispose();
    }
}
