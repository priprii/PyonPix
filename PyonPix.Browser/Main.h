#pragma once
#include <stdint.h>
#include <windows.h>
#include "Globals.h"
#include "BrowserHost.h"
#include <Commctrl.h>
#pragma comment(lib, "Comctl32")
#include <dwmapi.h>
#pragma comment(lib, "dwmapi")
#include <uxtheme.h>
#pragma comment(lib, "uxtheme")
#include <winrt/base.h>
#include <d3d11.h>
#include <dxgi.h>
#include <atomic>
#include <queue>
#include <mutex>
#include <functional>
#pragma comment(lib, "d3d11")
#pragma comment(lib, "windowsapp")
#pragma comment(lib, "webview2/x64/WebView2LoaderStatic.lib")

struct FindWindowByPidData {
    DWORD Pid;
    HWND HWND = nullptr;
};

extern HANDLE RendererThread;
extern DWORD RendererThreadId;
extern HANDLE RendererShutdownEvent;
extern std::mutex CommandMutex;
extern std::queue<std::function<void()>> CommandQueue;

extern BrowserHost* Host;

BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam);
HWND FindMainWindow(DWORD pid);
bool IsGameAlive();
void RestoreGameFocus();
void EnqueueCommand(std::function<void()> fn);
void ProcessCommands();

DWORD WINAPI RendererRoutine(void*);

extern "C" {
    __declspec(dllexport)
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
        );

    __declspec(dllexport)
        bool Initialize(const wchar_t* pluginPath, uint32_t gamePid, LUID adapterLuid);

    __declspec(dllexport)
        void Heartbeat();

    __declspec(dllexport)
        void Shutdown();

    __declspec(dllexport)
        void CreateTab(const wchar_t* tabId, bool gpuAcceleration, int32_t x, int32_t y, uint32_t w, uint32_t h, bool syncCookies, const wchar_t** installedExtensionIds, int32_t installedExtensionCount);

    __declspec(dllexport)
        void DestroyTab(const wchar_t* tabId);

    __declspec(dllexport)
        void SetFocusedTab(const wchar_t* tabId, bool byUserInput);

    __declspec(dllexport)
        void Navigate(const wchar_t* tabId, const wchar_t* url);

    __declspec(dllexport)
        void Reload(const wchar_t* tabId);

    __declspec(dllexport)
        void StopNavigation(const wchar_t* tabId);

    __declspec(dllexport)
        void Resize(const wchar_t* tabId, int32_t x, int32_t y, uint32_t w, uint32_t h);
    __declspec(dllexport)
        void Reposition(const wchar_t* tabId, int32_t x, int32_t y);

    __declspec(dllexport)
        void LostFocus();

    __declspec(dllexport)
        void SendMouseEvent(const wchar_t* tabId, uint32_t msg, WPARAM wParam, LPARAM lParam);

    __declspec(dllexport)
        void UpdateSpatialAudio(const wchar_t* tabId, float left, float right);

    __declspec(dllexport)
        void OpenDevTools(const wchar_t* tabId);

    __declspec(dllexport)
        void InstallExtension(const wchar_t* extensionId, const wchar_t* extensionName);
    __declspec(dllexport)
        void UninstallExtension(const wchar_t* extensionId, const wchar_t* extensionName);
    __declspec(dllexport)
        void EnableExtension(const wchar_t* extensionId, const wchar_t* extensionName);
    __declspec(dllexport)
        void DisableExtension(const wchar_t* extensionId, const wchar_t* extensionName);
}
