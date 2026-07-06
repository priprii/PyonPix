using System;
using System.Collections.Concurrent;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using PyonPix.Config;
using PyonPix.Services;
using PyonPix.Services.Core;
using PyonPix.Ui.Components;
using PyonPix.Ui.Windows;

namespace PyonPix.Ui;

public sealed class WindowContext : IWindowContext {
    private readonly ConcurrentDictionary<Type, object> _windows = new();
    private WindowSystem? _windowSystem;
    private IServiceContext _services = null!;

    private const string CommandName = "/pyonpix";
    private const string AltCommandName = "/pix";

    public TWindow Register<TWindow>(TWindow window) where TWindow : class {
        _windows[typeof(TWindow)] = window;
        return window;
    }

    public TWindow Get<TWindow>() where TWindow : class {
        if(TryGet<TWindow>(out var window) && window != null) return window;
        throw new InvalidOperationException($"Window Failure: {typeof(TWindow).Name}");
    }

    public bool TryGet<TWindow>(out TWindow? window) where TWindow : class {
        if(_windows.TryGetValue(typeof(TWindow), out var value) && value is TWindow typed) {
            window = typed;
            return true;
        }

        window = null;
        return false;
    }

    public void Initialize(Configuration config, IServiceContext services) {
        _windowSystem = new(Plugin.Name);
        _services = services;

        var isLoggedIn = _services.ClientState.IsLoggedIn;
        _windowSystem.AddWindow(Register(new MainWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.Main.IsOpen }));
        _windowSystem.AddWindow(Register(new BrowserWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.Browser.IsOpen }));
        _windowSystem.AddWindow(Register(new ExtensionsWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.Extensions.IsOpen }));
        _windowSystem.AddWindow(Register(new DataWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.Data.IsOpen }));
        _windowSystem.AddWindow(Register(new SyncSearchWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.SyncSearch.IsOpen }));
        _windowSystem.AddWindow(Register(new ConfigWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.Config.IsOpen }));
        _windowSystem.AddWindow(Register(new PixConfigWindow(config, services, this) { IsOpen = false }));
        _windowSystem.AddWindow(Register(new PixMembersWindow(config, services, this) { IsOpen = false }));
        _windowSystem.AddWindow(Register(new UserWindow(config, services, this) { IsOpen = isLoggedIn && config.UI.User.IsOpen }));

        var updateOpen = false;
        if(config.UI.Updates.LastVersion != Plugin.Version.ToString()) {
            config.UI.Updates.LastVersion = Plugin.Version.ToString();
            config.Save();
            if(config.UI.Updates.ShowUpdates) updateOpen = true;
        }

        _windowSystem.AddWindow(Register(new UpdatesWindow(config, services, this) { IsOpen = updateOpen }));

        _services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Open Main Interface\n" +
            "/pyonpix browser → Open Browser\n" +
            "/pyonpix extensions → Open Extension Manager\n" +
            "/pyonpix data → Open Data Manager\n" +
            "/pyonpix sync → Open Sync Search\n" +
            "/pyonpix config → Open Main Config\n" +
            "/pyonpix user → Open User Config\n" +
            "/pyonpix {PIXID} → Toggle Pix\n" +
            "/pix → Alternative Command Alias"
        });
        _services.CommandManager.AddHandler(AltCommandName, new CommandInfo(OnCommand) { ShowInHelp = false });

        _services.PluginInterface.UiBuilder.OpenMainUi += () => Get<MainWindow>().Toggle();
        _services.PluginInterface.UiBuilder.OpenConfigUi += () => Get<ConfigWindow>().Toggle();
        _services.PluginInterface.UiBuilder.DisableGposeUiHide = true;
        _services.PluginInterface.UiBuilder.Draw += Draw;
    }

    public void Draw() {
        _windowSystem?.Draw();
        Tooltip.Draw();
    }

    public void OnCommand(string command, string args) {
        args = args.Trim().ToLower();
        switch(args) {
            case "browser":
                var browser = Get<BrowserWindow>();
                browser.Toggle();
                if(!browser.IsOpen) {
                    browser.OnCloseUserInteraction();
                }
                break;
            case "extensions":
                Get<ExtensionsWindow>().Toggle();
                break;
            case "data":
                Get<DataWindow>().Toggle();
                break;
            case "sync":
                Get<SyncSearchWindow>().Toggle();
                break;
            case "config":
                Get<ConfigWindow>().Toggle();
                break;
            case "user":
                Get<UserWindow>().Toggle();
                break;
            default:
                if(string.IsNullOrWhiteSpace(args)) {
                    Get<MainWindow>().Toggle();
                } else {
                    _services.Get<PixService>().Toggle(args);
                }
                break;
        }
    }

    public void Dispose() {
        if(TryGet<BrowserWindow>(out var browser)) browser!.Dispose();

        _services.CommandManager.RemoveHandler(CommandName);
        _services.CommandManager.RemoveHandler(AltCommandName);
        _services.PluginInterface.UiBuilder.Draw -= Draw;
        _windowSystem?.RemoveAllWindows();
        _windowSystem = null;
        _windows.Clear();
    }
}
