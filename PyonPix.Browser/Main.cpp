#include "Globals.h"
#include "Main.h"
#include "BrowserTab.h"
#include <stdio.h>
#include <io.h>
#include <fcntl.h>
#include <iostream>
#include <queue>
#include <mutex>
#include <string>
#include <thread>

BrowserProps Browser {};
HANDLE RendererThread = nullptr;
DWORD RendererThreadId = 0;
HANDLE RendererShutdownEvent = nullptr;

LUID AdapterLuid = {};
IDXGIFactory1* DXGIFactory = nullptr;
IDXGIAdapter1* DXGIAdapter = nullptr;

std::mutex CommandMutex;
std::queue<std::function<void()>> CommandQueue;

BrowserHost* Host = nullptr;

std::wstring FocusedTabId;

BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam) {
    auto* data = reinterpret_cast<FindWindowByPidData*>(lParam);
    DWORD windowPid = 0;
    GetWindowThreadProcessId(hwnd, &windowPid);

    if(windowPid != data->Pid) return TRUE;
    if(!IsWindowVisible(hwnd)) return TRUE;
    if(GetWindow(hwnd, GW_OWNER) != nullptr) return TRUE;

    data->HWND = hwnd;
    return FALSE;
}

HWND FindMainWindow(DWORD pid) {
    FindWindowByPidData data { pid };
    EnumWindows(EnumWindowsProc, reinterpret_cast<LPARAM>(&data));
    return data.HWND;
}

bool IsGameAlive() {
    if(Browser.GamePid == 0) return false;
    HANDLE h = OpenProcess(SYNCHRONIZE, FALSE, Browser.GamePid);
    if(!h) return false;
    DWORD r = WaitForSingleObject(h, 0);
    CloseHandle(h);
    return r == WAIT_TIMEOUT;
}

void RestoreGameFocus() {
    if(!Browser.GameHwnd) return;
    DWORD gameThreadId = GetWindowThreadProcessId(Browser.GameHwnd, nullptr);
    DWORD currentThreadId = GetCurrentThreadId();

    BOOL attached = FALSE;
    if(gameThreadId != 0 && gameThreadId != currentThreadId) {
        attached = AttachThreadInput(currentThreadId, gameThreadId, TRUE);
    }

    SetFocus(nullptr);
    SetFocus(Browser.GameHwnd);

    if(attached) {
        AttachThreadInput(currentThreadId, gameThreadId, FALSE);
    }
}

void EnqueueCommand(std::function<void()> fn) {
    if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
    if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

    std::lock_guard<std::mutex> lock(CommandMutex);
    CommandQueue.push(std::move(fn));
}
void ProcessCommands() {
    if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
    if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

    std::queue<std::function<void()>> local;
    {
        std::lock_guard<std::mutex> lock(CommandMutex);
        std::swap(local, CommandQueue);
    }

    while(!Browser.ShutdownRequested.load(std::memory_order_acquire) && !local.empty()) {
        try {
            local.front()();
        } catch(...) { }
        local.pop();
    }
}

