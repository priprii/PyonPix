#pragma once
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <functional>
#include <atomic>
#include <string>
#include <cstdarg>
#include <vector>
#include <cwchar>
#include <queue>
#include <mutex>

#define interface struct
#define safe_callback(cb, ...) if(!Browser.ShutdownRequested.load(std::memory_order_acquire) && cb) cb(__VA_ARGS__)
#define safe_release(x) if(x) { x->Release(); x = nullptr; }

#define LOGVERBOSE(fmt, ...) LogFormatted(0, fmt, __VA_ARGS__)
#define LOGINFO(fmt, ...) LogFormatted(1, fmt, __VA_ARGS__)
#define LOGWARN(fmt, ...) LogFormatted(2, fmt, __VA_ARGS__)
#define LOGERROR(fmt, ...) LogFormatted(3, fmt, __VA_ARGS__)

typedef void (*OnLogCallback)(byte logType, const wchar_t* message);
typedef void (*OnHostReadyCallback)();
typedef void (*OnHostFailedCallback)(const wchar_t* message);
typedef void (*OnTabReadyCallback)(const wchar_t* tabId);
typedef void (*OnTabFailedCallback)(const wchar_t* tabId, const wchar_t* message);
typedef void (*OnTabDestroyedCallback)(const wchar_t* tabId);
typedef void (*OnFrameReadyCallback)(const wchar_t* tabId, HANDLE sharedTexture, uint32_t width, uint32_t height);
typedef void (*OnCursorChangedCallback)(uint32_t cursorId);
typedef void (*OnNavigationStartingCallback)(const wchar_t* tabId, const wchar_t* uri, bool userInitiated);
typedef void (*OnNavigationCompletedCallback)(const wchar_t* tabId, uint32_t statusCode);
typedef void (*OnNavigationCanceledCallback)(const wchar_t* tabId);
typedef void (*OnHistoryChangedCallback)(const wchar_t* tabId, const wchar_t* uri);
typedef void (*OnTitleChangedCallback)(const wchar_t* tabId, const wchar_t* title);
typedef void (*OnFavIconChangedCallback)(const wchar_t* tabId, const uint8_t* data, size_t length);
typedef void (*OnWebMessageReceivedCallback)(const wchar_t* tabId, const wchar_t* json);
typedef void (*OnExtensionOperationCallback)(byte opType, const wchar_t* extensionId);

extern std::mutex CommandMutex;
extern std::queue<std::function<void()>> CommandQueue;
void EnqueueCommand(std::function<void()> fn);
void ProcessCommands();

static struct BrowserProps {
    std::atomic<uint64_t> Heartbeat;
    std::atomic_bool ShutdownRequested { false };
    std::atomic<bool> IsRunning { false };
    std::atomic<bool> CancelNavigation = { false };

    std::wstring PluginPath;
    HWND GameHwnd = nullptr;
    DWORD GamePid = 0;
    ID3D11Device* D3D11Device = nullptr;

    OnLogCallback OnLog = nullptr;
    OnHostReadyCallback OnHostReady = nullptr;
    OnHostFailedCallback OnHostFailed = nullptr;
    OnTabReadyCallback OnTabReady = nullptr;
    OnTabFailedCallback OnTabFailed = nullptr;
    OnTabDestroyedCallback OnTabDestroyed = nullptr;
    OnFrameReadyCallback OnFrameReady = nullptr;
    OnCursorChangedCallback OnCursorChanged = nullptr;
    OnNavigationStartingCallback OnNavigationStarting = nullptr;
    OnNavigationCompletedCallback OnNavigationCompleted = nullptr;
    OnNavigationCanceledCallback OnNavigationCanceled = nullptr;
    OnHistoryChangedCallback OnHistoryChanged = nullptr;
    OnTitleChangedCallback OnTitleChanged = nullptr;
    OnFavIconChangedCallback OnFavIconChanged = nullptr;
    OnWebMessageReceivedCallback OnWebMessageReceived = nullptr;
    OnExtensionOperationCallback OnExtensionOperation = nullptr;
};

extern BrowserProps Browser;

inline bool OnHostFailed(HRESULT hr, const wchar_t* msg) {
    if(FAILED(hr)) {
        safe_callback(Browser.OnHostFailed, msg);
        return true;
    }
    return false;
}

inline bool OnTabFailed(HRESULT hr, const wchar_t* tabId, const wchar_t* msg) {
    if(FAILED(hr)) {
        safe_callback(Browser.OnTabFailed, tabId, msg);
        return true;
    }
    return false;
}

inline void LogFormatted(int level, const wchar_t* fmt, ...) {
    if(!Browser.OnLog || !fmt) return;

    va_list args;
    va_start(args, fmt);

    int size = _vscwprintf(fmt, args);
    if(size <= 0) { va_end(args); return; }

    std::vector<wchar_t> buffer(size + 1);
    vswprintf_s(buffer.data(), buffer.size(), fmt, args);

    va_end(args);

    Browser.OnLog(level, buffer.data());
}
