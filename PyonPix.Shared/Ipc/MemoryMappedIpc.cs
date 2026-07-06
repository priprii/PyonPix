using System.Collections.Concurrent;
using Google.FlatBuffers;
using PyonPix.Ipc;
using PyonPix.Shared.Structs.Renderer;

namespace PyonPix.Shared.Ipc;

public sealed class MemoryMappedIpc : IDisposable {
    private readonly IpcChannel _inbound;
    private readonly IpcChannel _outbound;

    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    private Task? _dispatchTask;
    private bool _started;

    private readonly ConcurrentQueue<byte[]> _queue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private readonly Dictionary<MessagePayload, Action<IpcMessage>> PayloadHandlers;

    public event Action<Command>? OnCommand;
    public event Action<Log>? OnLog;
    public event Action<InitializeBrowser>? OnInitializeBrowser;
    public event Action<HostInitializeState>? OnHostInitializeState;
    public event Action<TabInitializeState>? OnTabInitializeState;
    public event Action<CreateTab>? OnCreateTab;
    public event Action<DestroyTab>? OnDestroyTab;
    public event Action<UpdateFrame>? OnUpdateFrame;
    public event Action<CursorChanged>? OnCursorChanged;
    public event Action<NavigationStarting>? OnNavigationStarting;
    public event Action<HistoryChanged>? OnHistoryChanged;
    public event Action<TitleChanged>? OnTitleChanged;
    public event Action<NavigationCompleted>? OnNavigationCompleted;
    public event Action<NavigationCanceled>? OnNavigationCanceled;
    public event Action<FavIconChanged>? OnFavIconChanged;
    public event Action<ExtensionOperation>? OnExtensionOperation;
    public event Action<Navigate>? OnNavigate;
    public event Action<Reload>? OnReload;
    public event Action<StopNavigation>? OnStopNavigation;
    public event Action<Resize>? OnResize;
    public event Action<Reposition>? OnReposition;
    public event Action<SetFocusedTab>? OnSetFocusedTab;
    public event Action<SendMouseEvent>? OnSendMouseEvent;
    public event Action<UpdateSpatialAudio>? OnUpdateSpatialAudio;
    public event Action<OpenDevTools>? OnOpenDevTools;
    public event Action<InstallExtension>? OnInstallExtension;
    public event Action<UninstallExtension>? OnUninstallExtension;
    public event Action<EnableExtension>? OnEnableExtension;
    public event Action<DisableExtension>? OnDisableExtension;