DWORD WINAPI RendererRoutine(void*) {
    bool hostInitialized = false;
    if(!OnHostFailed(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED), L"Thread Initialization Failed")) {
        winrt::init_apartment(winrt::apartment_type::single_threaded);

        LOGVERBOSE(L"Creating DXGIFactory");
        if(!OnHostFailed(CreateDXGIFactory1(IID_PPV_ARGS(&DXGIFactory)), L"DXGIFactory Creation Failed")) {
            for(UINT i = 0; DXGIFactory->EnumAdapters1(i, &DXGIAdapter) != DXGI_ERROR_NOT_FOUND; i++) {
                DXGI_ADAPTER_DESC1 desc {};
                DXGIAdapter->GetDesc1(&desc);
                if(memcmp(&desc.AdapterLuid, &AdapterLuid, sizeof(LUID)) == 0)
                    break;
                safe_release(DXGIAdapter);
            }

            LOGVERBOSE(L"Creating D3D11Device");
            if(!OnHostFailed(D3D11CreateDevice(DXGIAdapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &Browser.D3D11Device, nullptr, nullptr), L"D3D11Device Creation Failed")) {
                Host = new BrowserHost();
                Host->Initialize();
                hostInitialized = true;
            }
        }
    }

    MSG msg {};
    bool hostShutdownInitiated = false;
    while(hostInitialized) {
        while(PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) {
            if(msg.message == WM_QUIT) {
                Browser.ShutdownRequested.store(true, std::memory_order_release);
                continue;
            }
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }

        if(Browser.ShutdownRequested.load(std::memory_order_acquire) || !IsGameAlive() || GetTickCount64() - Browser.Heartbeat.load(std::memory_order_relaxed) > 3000) {
            Browser.IsRunning.store(false, std::memory_order_release);
            Browser.ShutdownRequested.store(true, std::memory_order_release);

            if(Host && !hostShutdownInitiated) {
                ProcessCommands();
                Host->Shutdown();
                hostShutdownInitiated = true;
            }

            if(!Host || Host->IsDispatcherQueueShutdownComplete()) break;

            Sleep(1);
            continue;
        }

        ProcessCommands();

        if(Host) Host->PollTabsAndEmitFrames();

        Sleep(16);
    }

    if(Host) {
        delete Host;
        Host = nullptr;
    }

    safe_release(Browser.D3D11Device);
    safe_release(DXGIAdapter);
    safe_release(DXGIFactory);

    Browser.OnLog = nullptr;
    Browser.OnHostReady = nullptr;
    Browser.OnHostFailed = nullptr;
    Browser.OnTabReady = nullptr;
    Browser.OnTabFailed = nullptr;
    Browser.OnTabDestroyed = nullptr;
    Browser.OnFrameReady = nullptr;
    Browser.OnCursorChanged = nullptr;
    Browser.OnNavigationStarting = nullptr;
    Browser.OnNavigationCompleted = nullptr;
    Browser.OnNavigationCanceled = nullptr;
    Browser.OnHistoryChanged = nullptr;
    Browser.OnTitleChanged = nullptr;
    Browser.OnFavIconChanged = nullptr;
    Browser.OnExtensionOperation = nullptr;
    memset(&Browser, 0, sizeof(Browser));

    winrt::uninit_apartment();
    CoUninitialize();

    SetEvent(RendererShutdownEvent);
    return 0;
}

