using System.Runtime.InteropServices;
using PyonPix.Ipc;
using PyonPix.Mediator.Interop;
using PyonPix.Shared.Interop;
using PyonPix.Shared.Ipc;
using static PyonPix.Mediator.Interop.BrowserInterop;

namespace PyonPix.Mediator {
    internal static class Program {
        private static MemoryMappedIpc Ipc = null!;

        private static OnLogCallback OnLog = null!;
        private static OnHostReadyCallback OnHostReady = null!;
        private static OnHostFailedCallback OnHostFailed = null!;
        private static OnTabReadyCallback OnTabReady = null!;
        private static OnTabFailedCallback OnTabFailed = null!;
        private static OnTabDestroyedCallback OnTabDestroyed = null!;
        private static OnFrameReadyCallback OnFrameReady = null!;
        private static OnCursorChangedCallback OnCursorChanged = null!;
        private static OnNavigationStartingCallback OnNavigationStarting = null!;
        private static OnNavigationCompletedCallback OnNavigationCompleted = null!;
        private static OnNavigationCanceledCallback OnNavigationCanceled = null!;
        private static OnHistoryChangedCallback OnHistoryChanged = null!;
        private static OnTitleChangedCallback OnTitleChanged = null!;
        private static OnFavIconChangedCallback OnFavIconChanged = null!;
        private static OnExtensionOperationCallback OnExtensionOperation = null!;

        [STAThread]
        private static void Main(string[] args) {
            Ipc = new MemoryMappedIpc("PyonPix", false);

            try {
                Ipc.OnCommand += ((cmd) => {
                    switch(cmd.Type) {
                        case CommandType.MediatorInitializeRequest: Ipc.SendCommand(CommandType.MediatorInitializeSuccess); break;
                        case CommandType.BrowserHeartbeat: BrowserInterop.Heartbeat(); break;
                        case CommandType.BrowserShutdown: BrowserInterop.Shutdown(); break;
                        case CommandType.BrowserLostFocus: BrowserInterop.LostFocus(); break;
                    }
                });

                InitializeBrowser();
                Ipc.SendCommand(CommandType.MediatorInitializeSuccess);

                Ipc.OnInitializeBrowser += (e) => {
                    var result = BrowserInterop.Initialize(e.PluginPath, e.GamePid, new Shared.Structs.Renderer.LUID() { LowPart = e.LuidLowPart, HighPart = e.LuidHighPart });
                    Ipc.SendCommand(result ? CommandType.BrowserInitializeSuccess : CommandType.BrowserInitializeFailed);
                };

                Ipc.OnCreateTab += (e) => {
                    var extensions = new string[e.ExtensionsLength];
                    for(int i = 0; i < e.ExtensionsLength; i++) {
                        extensions[i] = e.Extensions(i);
                    }
                    BrowserInterop.CreateTab(e.PixId, e.GpuAcceleration, e.X, e.Y, e.W, e.H, e.SyncCookies, extensions, extensions.Length);
                };
                Ipc.OnDestroyTab += (e) => BrowserInterop.DestroyTab(e.PixId);
                Ipc.OnNavigate += (e) => BrowserInterop.Navigate(e.PixId, e.Uri);
                Ipc.OnReload += (e) => BrowserInterop.Reload(e.PixId);
                Ipc.OnStopNavigation += (e) => BrowserInterop.StopNavigation(e.PixId);
                Ipc.OnResize += (e) => BrowserInterop.Resize(e.PixId, e.X, e.Y, e.W, e.H);
                Ipc.OnReposition += (e) => BrowserInterop.Reposition(e.PixId, e.X, e.Y);
                Ipc.OnSetFocusedTab += (e) => BrowserInterop.SetFocusedTab(e.PixId, e.ByUserInput);
                Ipc.OnSendMouseEvent += (e) => BrowserInterop.SendMouseEvent(e.PixId, e.Msg, (nint)e.WParam, (nint)e.LParam);
                Ipc.OnUpdateSpatialAudio += (e) => BrowserInterop.UpdateSpatialAudio(e.PixId, e.Left, e.Right);
                Ipc.OnOpenDevTools += (e) => BrowserInterop.OpenDevTools(e.PixId);
                Ipc.OnInstallExtension += (e) => BrowserInterop.InstallExtension(e.ExtensionId, e.ExtensionName);
                Ipc.OnUninstallExtension += (e) => BrowserInterop.UninstallExtension(e.ExtensionId, e.ExtensionName);
                Ipc.OnEnableExtension += (e) => BrowserInterop.EnableExtension(e.ExtensionId, e.ExtensionName);
                Ipc.OnDisableExtension += (e) => BrowserInterop.DisableExtension(e.ExtensionId, e.ExtensionName);

                Ipc.Start();

                Win32Interop.MessageLoop();

                Ipc.Dispose();
            } catch(Exception ex) {
                Ipc.SendLog(LogType.Error, $"[Mediator] Critical Error: {ex}");
            }
        }

        private static void InitializeBrowser() {
            OnLog = (logType, msg) => Ipc.SendLog(logType, msg);
            OnHostReady = () => Ipc.SendHostInitializeState(StateType.Success);
            OnHostFailed = (message) => Ipc.SendHostInitializeState(StateType.Failed, message);
            OnTabReady = (pixId) => Ipc.SendTabInitializeState(StateType.Success, pixId);
            OnTabFailed = (pixId, message) => Ipc.SendTabInitializeState(StateType.Failed, pixId, message);
            OnTabDestroyed = (pixId) => Ipc.SendTabInitializeState(StateType.TabDestroyed, pixId);
            OnFrameReady = (pixId, sharedTex, w, h) => Ipc.SendUpdateFrame(pixId, sharedTex, w, h);
            OnCursorChanged = (cursorId) => Ipc.SendCursorChanged(cursorId);
            OnNavigationStarting = (pixId, uri, userInitiated) => Ipc.SendNavigationStarting(pixId, uri, userInitiated);
            OnHistoryChanged = (pixId, uri) => Ipc.SendHistoryChanged(pixId, uri);
            OnTitleChanged = (pixId, title) => Ipc.SendTitleChanged(pixId, title);
            OnNavigationCompleted = (pixId, statusCode) => Ipc.SendNavigationCompleted(pixId, statusCode);
            OnNavigationCanceled = (pixId) => Ipc.SendNavigationCanceled(pixId);
            OnFavIconChanged = (pixId, data, length) => {
                if(length <= 0 || data == nint.Zero) return;
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, length);
                Ipc.SendFavIconChanged(pixId, bytes);
            };
            OnExtensionOperation = (extensionOp, extensionId) => Ipc.SendExtensionOperation(extensionOp, extensionId);

            BrowserInterop.RegisterCallbacks(
                OnLog,
                OnHostReady,
                OnHostFailed,
                OnTabReady,
                OnTabFailed,
                OnTabDestroyed,
                OnFrameReady,
                OnCursorChanged,
                OnNavigationStarting,
                OnNavigationCompleted,
                OnNavigationCanceled,
                OnHistoryChanged,
                OnTitleChanged,
                OnFavIconChanged,
                OnExtensionOperation
            );
        }
    }
}
