using System.Runtime.InteropServices;
using PyonPix.Ipc;
using PyonPix.Shared.Structs.Renderer;

namespace PyonPix.Mediator.Interop;

internal static class BrowserInterop {
    private const string DllName = "PyonPix.Browser.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnLogCallback(LogType logType, [MarshalAs(UnmanagedType.LPWStr)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnHostReadyCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnHostFailedCallback([MarshalAs(UnmanagedType.LPWStr)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnTabReadyCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnTabFailedCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, [MarshalAs(UnmanagedType.LPWStr)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnTabDestroyedCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnFrameReadyCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, nint sharedTexture, uint width, uint height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnCursorChangedCallback(uint cursorId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnNavigationStartingCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, [MarshalAs(UnmanagedType.LPWStr)] string uri, bool userInitiated);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnNavigationCompletedCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, uint statusCode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnNavigationCanceledCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnHistoryChangedCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, [MarshalAs(UnmanagedType.LPWStr)] string uri);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnTitleChangedCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, [MarshalAs(UnmanagedType.LPWStr)] string title);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnFavIconChangedCallback([MarshalAs(UnmanagedType.LPWStr)] string tabId, nint data, int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void OnExtensionOperationCallback(ExtensionOp opType, [MarshalAs(UnmanagedType.LPWStr)] string extensionId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void RegisterCallbacks(
        OnLogCallback logCb,
        OnHostReadyCallback OnHostReadyCallback,
        OnHostFailedCallback OnHostFailedCallback,
        OnTabReadyCallback OnTabReadyCallback,
        OnTabFailedCallback OnTabFailedCallback,
        OnTabDestroyedCallback OnTabDestroyedCallback,
        OnFrameReadyCallback OnFrameReadyCallback,
        OnCursorChangedCallback OnCursorChangedCallback,
        OnNavigationStartingCallback OnNavigationStartingCallback,
        OnNavigationCompletedCallback OnNavigationCompletedCallback,
        OnNavigationCanceledCallback OnNavigationCanceledCallback,
        OnHistoryChangedCallback OnHistoryChangedCallback,
        OnTitleChangedCallback OnTitleChangedCallback,
        OnFavIconChangedCallback OnFavIconChangedCallback,
        OnExtensionOperationCallback OnExtensionOperationCallback
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern bool Initialize([MarshalAs(UnmanagedType.LPWStr)] string pluginPath, uint gamePid, LUID adapterLuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void Heartbeat();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void Shutdown();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void CreateTab([MarshalAs(UnmanagedType.LPWStr)] string pixId, bool gpuAcceleration, int x, int y, uint w, uint h, bool syncCookies, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 7)] string[] installedExtensionIds, int installedExtensionCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void DestroyTab([MarshalAs(UnmanagedType.LPWStr)] string pixId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void SetFocusedTab([MarshalAs(UnmanagedType.LPWStr)] string pixId, bool byUserInput);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void Navigate([MarshalAs(UnmanagedType.LPWStr)] string pixId, [MarshalAs(UnmanagedType.LPWStr)] string url);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void Reload([MarshalAs(UnmanagedType.LPWStr)] string pixId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void StopNavigation([MarshalAs(UnmanagedType.LPWStr)] string pixId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void Resize([MarshalAs(UnmanagedType.LPWStr)] string pixId, int x, int y, uint w, uint h);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void Reposition([MarshalAs(UnmanagedType.LPWStr)] string pixId, int x, int y);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void LostFocus();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void SendMouseEvent([MarshalAs(UnmanagedType.LPWStr)] string pixId, uint msg, nint wParam, nint lParam);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void UpdateSpatialAudio([MarshalAs(UnmanagedType.LPWStr)] string pixId, float left, float right);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void OpenDevTools([MarshalAs(UnmanagedType.LPWStr)] string pixId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void InstallExtension([MarshalAs(UnmanagedType.LPWStr)] string extensionId, [MarshalAs(UnmanagedType.LPWStr)] string extensionName);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void UninstallExtension([MarshalAs(UnmanagedType.LPWStr)] string extensionId, [MarshalAs(UnmanagedType.LPWStr)] string extensionName);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void EnableExtension([MarshalAs(UnmanagedType.LPWStr)] string extensionId, [MarshalAs(UnmanagedType.LPWStr)] string extensionName);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void DisableExtension([MarshalAs(UnmanagedType.LPWStr)] string extensionId, [MarshalAs(UnmanagedType.LPWStr)] string extensionName);
}
