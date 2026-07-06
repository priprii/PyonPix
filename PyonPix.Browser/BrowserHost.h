#pragma once
#include "Globals.h"
#include "AudioSessionManager.h"
#include "WebView2/Include/WebView2.h"
#include <d3d11.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <windows.graphics.capture.interop.h>
#include <wrl.h>
#include <memory>
#include <string>
#include <unordered_map>
#include <mutex>
#include <vector>
#include <atomic>

#include <winrt/base.h>
#include <winrt/windows.ui.composition.desktop.h>
#include <winrt/windows.ui.composition.h>

namespace winrt {
    using namespace std::literals;
    using namespace Windows::System;
    using namespace Windows::Graphics;
    using namespace Windows::Graphics::Capture;
    using namespace Windows::Graphics::DirectX;
    using namespace Windows::Graphics::DirectX::Direct3D11;
    using namespace Windows::UI::Composition;
}
namespace wrl {
    using namespace Microsoft::WRL;
}
namespace rt {
    using namespace winrt::Windows::Foundation;
    using namespace ABI::Windows::Graphics::Capture;
}

enum class ExtensionOperation {
    Install,
    Remove,
    Enable,
    Disable
};
struct PendingExtensionOp {
    std::atomic<int> Remaining { 0 };
    ExtensionOperation Op;
    std::vector<std::wstring> Tabs;
};

class BrowserTab;

class BrowserHost {
public:
    BrowserHost();
    ~BrowserHost();

    bool Initialize();
    void Shutdown();
    bool IsDispatcherQueueShutdownComplete() const;

    bool CreateTab(const wchar_t* tabId, bool gpuAcceleration, int32_t x, int32_t y, uint32_t w, uint32_t h, bool syncCookies, const std::vector<std::wstring>& installedExtensionIds);
    void DestroyTab(const wchar_t* tabId);
    void NotifyTabReady(const std::wstring& tabId);

    BrowserTab* GetTab(const wchar_t* tabId);
    BrowserTab* GetFocusedTab();
    void SetFocusedHost();
    void SetFocusedTab(const std::wstring& tabId, bool byUserInput);
    void UnsetFocusedTab();
    void PassFocusedTab(const std::wstring& tabId);

    void PollTabsAndEmitFrames();

    void UpdateSpatialAudio(const std::wstring& tabId, float left, float right);

    void ImportCookiesFromHostToTab(const std::wstring& tabId, ICoreWebView2CookieManager* tabCookieManager);
    void ImportCookiesFromTabToHost(const std::wstring& tabId, ICoreWebView2CookieManager* tabCookieManager);

    void StartExtensionOperation(const std::wstring& extensionId, const std::wstring& extensionName, ExtensionOperation op);
    void NotifyExtensionOperationResult(const std::wstring& extensionId, const std::wstring& tabId, bool success);
    bool HasPendingExtensionOperation(const std::wstring& extensionId);

    void RegisterTabPendingInstall(const std::wstring& tabId);
    void NotifyTabExtensionInstallResult(const std::wstring& extensionId, const std::wstring& tabId, bool success);

    std::vector<std::wstring> GetPendingExtensions() const { return PendingExtensions; }

private:
    std::mutex LifecycleMutex;
    std::atomic_bool IsShuttingDown { false };
    mutable std::atomic_bool DispatcherQueueShutdownCompleted { false };
    winrt::Windows::Foundation::IAsyncAction DispatcherQueueShutdownAction { nullptr };

    winrt::DispatcherQueueController WinRTDispatcher { nullptr };

    wrl::ComPtr<ICoreWebView2Environment15> Environment;
    wrl::ComPtr<ICoreWebView2Controller4> Controller;
    wrl::ComPtr<ICoreWebView2_28> WebView;
    wrl::ComPtr<ICoreWebView2Settings9> WebViewSettings;
    wrl::ComPtr<ICoreWebView2Profile8> Profile;
    wrl::ComPtr<ICoreWebView2CookieManager> CookieManager;

    std::unordered_map<std::wstring, std::unique_ptr<BrowserTab>> Tabs;
    std::mutex TabsMutex;
    std::wstring PendingFocusTabId;
    std::wstring CurrentFocusedTabId;

    std::vector<std::wstring> PendingExtensions;
    std::mutex PendingExtensionsMutex;
    std::unordered_map<std::wstring, PendingExtensionOp> PendingExtensionOps;
    std::unordered_map<std::wstring, int> PendingTabInstalls;
    std::mutex PendingTabInstallsMutex;

    AudioSessionManager* AudioManager = nullptr;

    EventRegistrationToken WebViewProcessFailedEventToken {};

    bool CreateController();

    bool ShouldSyncCookies(const std::wstring& tabId);

    std::wstring BuildPersistentUDF() const;
    std::wstring BuildTabUDF(const std::wstring& tabId) const;

    std::wstring GetUDFRootPath() const { return Browser.PluginPath + L"\\Data\\Profiles"; }
    std::wstring GetUDFPath(const std::wstring& id) const { return GetUDFRootPath() + L"\\" + id; }
};