    public MemoryMappedIpc(string baseName, bool isPlugin) {
        string toRenderer = $"{baseName}_ToRenderer";
        string toPlugin = $"{baseName}_ToPlugin";

        _outbound = new IpcChannel(isPlugin ? toRenderer : toPlugin);
        _inbound = new IpcChannel(isPlugin ? toPlugin : toRenderer);

        PayloadHandlers = new() {
            { MessagePayload.Command, new((msg) => { if(TryGetPayload(msg.Payload<Command>(), out var payload)) OnCommand?.Invoke(payload); }) },
            { MessagePayload.Log, new((msg) => { if(TryGetPayload(msg.Payload<Log>(), out var payload)) OnLog?.Invoke(payload); }) },
            { MessagePayload.InitializeBrowser, new((msg) => { if(TryGetPayload(msg.Payload<InitializeBrowser>(), out var payload)) OnInitializeBrowser?.Invoke(payload); }) },
            { MessagePayload.HostInitializeState, new((msg) => { if(TryGetPayload(msg.Payload<HostInitializeState>(), out var payload)) OnHostInitializeState?.Invoke(payload); }) },
            { MessagePayload.TabInitializeState, new((msg) => { if(TryGetPayload(msg.Payload<TabInitializeState>(), out var payload)) OnTabInitializeState?.Invoke(payload); }) },
            { MessagePayload.CreateTab, new((msg) => { if(TryGetPayload(msg.Payload<CreateTab>(), out var payload)) OnCreateTab?.Invoke(payload); }) },
            { MessagePayload.DestroyTab, new((msg) => { if(TryGetPayload(msg.Payload<DestroyTab>(), out var payload)) OnDestroyTab?.Invoke(payload); }) },
            { MessagePayload.UpdateFrame, new((msg) => { if(TryGetPayload(msg.Payload<UpdateFrame>(), out var payload)) OnUpdateFrame?.Invoke(payload); }) },
            { MessagePayload.CursorChanged, new((msg) => { if(TryGetPayload(msg.Payload<CursorChanged>(), out var payload)) OnCursorChanged?.Invoke(payload); }) },
            { MessagePayload.NavigationStarting, new((msg) => { if(TryGetPayload(msg.Payload<NavigationStarting>(), out var payload)) OnNavigationStarting?.Invoke(payload); }) },
            { MessagePayload.HistoryChanged, new((msg) => { if(TryGetPayload(msg.Payload<HistoryChanged>(), out var payload)) OnHistoryChanged?.Invoke(payload); }) },
            { MessagePayload.TitleChanged, new((msg) => { if(TryGetPayload(msg.Payload<TitleChanged>(), out var payload)) OnTitleChanged?.Invoke(payload); }) },
            { MessagePayload.NavigationCompleted, new((msg) => { if(TryGetPayload(msg.Payload<NavigationCompleted>(), out var payload)) OnNavigationCompleted?.Invoke(payload); }) },
            { MessagePayload.NavigationCanceled, new((msg) => { if(TryGetPayload(msg.Payload<NavigationCanceled>(), out var payload)) OnNavigationCanceled?.Invoke(payload); }) },
            { MessagePayload.FavIconChanged, new((msg) => { if(TryGetPayload(msg.Payload<FavIconChanged>(), out var payload)) OnFavIconChanged?.Invoke(payload); }) },
            { MessagePayload.ExtensionOperation, new((msg) => { if(TryGetPayload(msg.Payload<ExtensionOperation>(), out var payload)) OnExtensionOperation?.Invoke(payload); }) },
            { MessagePayload.Navigate, new((msg) => { if(TryGetPayload(msg.Payload<Navigate>(), out var payload)) OnNavigate?.Invoke(payload); }) },
            { MessagePayload.Reload, new((msg) => { if(TryGetPayload(msg.Payload<Reload>(), out var payload)) OnReload?.Invoke(payload); }) },
            { MessagePayload.StopNavigation, new((msg) => { if(TryGetPayload(msg.Payload<StopNavigation>(), out var payload)) OnStopNavigation?.Invoke(payload); }) },
            { MessagePayload.Resize, new((msg) => { if(TryGetPayload(msg.Payload<Resize>(), out var payload)) OnResize?.Invoke(payload); }) },
            { MessagePayload.Reposition, new((msg) => { if(TryGetPayload(msg.Payload<Reposition>(), out var payload)) OnReposition?.Invoke(payload); }) },
            { MessagePayload.SetFocusedTab, new((msg) => { if(TryGetPayload(msg.Payload<SetFocusedTab>(), out var payload)) OnSetFocusedTab?.Invoke(payload); }) },
            { MessagePayload.SendMouseEvent, new((msg) => { if(TryGetPayload(msg.Payload<SendMouseEvent>(), out var payload)) OnSendMouseEvent?.Invoke(payload); }) },
            { MessagePayload.UpdateSpatialAudio, new((msg) => { if(TryGetPayload(msg.Payload<UpdateSpatialAudio>(), out var payload)) OnUpdateSpatialAudio?.Invoke(payload); }) },
            { MessagePayload.OpenDevTools, new((msg) => { if(TryGetPayload(msg.Payload<OpenDevTools>(), out var payload)) OnOpenDevTools?.Invoke(payload); }) },
            { MessagePayload.InstallExtension, new((msg) => { if(TryGetPayload(msg.Payload<InstallExtension>(), out var payload)) OnInstallExtension?.Invoke(payload); }) },
            { MessagePayload.UninstallExtension, new((msg) => { if(TryGetPayload(msg.Payload<UninstallExtension>(), out var payload)) OnUninstallExtension?.Invoke(payload); }) },
            { MessagePayload.EnableExtension, new((msg) => { if(TryGetPayload(msg.Payload<EnableExtension>(), out var payload)) OnEnableExtension?.Invoke(payload); }) },
            { MessagePayload.DisableExtension, new((msg) => { if(TryGetPayload(msg.Payload<DisableExtension>(), out var payload)) OnDisableExtension?.Invoke(payload); }) },
        };
    }

    public void Start() {
        if(_started) return;
        _started = true;

        _pollTask = Task.Run(PollLoop);
        _dispatchTask = Task.Run(DispatchLoop);
    }

    private async Task PollLoop() {
        try {
            while(!_cts.IsCancellationRequested) {
                var readAny = false;
                while(_inbound.TryRead(out var data)) {
                    readAny = true;
                    _queue.Enqueue(data);
                    _queueSignal.Release();
                }
                if(!readAny) {
                    await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                } else {
                    await Task.Yield();
                }
            }
        } catch(OperationCanceledException) { }
    }

