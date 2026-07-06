#pragma once
#include "Globals.h"
#include "WebView2/Include/WebView2.h"
#include <wrl.h>
#include <winrt/Base.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <Windows.Graphics.Capture.Interop.h>
#include <d3d11.h>
#include <atomic>
#include <string>
#include <mutex>
#include <functional>

namespace winrt {
    using namespace std::literals;
    using namespace Windows::System;
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

struct WebErrorInfo {
    uint32_t HttpCode;
    std::wstring StatusCode;
    std::wstring Title;
    std::wstring Header;
    std::wstring Description;
};

class BrowserHost;

class BrowserTab {
public:
    BrowserTab(const std::wstring& tabId, BrowserHost* host);
    ~BrowserTab();

    bool SyncCookies = true;

    bool Initialize(const std::wstring& userDataFolder, bool gpuAcceleration, int32_t x, int32_t y, uint32_t w, uint32_t h);
    void Shutdown();

    void Navigate(const wchar_t* uri);
    void Reload();
    void StopNavigation();
    void OpenDevTools();

    void UpdateBounds(int32_t x, int32_t y, uint32_t w = 0, uint32_t h = 0);
    void MouseEvent(UINT message, WPARAM wParam, LPARAM lParam);

    void InstallPendingExtensions();
    bool InstallPendingExtension(const std::wstring& extensionId, const std::wstring& extensionName);
    bool InstallExtension(const std::wstring& extensionId, const std::wstring& extensionName);
    bool UninstallExtension(const std::wstring& extensionId, const std::wstring& extensionName);
    bool ToggleExtension(const std::wstring& extensionId, const std::wstring& extensionName, bool newState);

    void MoveFocusProgrammatic();

    bool HasFrame() const { return HasNewFrame.load(std::memory_order_acquire); }
    void ConsumeFrame() { HasNewFrame.store(false, std::memory_order_release); }
    HANDLE GetSharedHandle() const { return SharedHandle; }
    uint32_t GetFrameWidth() const { return FrameWidth; }
    uint32_t GetFrameHeight() const { return FrameHeight; }
    const wchar_t* GetTabId() const { return TabId.c_str(); }
    DWORD GetBrowserProcessId() const { return BrowserProcessId; }

private:
    std::wstring TabId;
    BrowserHost* Host;
    std::wstring UserDataFolder;

    std::mutex LifecycleMutex;
    bool IsShuttingDown = false;

    int32_t RenderX = 0;
    int32_t RenderY = 0;
    uint32_t RenderWidth = 0;
    uint32_t RenderHeight = 0;

    std::atomic<bool> HasNewFrame;
    uint32_t FrameWidth = 0;
    uint32_t FrameHeight = 0;

    ID3D11Texture2D* SharedTexture = nullptr;
    HANDLE SharedHandle = nullptr;

    DWORD BrowserProcessId = 0;
    wrl::ComPtr<ICoreWebView2Environment15> Environment;
    wrl::ComPtr<ICoreWebView2CompositionController5> CompositionController;
    wrl::ComPtr<ICoreWebView2Controller4> Controller;
    wrl::ComPtr<ICoreWebView2_28> WebView;
    wrl::ComPtr<ICoreWebView2Settings9> WebViewSettings;
    wrl::ComPtr<ICoreWebView2Profile8> Profile;
    wrl::ComPtr<ICoreWebView2CookieManager> CookieManager;

    winrt::Direct3D11CaptureFramePool WinRTFramePool { nullptr };
    winrt::GraphicsCaptureSession WinRTCaptureSession { nullptr };
    winrt::GraphicsCaptureItem WinRTCaptureItem { nullptr };
    winrt::IDirect3DDevice WinRTDevice { nullptr };
    winrt::DirectXPixelFormat WinRTPixelFormat;

    bool TrackingMouse = false;
    COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS ButtonState = COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE;
    POINT MousePosition = { 0,0 };

    EventRegistrationToken WebViewProcessFailedEventToken {};
    //EventRegistrationToken WebViewWebResourceRequestedEventToken {};
    EventRegistrationToken WebViewNavigationStartingEventToken {};
    EventRegistrationToken WebViewNavigationCompletedEventToken {};
    EventRegistrationToken WebViewHistoryChangedEventToken {};
    EventRegistrationToken WebViewDocumentTitleChangedEventToken {};
    EventRegistrationToken WebViewCursorChangedEventToken {};
    EventRegistrationToken WebViewPermissionRequestedEventToken {};
    EventRegistrationToken WebViewWebMessageReceivedEventToken {};
    EventRegistrationToken WebViewFaviconChangedEventToken {};
    //EventRegistrationToken WebViewFrameCreatedEventToken {};

    bool CreateController(int32_t x, int32_t y, uint32_t w, uint32_t h);

    bool CreateSharedTexture(uint32_t width, uint32_t height);
    void ReleaseSharedTexture();
    void OnFrameArrived(winrt::Direct3D11CaptureFramePool const& sender, winrt::Windows::Foundation::IInspectable const& args);

    std::wstring NavigatedURI;
    WebErrorInfo GetWebErrorInfoFromStatus(COREWEBVIEW2_WEB_ERROR_STATUS status, BOOL isSuccess);
    WebErrorInfo GetWebErrorInfoFromHttpCode(uint32_t httpCode);
    std::wstring BuildErrorPage(WebErrorInfo error, LPCWSTR uri);
    std::wstring BuildBlankPage();
    std::wstring BuildStarryPage();

    std::wstring GetUnpackedExtensionRootPath() const { return Browser.PluginPath + L"\\Data\\Extensions"; }
    std::wstring GetUnpackedExtensionPath(const std::wstring& id) const { return GetUnpackedExtensionRootPath() + L"\\" + id; }

    BrowserTab(const BrowserTab&) = delete;
    BrowserTab& operator=(const BrowserTab&) = delete;
};