extern "C" {
    void RegisterCallbacks(
        OnLogCallback OnLogCallback,
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
    ) {
        Browser.OnLog = OnLogCallback;
        Browser.OnHostReady = OnHostReadyCallback;
        Browser.OnHostFailed = OnHostFailedCallback;
        Browser.OnTabReady = OnTabReadyCallback;
        Browser.OnTabFailed = OnTabFailedCallback;
        Browser.OnTabDestroyed = OnTabDestroyedCallback;
        Browser.OnFrameReady = OnFrameReadyCallback;
        Browser.OnCursorChanged = OnCursorChangedCallback;
        Browser.OnNavigationStarting = OnNavigationStartingCallback;
        Browser.OnNavigationCompleted = OnNavigationCompletedCallback;
        Browser.OnNavigationCanceled = OnNavigationCanceledCallback;
        Browser.OnHistoryChanged = OnHistoryChangedCallback;
        Browser.OnTitleChanged = OnTitleChangedCallback;
        Browser.OnFavIconChanged = OnFavIconChangedCallback;
        Browser.OnExtensionOperation = OnExtensionOperationCallback;
    }

    bool Initialize(const wchar_t* pluginPath, uint32_t gamePid, LUID adapterLuid) {
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return false;
        bool expected = false;
        if(!Browser.IsRunning.compare_exchange_strong(expected, true)) return false;

        LOGVERBOSE(L"Initializing");

        Browser.PluginPath = pluginPath;
        Browser.GamePid = gamePid;
        Browser.GameHwnd = FindMainWindow(gamePid);
        AdapterLuid = adapterLuid;

        Browser.Heartbeat.store(GetTickCount64(), std::memory_order_relaxed);
        RendererShutdownEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);
        RendererThread = CreateThread(nullptr, 0, RendererRoutine, nullptr, 0, &RendererThreadId);

        return RendererThread != nullptr;
    }

    void Heartbeat() {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;
        Browser.Heartbeat.store(GetTickCount64(), std::memory_order_relaxed);
    }

    void Shutdown() {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        bool expected = false;
        if(!Browser.ShutdownRequested.compare_exchange_strong(expected, true)) return;
        Browser.IsRunning.store(false, std::memory_order_release);

        PostThreadMessage(RendererThreadId, WM_APP + 100, 0, 0);
    }

    void CreateTab(const wchar_t* tabId, bool gpuAcceleration, int32_t x, int32_t y, uint32_t w, uint32_t h, bool syncCookies, const wchar_t** installedExtensionIds, int32_t installedExtensionCount) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::vector<std::wstring> extIds;
        if(installedExtensionIds && installedExtensionCount > 0) {
            extIds.reserve(installedExtensionCount);
            for(int i = 0; i < installedExtensionCount; i++) {
                const wchar_t* s = installedExtensionIds[i];
                if(s && s[0] != L'\0') extIds.emplace_back(s);
            }
        }

        std::wstring key(tabId);
        EnqueueCommand([=]() mutable {
            if(!Host) return;
            Host->CreateTab(key.c_str(), gpuAcceleration, x, y, w, h, syncCookies, extIds);
        });
    }

    void DestroyTab(const wchar_t* tabId) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            Host->DestroyTab(key.c_str());
        });
    }

    void SetFocusedTab(const wchar_t* tabId, bool byUserInput) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key = tabId ? std::wstring(tabId) : std::wstring();
        EnqueueCommand([=]() {
            if(!Host) return;
            Host->SetFocusedTab(key, byUserInput);
            if(byUserInput) {
                RestoreGameFocus();
            }
        });
    }

    void LostFocus() {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;
        EnqueueCommand([=]() {
            RestoreGameFocus();
        });
    }

    void Navigate(const wchar_t* tabId, const wchar_t* url) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->Navigate(url);
        });
    }

    void Reload(const wchar_t* tabId) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->Reload();
        });
    }

    void StopNavigation(const wchar_t* tabId) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->StopNavigation();
        });
    }

    void Resize(const wchar_t* tabId, int32_t x, int32_t y, uint32_t w, uint32_t h) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->UpdateBounds(x, y, w, h);
        });
    }

    void Reposition(const wchar_t* tabId, int32_t x, int32_t y) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=] () {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->UpdateBounds(x, y);
        });
    }

    void SendMouseEvent(const wchar_t* tabId, uint32_t msg, WPARAM w, LPARAM l) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->MouseEvent(msg, w, l);
        });
    }

    void UpdateSpatialAudio(const wchar_t* tabId, float left, float right) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            Host->UpdateSpatialAudio(key.c_str(), left, right);
        });
    }

    void OpenDevTools(const wchar_t* tabId) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;

        std::wstring key(tabId);
        EnqueueCommand([=]() {
            if(!Host) return;
            BrowserTab* t = Host->GetTab(key.c_str());
            if(t) t->OpenDevTools();
        });
    }

    void InstallExtension(const wchar_t* extensionId, const wchar_t* extensionName) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;
        if(!extensionId || !extensionName) return;

        std::wstring id(extensionId);
        std::wstring name(extensionName);
        EnqueueCommand([id = std::move(id), name = std::move(name)]() mutable {
            if(!Host) return;
            Host->StartExtensionOperation(id, name, ExtensionOperation::Install);
        });
    }
    void UninstallExtension(const wchar_t* extensionId, const wchar_t* extensionName) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;
        if(!extensionId || !extensionName) return;

        std::wstring id(extensionId);
        std::wstring name(extensionName);
        EnqueueCommand([id = std::move(id), name = std::move(name)]() mutable {
            if(!Host) return;
            Host->StartExtensionOperation(id, name, ExtensionOperation::Remove);
        });
    }
    void EnableExtension(const wchar_t* extensionId, const wchar_t* extensionName) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;
        if(!extensionId || !extensionName) return;

        std::wstring id(extensionId);
        std::wstring name(extensionName);
        EnqueueCommand([id = std::move(id), name = std::move(name)]() mutable {
            if(!Host) return;
            Host->StartExtensionOperation(id, name, ExtensionOperation::Enable);
        });
    }
    void DisableExtension(const wchar_t* extensionId, const wchar_t* extensionName) {
        if(!Browser.IsRunning.load(std::memory_order_acquire)) return;
        if(Browser.ShutdownRequested.load(std::memory_order_acquire)) return;
        if(!extensionId || !extensionName) return;

        std::wstring id(extensionId);
        std::wstring name(extensionName);
        EnqueueCommand([id = std::move(id), name = std::move(name)]() mutable {
            if(!Host) return;
            Host->StartExtensionOperation(id, name, ExtensionOperation::Disable);
        });
    }
}