    private async Task DispatchLoop() {
        try {
            while(!_cts.IsCancellationRequested) {
                await _queueSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                while(_queue.TryDequeue(out var data)) {
                    Dispatch(data);
                }
            }
        } catch(OperationCanceledException) { }
    }

    private void Dispatch(byte[] data) {
        var msg = IpcMessage.GetRootAsIpcMessage(new ByteBuffer(data));
        if(PayloadHandlers.TryGetValue(msg.PayloadType, out var handler))
            handler(msg);
    }

    private static bool TryGetPayload<T>(T? payload, out T value) where T : struct {
        if(payload.HasValue) {
            value = payload.Value;
            return true;
        }
        value = default;
        return false;
    }

    public void SendCommand(CommandType type) {
        var fbb = new FlatBufferBuilder(128);
        var payload = Command.CreateCommand(fbb, type);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.Command, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendLog(LogType type, string message) {
        var fbb = new FlatBufferBuilder(128);
        var payload = Log.CreateLog(fbb, type, fbb.CreateString(message));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.Log, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendInitializeBrowser(string pluginPath, uint gamePid, LUID adapterLuid) {
        var fbb = new FlatBufferBuilder(128);
        var payload = InitializeBrowser.CreateInitializeBrowser(fbb, fbb.CreateString(pluginPath), gamePid, adapterLuid.LowPart, adapterLuid.HighPart);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.InitializeBrowser, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendHostInitializeState(StateType type, string? message = null) {
        var fbb = new FlatBufferBuilder(128);
        var payload = HostInitializeState.CreateHostInitializeState(fbb, type, string.IsNullOrEmpty(message) ? default : fbb.CreateString(message));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.HostInitializeState, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendTabInitializeState(StateType type, string pixId, string? message = null) {
        var fbb = new FlatBufferBuilder(128);
        var payload = TabInitializeState.CreateTabInitializeState(fbb, type, fbb.CreateString(pixId), string.IsNullOrEmpty(message) ? default : fbb.CreateString(message));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.TabInitializeState, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendCreateTab(string pixId, bool gpuAcceleration, int x, int y, uint w, uint h, bool syncCookies, string[] extensions) {
        var fbb = new FlatBufferBuilder(128);

        var extOffsets = new StringOffset[extensions.Length];
        for(int i = 0; i < extensions.Length; i++) {
            extOffsets[i] = fbb.CreateString(extensions[i]);
        }
        var extensionsVector = CreateTab.CreateExtensionsVector(fbb, extOffsets);

        var payload = CreateTab.CreateCreateTab(fbb, fbb.CreateString(pixId), gpuAcceleration, x, y, w, h, syncCookies, extensionsVector);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.CreateTab, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendDestroyTab(string pixId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = DestroyTab.CreateDestroyTab(fbb, fbb.CreateString(pixId));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.DestroyTab, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendUpdateFrame(string pixId, long sharedTexture, uint w, uint h) {
        var fbb = new FlatBufferBuilder(128);
        var payload = UpdateFrame.CreateUpdateFrame(fbb, fbb.CreateString(pixId), sharedTexture, w, h);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.UpdateFrame, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendCursorChanged(uint cursorId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = CursorChanged.CreateCursorChanged(fbb, cursorId);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.CursorChanged, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendNavigationStarting(string pixId, string uri, bool userInitiated) {
        var fbb = new FlatBufferBuilder(128);
        var payload = NavigationStarting.CreateNavigationStarting(fbb, fbb.CreateString(pixId), fbb.CreateString(uri), userInitiated);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.NavigationStarting, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendHistoryChanged(string pixId, string uri) {
        var fbb = new FlatBufferBuilder(128);
        var payload = HistoryChanged.CreateHistoryChanged(fbb, fbb.CreateString(pixId), fbb.CreateString(uri));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.HistoryChanged, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendTitleChanged(string pixId, string title) {
        var fbb = new FlatBufferBuilder(128);
        var payload = TitleChanged.CreateTitleChanged(fbb, fbb.CreateString(pixId), fbb.CreateString(title));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.TitleChanged, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendNavigationCompleted(string pixId, uint statusCode) {
        var fbb = new FlatBufferBuilder(128);
        var payload = NavigationCompleted.CreateNavigationCompleted(fbb, fbb.CreateString(pixId), statusCode);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.NavigationCompleted, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendNavigationCanceled(string pixId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = NavigationCanceled.CreateNavigationCanceled(fbb, fbb.CreateString(pixId));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.NavigationCanceled, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendFavIconChanged(string pixId, byte[] data) {
        var fbb = new FlatBufferBuilder(128);
        var payload = FavIconChanged.CreateFavIconChanged(fbb, fbb.CreateString(pixId), FavIconChanged.CreateDataVector(fbb, data));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.FavIconChanged, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendExtensionOperation(ExtensionOp extensionOp, string extensionId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = ExtensionOperation.CreateExtensionOperation(fbb, extensionOp, fbb.CreateString(extensionId));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.ExtensionOperation, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendNavigate(string pixId, string uri) {
        var fbb = new FlatBufferBuilder(128);
        var payload = Navigate.CreateNavigate(fbb, fbb.CreateString(pixId), fbb.CreateString(uri));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.Navigate, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendReload(string pixId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = Reload.CreateReload(fbb, fbb.CreateString(pixId));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.Reload, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendStopNavigation(string pixId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = StopNavigation.CreateStopNavigation(fbb, fbb.CreateString(pixId));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.StopNavigation, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendResize(string pixId, int x, int y, uint w, uint h) {
        var fbb = new FlatBufferBuilder(128);
        var payload = Resize.CreateResize(fbb, fbb.CreateString(pixId), x, y, w, h);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.Resize, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }
    public void SendReposition(string pixId, int x, int y) {
        var fbb = new FlatBufferBuilder(128);
        var payload = Reposition.CreateReposition(fbb, fbb.CreateString(pixId), x, y);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.Reposition, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendSetFocusedTab(string pixId, bool byUserInput) {
        var fbb = new FlatBufferBuilder(128);
        var payload = SetFocusedTab.CreateSetFocusedTab(fbb, fbb.CreateString(pixId), byUserInput);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.SetFocusedTab, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendSendMouseEvent(string pixId, uint msg, long wParam, long lParam) {
        var fbb = new FlatBufferBuilder(128);
        var payload = SendMouseEvent.CreateSendMouseEvent(fbb, fbb.CreateString(pixId), msg, wParam, lParam);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.SendMouseEvent, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendUpdateSpatialAudio(string pixId, float left, float right) {
        var fbb = new FlatBufferBuilder(128);
        var payload = UpdateSpatialAudio.CreateUpdateSpatialAudio(fbb, fbb.CreateString(pixId), left, right);
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.UpdateSpatialAudio, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendOpenDevTools(string pixId) {
        var fbb = new FlatBufferBuilder(128);
        var payload = OpenDevTools.CreateOpenDevTools(fbb, fbb.CreateString(pixId));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.OpenDevTools, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void SendInstallExtension(string extensionId, string extensionName) {
        var fbb = new FlatBufferBuilder(128);
        var payload = InstallExtension.CreateInstallExtension(fbb, fbb.CreateString(extensionId), fbb.CreateString(extensionName));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.InstallExtension, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }
    public void SendUninstallExtension(string extensionId, string extensionName) {
        var fbb = new FlatBufferBuilder(128);
        var payload = UninstallExtension.CreateUninstallExtension(fbb, fbb.CreateString(extensionId), fbb.CreateString(extensionName));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.UninstallExtension, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }
    public void SendEnableExtension(string extensionId, string extensionName) {
        var fbb = new FlatBufferBuilder(128);
        var payload = EnableExtension.CreateEnableExtension(fbb, fbb.CreateString(extensionId), fbb.CreateString(extensionName));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.EnableExtension, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }
    public void SendDisableExtension(string extensionId, string extensionName) {
        var fbb = new FlatBufferBuilder(128);
        var payload = DisableExtension.CreateDisableExtension(fbb, fbb.CreateString(extensionId), fbb.CreateString(extensionName));
        IpcMessage.FinishIpcMessageBuffer(fbb, IpcMessage.CreateIpcMessage(fbb, MessagePayload.DisableExtension, payload.Value));
        SendRaw(fbb.SizedByteArray());
    }

    public void Send(Action<FlatBufferBuilder> build) {
        var fbb = new FlatBufferBuilder(256);
        build(fbb);
        SendRaw(fbb.SizedByteArray());
    }

    public void SendRaw(byte[] data) {
        _sendGate.Wait();
        try {
            _outbound.Write(data);
        } finally {
            _sendGate.Release();
        }
    }

    public void Dispose() {
        _cts.Cancel();
        try { _queueSignal.Release(); } catch { }
        try { _pollTask?.Wait(500); } catch { }
        try { _dispatchTask?.Wait(500); } catch { }
        _cts.Dispose();
        _queueSignal.Dispose();
        _sendGate.Dispose();
        _outbound.Dispose();
        _inbound.Dispose();
    }
}
