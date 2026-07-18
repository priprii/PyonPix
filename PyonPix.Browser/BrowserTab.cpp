#pragma once
#include "BrowserTab.h"
#include "BrowserHost.h"
#include "Globals.h"
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.UI.Composition.h>
#include <WebView2EnvironmentOptions.h>
#include <filesystem>

namespace fs = std::filesystem;

BrowserTab::BrowserTab(const std::wstring& tabId, BrowserHost* host)
    : TabId(tabId), Host(host), IsShuttingDown(false), HasNewFrame(false), FrameWidth(0), FrameHeight(0),
    SharedTexture(nullptr), SharedHandle(nullptr), TrackingMouse(false), ButtonState(COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE) {
}

BrowserTab::~BrowserTab() {
    Shutdown();
}

bool BrowserTab::Initialize(const std::wstring& userDataFolder, bool gpuAcceleration, int32_t x, int32_t y, uint32_t w, uint32_t h) {
    if(userDataFolder.empty() || !Browser.D3D11Device) return false;
    UserDataFolder = userDataFolder;

    std::wstring args =
        L"--enable-features=msWebView2EnableDrm "
        L"--disable-blink-features=AutomationControlled "
        L"--autoplay-policy=no-user-gesture-required "
        L"--disable-gpu-sandbox "
        L"--disable_vp_auto_hdr ";
    if(!gpuAcceleration) args += L" --disable-gpu";

    wrl::ComPtr<ICoreWebView2EnvironmentOptions> envOptions;
    if(OnTabFailed(MakeAndInitialize<CoreWebView2EnvironmentOptions>(&envOptions), TabId.c_str(), L"EnvironmentOptions Creation Failed")) return false;
    envOptions->put_AllowSingleSignOnUsingOSPrimaryAccount(TRUE);
    envOptions->put_AdditionalBrowserArguments(args.c_str());

    wrl::ComPtr<ICoreWebView2EnvironmentOptions3> options3;
    if(SUCCEEDED(envOptions.As(&options3))) options3->put_IsCustomCrashReportingEnabled(TRUE);
    wrl::ComPtr<ICoreWebView2EnvironmentOptions5> options5;
    if(SUCCEEDED(envOptions.As(&options5))) options5->put_EnableTrackingPrevention(TRUE);
    wrl::ComPtr<ICoreWebView2EnvironmentOptions6> options6;
    if(SUCCEEDED(envOptions.As(&options6))) options6->put_AreBrowserExtensionsEnabled(TRUE);
    wrl::ComPtr<ICoreWebView2EnvironmentOptions8> options8;
    if(SUCCEEDED(envOptions.As(&options8))) options8->put_ScrollBarStyle(COREWEBVIEW2_SCROLLBAR_STYLE_FLUENT_OVERLAY);

    HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, UserDataFolder.c_str(), envOptions.Get(), wrl::Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>([this, x, y, w, h](HRESULT result, ICoreWebView2Environment* env) -> HRESULT {
        if(IsShuttingDown) return E_FAIL;
        
        if(OnTabFailed(!env ? E_FAIL : FAILED(result), TabId.c_str(), L"Environment Creation Failed")) return result;

        wrl::ComPtr<ICoreWebView2Environment> baseEnv(env);
        if(OnTabFailed(baseEnv.As(&Environment), TabId.c_str(), L"Environment QI Failed")) return E_FAIL;

        return CreateController(x, y, w, h) ? S_OK : E_FAIL;
    }).Get());

    return SUCCEEDED(hr);
}

bool BrowserTab::CreateController(int32_t x, int32_t y, uint32_t w, uint32_t h) {
    HRESULT hr = Environment->CreateCoreWebView2CompositionController(Browser.GameHwnd, wrl::Callback<ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler>([this, x, y, w, h](HRESULT result, ICoreWebView2CompositionController* comp) -> HRESULT {
        if(IsShuttingDown) return E_FAIL;
        
        if(OnTabFailed(!comp ? E_FAIL : FAILED(result), TabId.c_str(), L"CompositionController Failed")) return result;

        wrl::ComPtr<ICoreWebView2CompositionController> baseComp;
        if(OnTabFailed(comp->QueryInterface(IID_PPV_ARGS(baseComp.GetAddressOf())), TabId.c_str(), L"CompositionController QI Failed")) return E_FAIL;
        baseComp.As(&CompositionController);

        wrl::ComPtr<ICoreWebView2Controller> baseCtrl;
        if(OnTabFailed(comp->QueryInterface(IID_PPV_ARGS(baseCtrl.GetAddressOf())), TabId.c_str(), L"Controller QI Failed")) return E_FAIL;
        baseCtrl.As(&Controller);

        wrl::ComPtr<ICoreWebView2> baseWebView;
        if(OnTabFailed(Controller->get_CoreWebView2(baseWebView.GetAddressOf()), TabId.c_str(), L"WebView QI Failed")) return E_FAIL;
        baseWebView.As(&WebView);

        wrl::ComPtr<ICoreWebView2Settings> baseSettings;
        if(OnTabFailed(WebView->get_Settings(baseSettings.GetAddressOf()), TabId.c_str(), L"WebView Settings Failed")) return E_FAIL;
        baseSettings.As(&WebViewSettings);

        wrl::ComPtr<ICoreWebView2Profile> baseProfile;
        if(OnTabFailed(WebView->get_Profile(baseProfile.GetAddressOf()), TabId.c_str(), L"WebView Profile Failed")) return E_FAIL;
        baseProfile.As(&Profile);

        wrl::ComPtr<ICoreWebView2CookieManager> cookieManager;
        if(OnTabFailed(Profile->get_CookieManager(cookieManager.GetAddressOf()), TabId.c_str(), L"CookieManager Failed")) return E_FAIL;
        CookieManager = cookieManager;

        Controller->put_ShouldDetectMonitorScaleChanges(FALSE);
        Controller->put_DefaultBackgroundColor({ 0, 0, 0, 0 });
        Controller->put_BoundsMode(COREWEBVIEW2_BOUNDS_MODE_USE_RAW_PIXELS);
        Controller->put_RasterizationScale(1.0);
        Controller->put_IsVisible(TRUE);

        WebViewSettings->put_IsPasswordAutosaveEnabled(TRUE);
        WebViewSettings->put_AreHostObjectsAllowed(TRUE);
        WebViewSettings->put_IsScriptEnabled(TRUE);
        WebViewSettings->put_IsWebMessageEnabled(TRUE);
        WebViewSettings->put_IsStatusBarEnabled(FALSE);
        WebViewSettings->put_IsBuiltInErrorPageEnabled(FALSE);
        WebViewSettings->put_AreDefaultContextMenusEnabled(TRUE);
        WebViewSettings->put_AreDefaultScriptDialogsEnabled(TRUE);
        WebViewSettings->put_AreDevToolsEnabled(TRUE);
        WebViewSettings->put_IsGeneralAutofillEnabled(TRUE);
        WebViewSettings->put_AreBrowserAcceleratorKeysEnabled(FALSE);

        WebView->add_ProcessFailed(wrl::Callback<ICoreWebView2ProcessFailedEventHandler>([this](ICoreWebView2*, ICoreWebView2ProcessFailedEventArgs* args) -> HRESULT {
            if(IsShuttingDown) return S_OK;

            std::wstring failKind = L"UNKNOWN";
            std::wstring failReason = L"UNKNOWN";
            COREWEBVIEW2_PROCESS_FAILED_KIND kind;
            if(SUCCEEDED(args->get_ProcessFailedKind(&kind))) {
                switch(kind) {
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_BROWSER_PROCESS_EXITED: failKind = L"BROWSER_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_RENDER_PROCESS_EXITED: failKind = L"RENDER_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_RENDER_PROCESS_UNRESPONSIVE: failKind = L"RENDER_PROCESS_UNRESPONSIVE"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_FRAME_RENDER_PROCESS_EXITED: failKind = L"FRAME_RENDER_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_UTILITY_PROCESS_EXITED: failKind = L"UTILITY_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_SANDBOX_HELPER_PROCESS_EXITED: failKind = L"SANDBOX_HELPER_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_GPU_PROCESS_EXITED: failKind = L"GPU_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_PPAPI_PLUGIN_PROCESS_EXITED: failKind = L"PPAPI_PLUGIN_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_PPAPI_BROKER_PROCESS_EXITED: failKind = L"PPAPI_BROKER_PROCESS_EXITED"; break;
                    case COREWEBVIEW2_PROCESS_FAILED_KIND_UNKNOWN_PROCESS_EXITED: failKind = L"UNKNOWN_PROCESS_EXITED"; break;
                }
            }
            wrl::ComPtr<ICoreWebView2ProcessFailedEventArgs3> args3;
            if(SUCCEEDED(args->QueryInterface(IID_PPV_ARGS(&args3))) && args3) {
                COREWEBVIEW2_PROCESS_FAILED_REASON reason;
                if(SUCCEEDED(args3->get_Reason(&reason))) {
                    switch(reason) {
                        case COREWEBVIEW2_PROCESS_FAILED_REASON_UNEXPECTED: failReason = L"UNEXPECTED"; break;
                        case COREWEBVIEW2_PROCESS_FAILED_REASON_UNRESPONSIVE: failReason = L"UNRESPONSIVE"; break;
                        case COREWEBVIEW2_PROCESS_FAILED_REASON_TERMINATED: failReason = L"TERMINATED"; break;
                        case COREWEBVIEW2_PROCESS_FAILED_REASON_CRASHED: failReason = L"CRASHED"; break;
                        case COREWEBVIEW2_PROCESS_FAILED_REASON_LAUNCH_FAILED: failReason = L"LAUNCH_FAILED"; break;
                        case COREWEBVIEW2_PROCESS_FAILED_REASON_OUT_OF_MEMORY: failReason = L"OUT_OF_MEMORY"; break;
                    }
                }
            }
            std::wstringstream message;
            message << failKind << L" " << failReason;
            safe_callback(Browser.OnTabFailed, TabId.c_str(), message.str().c_str());
            return S_OK;
        }).Get(), &WebViewProcessFailedEventToken);

        //WebView->AddWebResourceRequestedFilter(L"pix://*", COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
        //WebView->add_WebResourceRequested(wrl::Callback<ICoreWebView2WebResourceRequestedEventHandler>([this](ICoreWebView2* sender, ICoreWebView2WebResourceRequestedEventArgs* args) -> HRESULT {
        //    return S_OK;
        //}).Get(), &WebViewWebResourceRequestedEventToken);

        WebView->add_NavigationStarting(wrl::Callback<ICoreWebView2NavigationStartingEventHandler>([this](ICoreWebView2* sender, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            BOOL cancel;
            args->get_Cancel(&cancel);
            if(cancel) {
                safe_callback(Browser.OnNavigationCanceled, TabId.c_str());
            } else {
                if(Host && CookieManager) {
                    Host->ImportCookiesFromHostToTab(TabId, CookieManager.Get());
                }

                BOOL userInit;
                args->get_IsUserInitiated(&userInit);
                LPWSTR uri = nullptr;
                args->get_Uri(&uri);
                NavigatedURI = uri;

                std::wstring uriW(uri);
                if(uriW.rfind(L"pix://", 0) == 0) {
                    sender->Stop();
                    if(uriW == L"pix://" + TabId || uriW == L"pix://starry") {
                        auto html = BuildStarryPage();
                        WebView->NavigateToString(html.c_str());
                    } else if(uriW == L"pix://") {
                        auto html = BuildBlankPage();
                        WebView->NavigateToString(html.c_str());
                    } else {
                        WebErrorInfo errorInfo = GetWebErrorInfoFromHttpCode(404);
                        auto html = BuildErrorPage(errorInfo, NavigatedURI.c_str());
                        WebView->NavigateToString(html.c_str());
                    }
                }

                safe_callback(Browser.OnNavigationStarting, TabId.c_str(), uri, userInit);
                CoTaskMemFree(uri);
            }
            return S_OK;
        }).Get(), &WebViewNavigationStartingEventToken);
        
        WebView->add_HistoryChanged(wrl::Callback<ICoreWebView2HistoryChangedEventHandler>([this](ICoreWebView2* sender, IUnknown*) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            LPWSTR src = nullptr;
            sender->get_Source(&src);
            NavigatedURI = src;
            safe_callback(Browser.OnHistoryChanged, TabId.c_str(), src);
            CoTaskMemFree(src);
            return S_OK;
        }).Get(), &WebViewHistoryChangedEventToken);

        WebView->add_DocumentTitleChanged(wrl::Callback<ICoreWebView2DocumentTitleChangedEventHandler>([this](ICoreWebView2* sender, IUnknown*) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            LPWSTR title = nullptr;
            sender->get_DocumentTitle(&title);
            safe_callback(Browser.OnTitleChanged, TabId.c_str(), title);
            CoTaskMemFree(title);
            return S_OK;
        }).Get(), &WebViewDocumentTitleChangedEventToken);

        WebView->add_NavigationCompleted(wrl::Callback<ICoreWebView2NavigationCompletedEventHandler>([this](ICoreWebView2* sender, ICoreWebView2NavigationCompletedEventArgs* args) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            BOOL isSuccess = FALSE;
            args->get_IsSuccess(&isSuccess);
            COREWEBVIEW2_WEB_ERROR_STATUS status = COREWEBVIEW2_WEB_ERROR_STATUS_UNKNOWN;
            args->get_WebErrorStatus(&status);
            int httpStatusCode = 0;
            wrl::ComPtr<ICoreWebView2NavigationCompletedEventArgs2> args2;
            if(SUCCEEDED(args->QueryInterface(IID_PPV_ARGS(&args2))) && args2) {
                args2->get_HttpStatusCode(&httpStatusCode);
            }

            WebErrorInfo errorInfo = httpStatusCode != 0 ? GetWebErrorInfoFromHttpCode(httpStatusCode) : GetWebErrorInfoFromStatus(status, isSuccess);
            if(errorInfo.HttpCode >= 400 && errorInfo.HttpCode != 499) {
                sender->Stop();
                auto html = BuildErrorPage(errorInfo, NavigatedURI.empty() ? L"Unknown" : NavigatedURI.c_str());
                WebView->NavigateToString(html.c_str());
                safe_callback(Browser.OnNavigationCompleted, TabId.c_str(), errorInfo.HttpCode);
                return S_OK;
            }

            if(status == COREWEBVIEW2_WEB_ERROR_STATUS_CONNECTION_ABORTED && !NavigatedURI.empty()) {
                auto uri = NavigatedURI;

                std::thread([this, uri]() {
                    Sleep(1000);
                    if(IsShuttingDown || !Host) return;
                    Host->EnqueueCommand([this, uri]() {
                        if(IsShuttingDown || !WebView) return;
                        WebView->Navigate(uri.c_str());
                    });
                }).detach();

                return S_OK;
            }

            safe_callback(Browser.OnNavigationCompleted, TabId.c_str(), errorInfo.HttpCode);

            if(Host && CookieManager) {
                Host->ImportCookiesFromTabToHost(TabId, CookieManager.Get());
            }

            return S_OK;
        }).Get(), &WebViewNavigationCompletedEventToken);

        // todo: implement api
        WebView->add_PermissionRequested(wrl::Callback<ICoreWebView2PermissionRequestedEventHandler>([this](ICoreWebView2* sender, ICoreWebView2PermissionRequestedEventArgs* args) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            COREWEBVIEW2_PERMISSION_KIND kind;
            if(SUCCEEDED(args->get_PermissionKind(&kind))) {
                switch(kind) {
                    case COREWEBVIEW2_PERMISSION_KIND_CLIPBOARD_READ:
                    case COREWEBVIEW2_PERMISSION_KIND_FILE_READ_WRITE:
                    case COREWEBVIEW2_PERMISSION_KIND_AUTOPLAY:
                    case COREWEBVIEW2_PERMISSION_KIND_LOCAL_FONTS:
                    case COREWEBVIEW2_PERMISSION_KIND_CAMERA:
                    case COREWEBVIEW2_PERMISSION_KIND_MICROPHONE:
                        args->put_State(COREWEBVIEW2_PERMISSION_STATE_ALLOW);
                        break;
                    default:
                        args->put_State(COREWEBVIEW2_PERMISSION_STATE_DENY);
                }
            }
            return S_OK;
        }).Get(), &WebViewPermissionRequestedEventToken);

        WebView->add_WebMessageReceived(wrl::Callback<ICoreWebView2WebMessageReceivedEventHandler>([this](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
            if(IsShuttingDown) return S_OK;

            LPWSTR json = nullptr;
            if(SUCCEEDED(args->get_WebMessageAsJson(&json))) {
                safe_callback(Browser.OnWebMessageReceived, TabId.c_str(), json);
                CoTaskMemFree(json);
            }
            return S_OK;
        }).Get(), &WebViewWebMessageReceivedEventToken);

        /* todo: implement api
        WebView->add_NewWindowRequested(wrl::Callback<ICoreWebView2NewWindowRequestedEventHandler>([this](ICoreWebView2* sender, ICoreWebView2NewWindowRequestedEventArgs* args) -> HRESULT {
            args->put_Handled(TRUE); // Refuse creation

            // Open in current
            ICoreWebView2Deferral* deferral = 0;
            args->GetDeferral(&deferral);

            LPWSTR uri;
            args->get_Uri(&uri);

            sender->Navigate(uri);

            args->put_Handled(TRUE);
            deferral->Complete();

            // if request is same domain, defer instead of refusing?
            return S_OK;
        }).Get(), &WebViewNewWindowRequestedEventToken);
        */

        WebView->add_FaviconChanged(wrl::Callback<ICoreWebView2FaviconChangedEventHandler>([this](ICoreWebView2* sender, IUnknown* args) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            WebView->GetFavicon(COREWEBVIEW2_FAVICON_IMAGE_FORMAT_PNG, wrl::Callback<ICoreWebView2GetFaviconCompletedHandler>([this](HRESULT errorCode, IStream* stream)->HRESULT {
                if(IsShuttingDown) return S_OK;
                
                if(FAILED(errorCode) || !stream) return S_OK;

                std::vector<uint8_t> buffer;
                STATSTG stat {};
                if(FAILED(stream->Stat(&stat, STATFLAG_NONAME))) return S_OK;

                ULONG size = static_cast<ULONG>(stat.cbSize.QuadPart);
                buffer.resize(size);
                LARGE_INTEGER zero {};
                stream->Seek(zero, STREAM_SEEK_SET, nullptr);

                ULONG bytesRead = 0;
                if(SUCCEEDED(stream->Read(buffer.data(), size, &bytesRead)) && bytesRead > 0) {
                    buffer.resize(bytesRead);
                    safe_callback(Browser.OnFavIconChanged, TabId.c_str(), buffer.data(), buffer.size());
                }
                return S_OK;
            }).Get());
            return S_OK;
        }).Get(), &WebViewFaviconChangedEventToken);

        CompositionController->add_CursorChanged(wrl::Callback<ICoreWebView2CursorChangedEventHandler>([this](ICoreWebView2CompositionController* sender, IUnknown*) -> HRESULT {
            if(IsShuttingDown) return S_OK;
            
            UINT32 cursorId = 0;
            if(SUCCEEDED(sender->get_SystemCursorId(&cursorId))) {
                safe_callback(Browser.OnCursorChanged, cursorId);
            }
            return S_OK;
        }).Get(), &WebViewCursorChangedEventToken);

        //WebView->add_IsDocumentPlayingAudioChanged

        auto compositor = winrt::Compositor();
        auto rootVisual = compositor.CreateContainerVisual();
        winrt::com_ptr<IUnknown> rootVisualTarget = rootVisual.as<IUnknown>();
        CompositionController->put_RootVisualTarget(rootVisualTarget.get());
        WinRTCaptureItem = winrt::GraphicsCaptureItem::CreateFromVisual(rootVisual);

        IDXGIDevice* dxgiDevice = nullptr;
        if(OnTabFailed(Browser.D3D11Device->QueryInterface(IID_PPV_ARGS(&dxgiDevice)), TabId.c_str(), L"DXGIDevice QI Failed")) return E_FAIL;
        winrt::com_ptr<IInspectable> inspectableDevice;
        if(OnTabFailed(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, inspectableDevice.put()), TabId.c_str(), L"D3D11Device Creation Failed")) {
            dxgiDevice->Release();
            return E_FAIL;
        }
        dxgiDevice->Release();

        WinRTDevice = inspectableDevice.as<winrt::IDirect3DDevice>();
        WinRTPixelFormat = winrt::DirectXPixelFormat::B8G8R8A8UIntNormalized;
        WinRTFramePool = winrt::Direct3D11CaptureFramePool::Create(WinRTDevice, WinRTPixelFormat, 2, { (int32_t)w, (int32_t)h });
        WinRTFramePool.FrameArrived({ this, &BrowserTab::OnFrameArrived });
        WinRTCaptureSession = WinRTFramePool.CreateCaptureSession(WinRTCaptureItem);
        if(WinRTCaptureSession) WinRTCaptureSession.StartCapture();

        UINT32 pid = 0;
        WebView->get_BrowserProcessId(&pid);
        BrowserProcessId = static_cast<DWORD>(pid);

        UpdateBounds(x, y, w, h);
        InstallPendingExtensions();

        InjectMediaStateScript();

        return S_OK;
    }).Get());

    return SUCCEEDED(hr);
}

void BrowserTab::Shutdown() {
    std::lock_guard<std::mutex> lk(LifecycleMutex);
    if(IsShuttingDown) return;
    IsShuttingDown = true;

    if(WinRTCaptureSession) {
        try { WinRTCaptureSession.Close(); } catch(...) {}
        WinRTCaptureSession = nullptr;
    }
    for(int i = 0; i < 5 && HasNewFrame.load(std::memory_order_acquire); i++) Sleep(8);
    if(WinRTFramePool) {
        try { WinRTFramePool.Close(); } catch(...) {}
        WinRTFramePool = nullptr;
    }

    if(CompositionController) {
        try {
            CompositionController->put_RootVisualTarget(nullptr);
            CompositionController->remove_CursorChanged(WebViewCursorChangedEventToken);
        } catch(...) {}
    }

    if(WebView) {
        try {
            WebView->remove_ProcessFailed(WebViewProcessFailedEventToken);
            //WebView->remove_WebResourceRequested(WebViewWebResourceRequestedEventToken);
            WebView->remove_NavigationStarting(WebViewNavigationStartingEventToken);
            WebView->remove_HistoryChanged(WebViewHistoryChangedEventToken);
            WebView->remove_DocumentTitleChanged(WebViewDocumentTitleChangedEventToken);
            WebView->remove_NavigationCompleted(WebViewNavigationCompletedEventToken);
            WebView->remove_PermissionRequested(WebViewPermissionRequestedEventToken);
            WebView->remove_WebMessageReceived(WebViewWebMessageReceivedEventToken);
            WebView->remove_FaviconChanged(WebViewFaviconChangedEventToken);
            //WebView->remove_FrameCreated(WebViewFrameCreatedEventToken);
        } catch(...) {}

        WebView->Stop();
    }

    if(Controller) {
        Controller->put_IsVisible(FALSE);
        try { Controller->Close(); } catch(...) {}
    }

    ReleaseSharedTexture();

    CookieManager = nullptr;
    Profile = nullptr;
    WebViewSettings = nullptr;
    WebView = nullptr;
    Controller = nullptr;
    CompositionController = nullptr;
    Environment = nullptr;

    HasNewFrame.store(false, std::memory_order_release);

    safe_callback(Browser.OnTabDestroyed, TabId.c_str());
}

void BrowserTab::Navigate(const wchar_t* uri) { if(WebView) WebView->Navigate(uri); }
void BrowserTab::Reload() { if(WebView) WebView->Reload(); }
void BrowserTab::StopNavigation() { if(WebView) WebView->Stop(); }
void BrowserTab::OpenDevTools() { if(WebView) WebView->OpenDevToolsWindow(); }

void BrowserTab::UpdateBounds(int32_t x, int32_t y, uint32_t w, uint32_t h) {
    if(IsShuttingDown) return;
    if(!Controller) return;
    if(w < 1) w = RenderWidth;
    if(h < 1) h = RenderHeight;
    if(w < 1 || h < 1 || x + w <= 4 || y + h <= 4) return;

    bool positionChanged = RenderX != x || RenderY != y;
    bool sizeChanged = RenderWidth != w || RenderHeight != h;
    if(!positionChanged && !sizeChanged) return;

    RECT bounds { x, y, (int32_t)(x + w), (int32_t)(y + h) };
    Controller->put_Bounds(bounds);

    if(positionChanged) {
        RenderX = x; RenderY = y;
        Controller->NotifyParentWindowPositionChanged();
    }

    if(sizeChanged && WinRTFramePool) {
        RenderWidth = w; RenderHeight = h;
        WinRTFramePool.Recreate(WinRTDevice, WinRTPixelFormat, 2, { (int32_t)w, (int32_t)h });
    }
}

void BrowserTab::MouseEvent(UINT message, WPARAM wParam, LPARAM lParam) {
    if(IsShuttingDown) return;
    if(!CompositionController) return;
    if(message != WM_MOUSEWHEEL) {
        MousePosition.x = LOWORD(lParam);
        MousePosition.y = HIWORD(lParam);
    }

    COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS flag = COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE;
    if(GetKeyState(VK_SHIFT) & 0x8000) flag |= COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_SHIFT;
    if(GetKeyState(VK_CONTROL) & 0x8000) flag |= COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_CONTROL;

    if(message == WM_MOUSEMOVE && !TrackingMouse) {
        TRACKMOUSEEVENT tme { sizeof(TRACKMOUSEEVENT) };
        tme.dwFlags = TME_LEAVE;
        tme.hwndTrack = Browser.GameHwnd;
        TrackMouseEvent(&tme);
        TrackingMouse = true;
    }

    switch(message) {
        case WM_MOUSEMOVE:
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_MOVE, ButtonState | flag, 0, { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_LBUTTONDOWN:
            ButtonState = (COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS)(ButtonState | COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_LEFT_BUTTON);
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_DOWN, ButtonState | flag, GET_XBUTTON_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_LBUTTONUP:
            ButtonState = (COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS)(ButtonState & ~COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_LEFT_BUTTON);
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_UP, ButtonState | flag, GET_XBUTTON_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_RBUTTONDOWN:
            ButtonState = (COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS)(ButtonState | COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_RIGHT_BUTTON);
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_RIGHT_BUTTON_DOWN, ButtonState | flag, GET_XBUTTON_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_RBUTTONUP:
            ButtonState = (COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS)(ButtonState & ~COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_RIGHT_BUTTON);
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_RIGHT_BUTTON_UP, ButtonState | flag, GET_XBUTTON_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_MBUTTONDOWN:
            ButtonState = (COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS)(ButtonState | COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_MIDDLE_BUTTON);
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_MIDDLE_BUTTON_DOWN, ButtonState | flag, GET_XBUTTON_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_MBUTTONUP:
            ButtonState = (COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS)(ButtonState & ~COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_MIDDLE_BUTTON);
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_MIDDLE_BUTTON_UP, ButtonState | flag, GET_XBUTTON_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_MOUSEWHEEL:
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_WHEEL, ButtonState | flag, GET_WHEEL_DELTA_WPARAM(wParam), { (LONG)MousePosition.x, (LONG)MousePosition.y });
            break;
        case WM_MOUSELEAVE:
            TrackingMouse = false;
            CompositionController->SendMouseInput(COREWEBVIEW2_MOUSE_EVENT_KIND_LEAVE, COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE, 0, { 0,0 });
            break;
    }
}

void BrowserTab::InstallPendingExtensions() {
    if(IsShuttingDown) return;
    if(!Host || !WebView || !Profile) return;

    std::vector<std::wstring> pendingIds = Host->GetPendingExtensions();
    if(pendingIds.empty()) {
        if(Host) Host->NotifyTabReady(TabId);
        return;
    }

    int found = 0;
    try {
        for(const auto& id : pendingIds) {
            if(!InstallPendingExtension(id, id)) continue;
            found++;
        }
    } catch(const std::exception& e) {
        LOGWARN(L"Extension Installation Error: %S", e.what());
    }

    if(found == 0 && Host) Host->NotifyTabReady(TabId);
}

bool BrowserTab::InstallPendingExtension(const std::wstring& extensionId, const std::wstring& extensionName) {
    if(IsShuttingDown) return false;
    if(!WebView || !Profile) return false;
    if(extensionId.empty() || extensionName.empty()) return false;
    std::wstring extensionPath = GetUnpackedExtensionPath(extensionId);
    if(!fs::exists(extensionPath) || !fs::is_directory(extensionPath)) return false;

    if(Host) Host->RegisterTabPendingInstall(TabId);

    std::wstring tabIdCopy = TabId;
    std::wstring extIdCopy = extensionId;
    std::wstring extNameCopy = extensionName;
    std::wstring extPathCopy = extensionPath;
    BrowserHost* hostPtr = Host;

    Profile->AddBrowserExtension(extensionPath.c_str(), wrl::Callback<ICoreWebView2ProfileAddBrowserExtensionCompletedHandler>([this, tabIdCopy, extIdCopy, extNameCopy, extPathCopy, hostPtr](HRESULT hr, ICoreWebView2BrowserExtension*) -> HRESULT {
        if(IsShuttingDown) return S_OK;
        
        if(FAILED(hr)) {
            LOGWARN(L"[%s] Extension Install Failed: %s", tabIdCopy.c_str(), extNameCopy.c_str());
        } else {
            LOGVERBOSE(L"[%s] Extension Installed: %s", tabIdCopy.c_str(), extNameCopy.c_str());
        }

        if(hostPtr) hostPtr->NotifyTabExtensionInstallResult(extIdCopy, tabIdCopy, SUCCEEDED(hr));

        return S_OK;
    }).Get());
    return true;
}

bool BrowserTab::InstallExtension(const std::wstring& extensionId, const std::wstring& extensionName) {
    if(IsShuttingDown) return false;
    if(!WebView || !Profile) return false;
    if(extensionId.empty() || extensionName.empty()) return false;
    std::wstring extensionPath = GetUnpackedExtensionPath(extensionId);
    if(!fs::exists(extensionPath) || !fs::is_directory(extensionPath)) return false;

    Profile->AddBrowserExtension(extensionPath.c_str(), wrl::Callback<ICoreWebView2ProfileAddBrowserExtensionCompletedHandler>([this, extensionId, extensionName, extensionPath](HRESULT hr, ICoreWebView2BrowserExtension*) -> HRESULT {
        if(IsShuttingDown) return S_OK;
        
        if(FAILED(hr)) {
            LOGWARN(L"[%s] Extension Install Failed: %s", TabId.c_str(), extensionName.c_str());
        } else {
            LOGVERBOSE(L"[%s] Extension Installed: %s", TabId.c_str(), extensionName.c_str());
        }

        if(Host) Host->NotifyExtensionOperationResult(extensionId, TabId, SUCCEEDED(hr));

        return S_OK;
    }).Get());
    return true;
}

bool BrowserTab::UninstallExtension(const std::wstring& extensionId, const std::wstring& extensionName) {
    if(IsShuttingDown) return false;
    if(!WebView || !Profile) return false;
    if(extensionId.empty() || extensionName.empty()) return false;
    std::wstring extensionPath = GetUnpackedExtensionPath(extensionId);

    Profile->GetBrowserExtensions(wrl::Callback<ICoreWebView2ProfileGetBrowserExtensionsCompletedHandler>([this, extensionId, extensionName, extensionPath](HRESULT hr, ICoreWebView2BrowserExtensionList* result) -> HRESULT {
        if(IsShuttingDown) return S_OK;
        
        if(FAILED(hr) || !result) {
            LOGWARN(L"[%s] Extension Retrieval Failed: %s", TabId.c_str(), extensionName.c_str());
            if(Host) Host->NotifyExtensionOperationResult(extensionId, TabId, false);
            return S_OK;
        }

        uint32_t count = 0;
        result->get_Count(&count);
        bool initiated = false;
        for(uint32_t i = 0; i < count; i++) {
            wrl::ComPtr<ICoreWebView2BrowserExtension> ext;
            if(FAILED(result->GetValueAtIndex(i, &ext)) || !ext) continue;

            LPWSTR extName = nullptr;
            if(SUCCEEDED(ext->get_Name(&extName)) && extName) {
                if(extensionName == extName) {
                    ext->Remove(wrl::Callback<ICoreWebView2BrowserExtensionRemoveCompletedHandler>([this, extensionId, extensionName, extensionPath](HRESULT hr) -> HRESULT {
                        if(IsShuttingDown) return S_OK;
                        
                        if(FAILED(hr)) {
                            LOGWARN(L"[%s] Extension Removal Failed: %s", TabId.c_str(), extensionName.c_str());
                        } else {
                            LOGVERBOSE(L"[%s] Extension Removed: %s", TabId.c_str(), extensionName.c_str());
                        }
                        if(Host) Host->NotifyExtensionOperationResult(extensionId, TabId, SUCCEEDED(hr));
                        return S_OK;
                    }).Get());
                    initiated = true;
                    CoTaskMemFree(extName);
                    break;
                }
                CoTaskMemFree(extName);
            }
        }
        if(!initiated && Host) {
            Host->NotifyExtensionOperationResult(extensionId, TabId, true);
        }
        return S_OK;
    }).Get());
    return true;
}

bool BrowserTab::ToggleExtension(const std::wstring& extensionId, const std::wstring& extensionName, bool newState) {
    if(IsShuttingDown) return false;
    if(!WebView || !Profile) return false;
    if(extensionId.empty() || extensionName.empty()) return false;
    std::wstring extensionPath = GetUnpackedExtensionPath(extensionId);

    Profile->GetBrowserExtensions(wrl::Callback<ICoreWebView2ProfileGetBrowserExtensionsCompletedHandler>([this, extensionId, extensionName, newState, extensionPath](HRESULT hr, ICoreWebView2BrowserExtensionList* result) -> HRESULT {
        if(IsShuttingDown) return S_OK;
        
        if(FAILED(hr) || !result) {
            LOGWARN(L"[%s] Extension Retrieval Failed: %s", TabId.c_str(), extensionName.c_str());
            if(Host) Host->NotifyExtensionOperationResult(extensionId, TabId, false);
            return S_OK;
        }

        uint32_t count = 0;
        result->get_Count(&count);
        bool initiated = false;
        for(uint32_t i = 0; i < count; i++) {
            wrl::ComPtr<ICoreWebView2BrowserExtension> ext;
            if(FAILED(result->GetValueAtIndex(i, &ext)) || !ext) continue;

            LPWSTR extName = nullptr;
            if(SUCCEEDED(ext->get_Name(&extName)) && extName) {
                if(extensionName == extName) {
                    ext->Enable(newState, wrl::Callback<ICoreWebView2BrowserExtensionEnableCompletedHandler>([this, extensionId, extensionName, newState, extensionPath](HRESULT hr) -> HRESULT {
                        if(IsShuttingDown) return S_OK;
                        
                        if(!FAILED(hr)) {
                            if(newState) {
                                LOGVERBOSE(L"[%s] Extension Enabled: %s", TabId.c_str(), extensionName.c_str());
                            } else {
                                LOGVERBOSE(L"[%s] Extension Disabled: %s", TabId.c_str(), extensionName.c_str());
                            }
                        }
                        if(Host) Host->NotifyExtensionOperationResult(extensionId, TabId, SUCCEEDED(hr));
                        return S_OK;
                    }).Get());
                    initiated = true;
                    CoTaskMemFree(extName);
                    break;
                }
                CoTaskMemFree(extName);
            }
        }
        if(!initiated && Host) {
            Host->NotifyExtensionOperationResult(extensionId, TabId, false);
        }
        return S_OK;
    }).Get());
    return true;
}

void BrowserTab::MoveFocusProgrammatic() {
    if(IsShuttingDown) return;
    if(!Controller) return;
    Controller->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
}

bool BrowserTab::CreateSharedTexture(uint32_t width, uint32_t height) {
    if(IsShuttingDown) return false;
    if(!Browser.D3D11Device) return false;

    D3D11_TEXTURE2D_DESC texDesc {};
    texDesc.Width = width; texDesc.Height = height; texDesc.MipLevels = 1; texDesc.ArraySize = 1;
    texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; texDesc.SampleDesc.Count = 1;
    texDesc.Usage = D3D11_USAGE_DEFAULT; texDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    texDesc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;

    ID3D11Texture2D* tex = nullptr;
    if(FAILED(Browser.D3D11Device->CreateTexture2D(&texDesc, nullptr, &tex))) { safe_release(tex); return false; }

    IDXGIResource* dxgiRes = nullptr;
    if(FAILED(tex->QueryInterface(IID_PPV_ARGS(&dxgiRes)))) { safe_release(tex); return false; }

    HANDLE handle = nullptr;
    if(FAILED(dxgiRes->GetSharedHandle(&handle))) { dxgiRes->Release(); tex->Release(); return false; }
    dxgiRes->Release();

    SharedTexture = tex;
    SharedHandle = handle;
    FrameWidth = width;
    FrameHeight = height;
    return true;
}

void BrowserTab::ReleaseSharedTexture() {
    if(SharedTexture) { SharedTexture->Release(); SharedTexture = nullptr; }
    SharedHandle = nullptr;
}

void BrowserTab::OnFrameArrived(winrt::Direct3D11CaptureFramePool const& sender, winrt::Windows::Foundation::IInspectable const&) {
    if(IsShuttingDown) return;
    auto frame = sender.TryGetNextFrame();
    if(!frame) return;
    auto contentSize = frame.ContentSize();
    if(contentSize.Width < 1 || contentSize.Height < 1) { frame.Close(); return; }

    auto surface = frame.Surface();
    if(!surface) { frame.Close(); return; }

    auto access = surface.as<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    if(!access) { frame.Close(); return; }

    ID3D11Texture2D* frameTex = nullptr;
    if(FAILED(access->GetInterface(winrt::guid_of<ID3D11Texture2D>(), reinterpret_cast<void**>(&frameTex)))) { frame.Close(); return; }

    if(!SharedTexture || FrameWidth != contentSize.Width || FrameHeight != contentSize.Height) {
        if(SharedTexture) { SharedHandle = nullptr; SharedTexture->Release(); SharedTexture = nullptr; }
        if(!CreateSharedTexture((uint32_t)contentSize.Width, (uint32_t)contentSize.Height)) { safe_release(frameTex); frame.Close(); return; }
    }

    ID3D11DeviceContext* ctx = nullptr;
    Browser.D3D11Device->GetImmediateContext(&ctx);
    if(ctx) {
        ctx->CopyResource(SharedTexture, frameTex);
        ctx->Flush();
        ctx->Release();
    }

    safe_release(frameTex);
    HasNewFrame.store(true, std::memory_order_release);
    frame.Close();
}

void BrowserTab::UpdateMediaState(uint32_t action, bool isPlaying, int64_t seekTime, int64_t duration, int64_t timestamp) {
    if(IsShuttingDown || !WebView) return;

    std::wstringstream ss;
    ss
        << L"{"
        << L"\"Action\":" << (int)action << L","
        << L"\"IsPlaying\":" << (isPlaying ? L"true" : L"false") << L","
        << L"\"SeekTime\":" << seekTime << L","
        << L"\"Duration\":" << duration << L","
        << L"\"Timestamp\":" << timestamp
        << L"}";

    std::wstring script =
        L"window.PyonPixMedia?.sync?.applyUpdate(" +
        ss.str() +
        L");";

    LOGVERBOSE(L"[%s] Injecting Media State: { Action: %i, IsPlaying: %s, SeekTime: %lli, Duration: %lli, Timestamp: %lli }", TabId.c_str(), action, isPlaying ? L"True" : L"False", seekTime, duration, timestamp);
    WebView->ExecuteScript(script.c_str(), nullptr);
}

void BrowserTab::ToggleTheatreMode() {
    if(IsShuttingDown || !WebView) return;

    WebView->ExecuteScript(L"window.PyonPixMedia?.theatre?.toggle();", nullptr);
}

void BrowserTab::InjectMediaStateScript() {
    if(IsShuttingDown || !WebView) return;

    static const wchar_t* script = LR"(
(() => {
    if (window !== window.top) return;
    if (window.PyonPixMedia) return;

    class SiteAdapter {
        constructor() {
            this.syncEnabled = true;
            this.theatreEnabled = true;
        }

        identify(media) {
            return {
                host: 0,
                source: media.currentSrc || media.src || "",
                index: 0
            };
        }
    }
	
	class MediaManager {
		constructor() {
			this.media = null;
			this.mediaChangedListeners = [];
			this.lastUrl = location.href;
		}
		
		initialize() {
			this.observe();

			setTimeout(() => {
				this.scan();
			}, 500);
		}

		observe() {
			new MutationObserver(() => {
				this.scan();
			}).observe(document.body, {
				childList:true,
				subtree:true
			});
		}

		scan() {
			const media = document.querySelector("video, audio");
			if(!media) return;
			if(media.readyState < HTMLMediaElement.HAVE_METADATA) return;
			
			const urlChanged = this.lastUrl !== location.href;
			if(urlChanged) this.lastUrl = location.href;
			if(media === this.media && !urlChanged) return;
			
			const oldMedia = this.media;
			this.media = media;

			this.emitMediaChanged(oldMedia, media);
		}

		onMediaChanged(callback) {
			this.mediaChangedListeners.push(callback);

			return () => {
				const index = this.mediaChangedListeners.indexOf(callback);
				if(index >= 0) this.mediaChangedListeners.splice(index, 1);
			};
		}

		emitMediaChanged(oldMedia, newMedia) {
			for(const callback of this.mediaChangedListeners) {
				callback(oldMedia, newMedia);
			}
		}
	}

	class MediaController {
		constructor(mediaManager) {
			this.mediaManager = mediaManager;
			this.currentMedia = null;
			this.currentSource = null;
			this.lastSource = null;
			
			this.mediaChangedListeners = [];
			this.mediaSourceChangedListeners = [];
			this.mediaReadyListeners = [];
			
			this.unwatch = null;

			this.unsubscribeManager = this.mediaManager.onMediaChanged((oldMedia, newMedia) => {
				if(this.unwatch) this.unwatch();
				this.setMedia(newMedia);
				if(newMedia) this.unwatch = this.watch(newMedia);
			});
		}
		
		get media() {
			return this.currentMedia;
		}
		
		setMedia(media) {
			const oldMedia = this.currentMedia;
			if(oldMedia === media) return false;

			this.currentMedia = media;
			this.currentSource = media?.currentSrc || media?.src || null;
			this.lastSource = null;

			console.log("[PyonPix] SetMedia Element Changed");

			for(const callback of this.mediaChangedListeners)
				callback(oldMedia, media);

			return true;
		}
		
		watch(media) {
			const attempt = () => {
				if(media !== this.currentMedia) return;
				const currentSrc = media.currentSrc || media.src;
				if(!currentSrc) return;
				if(media.readyState < HTMLMediaElement.HAVE_METADATA) return;
				if(!Number.isFinite(media.duration)) return;
				if(currentSrc === this.lastSource) return;

				this.lastSource = currentSrc;

				console.log("[PyonPix] MediaReady: ", currentSrc);
				this.emitMediaReady(media);
			};
			
			const onMetadata = () => {
				this.checkSourceChanged(media);
				attempt();
			};
			media.addEventListener("loadedmetadata", onMetadata);
			media.addEventListener("canplay", attempt);
			media.addEventListener("playing", attempt);

			const observer = new MutationObserver(attempt);
			observer.observe(media, { attributes: true, attributeFilter: ["src"] });

			const interval = setInterval(() => {
				this.checkSourceChanged(media);
				attempt();
			}, 500);

			return () => {
				media.removeEventListener("loadedmetadata", onMetadata);
				media.removeEventListener("canplay", attempt);
				media.removeEventListener("playing", attempt);
				
				observer.disconnect();
				clearInterval(interval);
			};
		}
		
		onMediaChanged(callback) {
			this.mediaChangedListeners.push(callback);

			return () => {
				const index = this.mediaChangedListeners.indexOf(callback);
				if(index >= 0) this.mediaChangedListeners.splice(index, 1);
			};
		}
		
		onMediaSourceChanged(callback) {
			this.mediaSourceChangedListeners.push(callback);

			return () => {
				const index = this.mediaSourceChangedListeners.indexOf(callback);
				if(index >= 0) this.mediaSourceChangedListeners.splice(index, 1);
			};
		}
		
		checkSourceChanged(media) {
			const source = media.currentSrc || media.src || "";
			if(source === this.currentSource) return;

			const oldSource = this.currentSource;
			this.currentSource = source;
			this.lastSource = null;

			console.log("[PyonPix] Media Source Changed", { old: oldSource, new: source });

			for(const callback of this.mediaSourceChangedListeners)
				callback(media, oldSource, source);
		}
		
		onMediaReady(callback) {
			this.mediaReadyListeners.push(callback);

			return () => {
				const index = this.mediaReadyListeners.indexOf(callback);
				if(index >= 0) this.mediaReadyListeners.splice(index, 1);
			};
		}

		emitMediaReady(media) {
			if(media !== this.currentMedia) return;
			for(const callback of this.mediaReadyListeners)
				callback(media);
		}

		play() {
			if(!this.media) return;
			return this.media.play();
		}

		pause() {
			if(!this.media) return;
			this.media.pause();
		}

		togglePlay() {
			if(!this.media) return;
			if(this.media.paused) return this.play();
			this.pause();
		}

		set volume(value) {
			if(!this.media) return;
			this.media.volume = value;
		}

		get volume() {
			return this.media?.volume ?? 0;
		}

		seek(value) {
			if(!this.media || !this.media.duration) return;
			this.media.currentTime = value;
		}
		
		get currentTime() {
			return this.media?.currentTime ?? 0;
		}

		get duration() {
			return this.media?.duration ?? 0;
		}

		get paused() {
			return this.media?.paused ?? true;
		}
	}

    class Theatre {
        constructor(mediaController) {
            this.mediaController = mediaController;
			
            this.enabled = false;
			this.activeMedia = null;
            this.overlay = null;
            this.controls = null;
			this.videoContainer = null;
			
			this.originalParent = null;
			this.originalNextSibling = null;
			this.originalStyle = null;
			this.originalOverflow = null;

			this.controlsHovered = false;
            this.hideTimer = null;
        }

        initialize() {
            this.createOverlay();
			
			this.mediaController.onMediaChanged((oldMedia, newMedia) => {
				console.log("[PyonPix] Theatre Media Changed", { old: oldMedia?.currentSrc, new: newMedia?.currentSrc, enabled: this.enabled });

				if(newMedia instanceof HTMLVideoElement && !this.enabled)
					this.enable();
			});
			
			this.mediaController.onMediaSourceChanged((media, oldSrc, newSrc) => {
				console.log("[PyonPix] Theatre source Changed", { oldSrc, newSrc, enabled: this.enabled });
				if(media instanceof HTMLVideoElement && !this.enabled)
					this.enable();
			});
        }

        createOverlay() {
			const fa = document.createElement("link");
			fa.rel = "stylesheet";
			fa.href = "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.2/css/all.min.css";
			document.head.appendChild(fa);
			
            this.overlay = document.createElement("div");
            this.overlay.id = "pyonpix-overlay";

			Object.assign(this.overlay.style, {
				position: "fixed",
				inset: "0",
				display: "none",
				zIndex: "2147483646",
				pointerEvents: "auto",
				background:"transparent"
			});
			
			this.videoContainer = document.createElement("div");
			Object.assign(this.videoContainer.style, {
				position: "absolute",
				inset: "0",
				display: "flex",
				justifyContent: "center",
				alignItems: "center",
				overflow: "hidden",
				background: "black",
				zIndex:"0",
				pointerEvents:"auto"
			});
			
			this.controlsOverlay = document.createElement("div");
			Object.assign(this.controlsOverlay.style, {
				position: "fixed",
				inset: "0",
				display: "none",
				zIndex: "2147483647",
				pointerEvents: "none"
			});
			
			this.controls = document.createElement("div");
			Object.assign(this.controls.style, {
				position: "absolute",
				left: "0",
				right: "0",
				bottom: "0",
				height: "54px",
				display: "flex",
				alignItems: "center",
				padding: "18px 6px 6px 6px",
				gap: "6px",
				background: "rgba(0,0,0,0.65) ",
				pointerEvents: "auto",
				opacity: "1",
				transition: "opacity 0.2s",
				overflow: "visible"
			});
			
			document.body.append(this.overlay, this.controlsOverlay);
			this.overlay.append(this.videoContainer);
			this.controlsOverlay.append(this.controls);
			
			const style = document.createElement("style");
			style.textContent = `
			html.pyonpix-theatre video {
				width:100% !important;
				height:100% !important;
				max-width:none !important;
				max-height:none !important;
				object-fit:contain !important;
			}

			/* Controls */
			#pyonpix-controls button {
				background:none;
				border:none;
				width: 26px;
				height: 26px;
				padding:0;
				color:white;
				font-size:26px;
				cursor:pointer;
				display:flex;
				align-items:center;
				justify-content:center;
			}

			#pyonpix-controls button:hover {
				color:#aaa;
			}

			#pyonpix-controls span {
				color:white;
				font-size:14px;
				white-space:nowrap;
				font-family:Arial,sans-serif;
			}

			/* Seek / volume bars */
			#pyonpix-seek,
			#pyonpix-volume {
				appearance:none;
				-webkit-appearance:none;
				height:10px;
				cursor:pointer;
				background:transparent;
			}

			#pyonpix-seek::-webkit-slider-runnable-track {
				height:10px;
				background:
					linear-gradient(
						to right,
						#f00 0%,
						#f00 var(--progress, 0%),
						#333 var(--progress, 0%),
						#333 100%
					);
			}

			#pyonpix-volume::-webkit-slider-runnable-track {
				height:10px;
				background:
					linear-gradient(
						to right,
						white 0%,
						white var(--progress, 100%),
						#333 var(--progress, 100%),
						#333 100%
					);
			}

			#pyonpix-seek::-webkit-slider-thumb,
			#pyonpix-volume::-webkit-slider-thumb {
				width:10px;
				height:10px;
				border-radius:50%;
				background:transparent;
				border:none;
				opacity:0;
			}

			/* Layout */
			#pyonpix-controls {
				user-select:none;
			}

			#pyonpix-volume {
				width:80px;
			}

			#pyonpix-seek {
				position:absolute;
				left:2px;
				right:2px;
				top:2px;
				width:calc(100% - 4px);
			}
			`;
			document.head.appendChild(style);

            this.buildControls();
			
			document.addEventListener("mousemove", () => {
				this.showControls();
			}, true);
			
			this.controls.addEventListener("mouseenter", () => {
				this.controlsHovered = true;
				clearTimeout(this.hideTimer);
				this.controls.style.opacity = "1";
			});

			this.controls.addEventListener("mouseleave", () => {
				this.controlsHovered = false;
				this.startHideTimer();
			});
			
			this.videoContainer.addEventListener("click", e => {
				if(!this.enabled) return;
				this.mediaController.togglePlay();
				e.preventDefault();
				e.stopImmediatePropagation();
			}, true);
        }
		
		showControls() {
			if(!this.enabled) return;

			this.controls.style.opacity = "1";
			clearTimeout(this.hideTimer);
			this.hideTimer = setTimeout(() => {
				this.controls.style.opacity = "0";
			}, 1500);
		}
		
		showControls() {
			if(!this.enabled) return;

			this.controls.style.opacity = "1";
			clearTimeout(this.hideTimer);
			if(!this.controlsHovered)
				this.startHideTimer();
		}
		
		startHideTimer() {
			clearTimeout(this.hideTimer);

			this.hideTimer = setTimeout(() => {
				if(!this.controlsHovered)
					this.controls.style.opacity = "0";
			}, 1500);
		}

        buildControls() {
            this.playButton = this.makeButton("fa-solid fa-play");
			
			this.resync = this.makeButton("fa-solid fa-rotate");

            this.time = document.createElement("span");

            this.seek = document.createElement("input");
			this.seek.id = "pyonpix-seek";
            this.seek.type = "range";
            this.seek.min = 0;
            this.seek.max = 1000;
			
			this.volumeButton = this.makeButton("fa-solid fa-volume-high");

            this.volume = document.createElement("input");
			this.volume.id = "pyonpix-volume";
            this.volume.type = "range";
            this.volume.min = 0;
            this.volume.max = 100;
            this.volume.value = 100;

            this.volumeContainer = document.createElement("div");
			Object.assign(this.volumeContainer.style,{
				display:"flex",
				alignItems:"center",
				gap:"8px",
				color:"white"
			});
			
			this.volumeContainer.append(this.volumeButton, this.volume);

            this.theatre = this.makeButton("fa-solid fa-expand");

			this.controls.id = "pyonpix-controls";
            this.controls.append(this.seek, this.playButton, this.resync, this.time);
			
			const spacer = document.createElement("div");
			Object.assign(spacer.style,{
				flex:"1"
			});
			
			this.controls.append(spacer, this.volumeContainer, this.theatre);

            this.playButton.onclick = () => this.mediaController.togglePlay();

			this.resync.onclick = () => {
				const media = this.mediaController.media;
				if(!media) return;
				if(!Number.isFinite(media.duration)) return;
				
				chrome.webview.postMessage({
					Type: "MediaResync",
					Payload: {
						Action: media.paused ? SyncAction.Pause : SyncAction.Play,
						IsPlaying: !media.paused,
						SeekTime: Math.floor(media.currentTime * 1000),
						Duration: Math.floor(media.duration * 1000),
						Timestamp: Date.now()
					}
				});
            };
			
			this.volumeButton.onclick = () => {
				if(this.mediaController.volume > 0) {
					this.previousVolume = this.mediaController.volume;
					this.mediaController.volume = 0;
				} else {
					this.mediaController.volume = this.previousVolume ?? 1;
				}
			};
			
			this.volume.oninput = () => this.mediaController.volume = this.volume.value / 100;

			this.seeking = false;
			this.seek.onpointerdown = () => { this.seeking = true; };
			this.seek.onpointerup = () => {
				this.seeking = false;
				this.mediaController.seek(this.mediaController.duration * this.seek.value / 1000);
			};
			this.seek.oninput = () => {
				if(!this.seeking) return;
				const time = this.mediaController.duration * this.seek.value / 1000;
				this.time.textContent = this.formatTime(time) + " / " + this.formatTime(this.mediaController.duration);
			};
			
            this.theatre.onclick = () => {
                this.toggle();
            };
        }

		makeButton(icon) {
			const b = document.createElement("button");
			const i = document.createElement("i");
			i.className = icon;
			b.appendChild(i);
			return b;
		}

        enable() {
			const media = this.mediaController.media;
			if(!(media instanceof HTMLVideoElement)) return;
			if(this.enabled && this.activeMedia === media) return;
			if(this.enabled) this.disable();
			
            this.enabled = true;
			this.activeMedia = media;
			
			this.originalParent = media.parentNode;
			this.originalNextSibling = media.nextSibling;
			
			this.originalStyle = {
				width: media.style.width,
				height: media.style.height,
				position: media.style.position,
				objectFit: media.style.objectFit,
				transform: media.style.transform,
				margin: media.style.margin,
				maxWidth: media.style.maxWidth,
				maxHeight: media.style.maxHeight
			};
			
			this.overlay.style.display = "block";
			this.controlsOverlay.style.display = "block";
			
			this.videoContainer.appendChild(media);
			
			Object.assign(media.style,{
				position:"relative",
				width:"100%",
				height:"100%",
				maxWidth:"100%",
				maxHeight:"100%",
				objectFit:"contain",
				transform:"none",
				margin:"0"
			});
			
			this.volume.value = this.mediaController.volume * 100;
			document.documentElement.classList.add("pyonpix-theatre");

			this.originalOverflow = document.body.style.overflow;
			document.body.style.overflow="hidden";
        }

        disable() {
			if(!this.enabled) return;

			const media = this.activeMedia;
			if(media && this.originalParent){
				if(this.originalNextSibling)
					this.originalParent.insertBefore(media, this.originalNextSibling);
				else
					this.originalParent.appendChild(media);
			}
			
			if(media && this.originalStyle){
				Object.assign(media.style, this.originalStyle);
			}
			
			document.documentElement.classList.remove("pyonpix-theatre");
			
			document.body.style.overflow = this.originalOverflow;
			this.overlay.style.display = "none";
			this.controlsOverlay.style.display = "none";
			
			this.enabled = false;
			this.activeMedia = null;
        }

        toggle() {
            if(this.enabled) {
                this.disable();
            } else {
                this.enable();
            }
        }

        update() {
			if(!this.mediaController.media) return;

			this.playButton.firstChild.className = this.mediaController.paused ? "fa-solid fa-play" : "fa-solid fa-pause";
			
			const current = this.mediaController.currentTime;
			const duration = this.mediaController.duration;

            this.time.textContent = this.formatTime(current) + " / " + this.formatTime(duration);
			
			if(duration && !this.seeking) {
				const progress = current / duration * 100;
				this.seek.value = current / duration * 1000;
				this.seek.style.setProperty("--progress", `${progress}%`);
			}

			const volume = this.mediaController.volume * 100;
			if(volume === 0)
				this.volumeButton.firstChild.className = "fa-solid fa-volume-xmark";
			else if(volume < 50)
				this.volumeButton.firstChild.className = "fa-solid fa-volume-low";
			else
				this.volumeButton.firstChild.className = "fa-solid fa-volume-high";
			this.volume.value = volume;
			this.volume.style.setProperty("--progress", `${volume}%`);
        }
		
		formatTime(seconds) {
            if(!Number.isFinite(seconds)) return "--:--";

            seconds = Math.floor(seconds);

            const h = Math.floor(seconds / 3600);
            const m = Math.floor(seconds / 60) % 60;
            const s = seconds % 60;

            if(h) return `${h}:${String(m).padStart(2,"0")}:${String(s).padStart(2,"0")}`;

            return `${m}:${String(s).padStart(2,"0")}`;
        }
    }

	class KeybindManager {
		constructor(theatre) {
			this.theatre = theatre;
			this.uriOverlay = null;
			this.uriInput = null;
		}

		initialize() {
			this.createUriOverlay();

			window.addEventListener("keydown", e => {
				console.log("[PyonPix] KeyDown", {
					key: e.key,
					code: e.code,
					ctrl: e.ctrlKey,
					alt: e.altKey,
					shift: e.shiftKey
				});
				this.handleKeyDown(e);
			}, true);
		}

		handleKeyDown(e) {
			if(this.isTypingTarget(e.target)) return;
			if(e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
			if(!e.ctrlKey || e.altKey || e.shiftKey) return;

			const key = e.key.toLowerCase();
			switch(key) {
				case "f":
					e.preventDefault();
					e.stopImmediatePropagation();

					this.theatre.toggle();
					break;
				case "e":
					e.preventDefault();
					e.stopImmediatePropagation();
					
					this.toggleUriInput();
					break;
			}
		}

		isTypingTarget(target) {
			if(!target) return false;
			return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable;
		}

		createUriOverlay() {
			this.uriOverlay = document.createElement("div");
			Object.assign(this.uriOverlay.style, {
				position: "fixed",
				inset: "0",
				display: "none",
				zIndex: "2147483647",
				background: "rgba(0,0,0,0.5) ",
				alignItems: "flex-start",
				justifyContent: "center",
				paddingTop: "15%",
				pointerEvents: "auto"
			});

			this.uriInput = document.createElement("input");
			Object.assign(this.uriInput.style, {
				width: "500px",
				padding: "12px 16px",
				fontSize: "18px",
				borderRadius: "6px",
				border: "none",
				outline: "none",
				background: "#222",
				color: "white",
				boxShadow: "0 4px 20px rgba(0,0,0,.5) "
			});

			this.uriInput.placeholder = "Search Google or enter a URI";

			this.uriOverlay.appendChild(this.uriInput);
			document.body.appendChild(this.uriOverlay);

			this.uriOverlay.addEventListener("mousedown", e => {
				if(e.target === this.uriOverlay) {
					this.hideUriInput();
					e.preventDefault();
					e.stopPropagation();
				}
			}, true);
			
			this.uriInput.addEventListener("mousedown", e => {
				e.stopPropagation();
			});

			this.uriInput.addEventListener("keydown", e => {
				e.stopPropagation();

				if(e.key === "Enter") {
					const uri = this.uriInput.value.trim();

					if(uri) {
						chrome.webview.postMessage({
							Type: "Navigate",
							Payload: {
								Uri: uri
							}
						});
					}

					this.hideUriInput();
				}
			}, true);
		}

		toggleUriInput() {
			if(this.uriOverlay.style.display === "flex")
				this.hideUriInput();
			else
				this.showUriInput();
		}

		showUriInput() {
			this.uriOverlay.style.display = "flex";
			this.uriInput.value = location.href;

			setTimeout(() => {
				this.uriInput.focus();
				this.uriInput.select();
			}, 0);
		}

		hideUriInput() {
			this.uriOverlay.style.display = "none";
			this.uriInput.blur();
		}
	}

	class SyncAction {
		static Play = 0;
		static Pause = 1;
	}
	
	class SyncController {
		constructor(mediaController) {
			this.mediaController = mediaController;
			this.suppressEvents = false;
			this.unbindEvents = null;

			this.mediaController.onMediaReady(media => {
				this.onMediaReady(media);
			});

			this.mediaController.onMediaChanged((oldMedia, newMedia) => {
				if(this.unbindEvents) this.unbindEvents();
				if (!newMedia) return;
				
				const onPlay = () => this.onPlay(newMedia);
				const onPause = () => this.onPause(newMedia);
				const onSeek = () => this.onSeek(newMedia);
				
				newMedia.addEventListener("play", onPlay);
				newMedia.addEventListener("pause", onPause);
				newMedia.addEventListener("seeked", onSeek);

				this.unbindEvents = () => {
					newMedia.removeEventListener("play", onPlay);
					newMedia.removeEventListener("pause", onPause);
					newMedia.removeEventListener("seeked", onSeek);
				};
			});
		}
		
		onMediaReady(media) {
			if(!Number.isFinite(media.duration)) return;
			
			console.log("[PyonPix] Sending MediaReady", {
				src: media.currentSrc,
				time: media.currentTime,
				duration: media.duration,
				paused: media.paused
			});
			
			setTimeout(() => {
				chrome.webview.postMessage({
					Type: "MediaReady",
					Payload: {
						Action: media.paused ? SyncAction.Pause : SyncAction.Play,
						IsPlaying: !media.paused,
						SeekTime: Math.floor(media.currentTime * 1000),
						Duration: Math.floor(media.duration * 1000),
						Timestamp: Date.now()
					}
				});
			}, 0);
		}
		
		onPlay(media) {
			this.sendState(media, SyncAction.Play);
		}

		onPause(media) {
			this.sendState(media, SyncAction.Pause);
		}

		onSeek(media) {
			this.sendState(media, media.paused ? SyncAction.Pause : SyncAction.Play);
		}
		
		sendState(media, action) {
			if(this.suppressEvents) return;
			if(media.readyState < HTMLMediaElement.HAVE_METADATA) return;
			if(!Number.isFinite(media.duration)) return;

			chrome.webview.postMessage({
				Type: "MediaState",
				Payload: {
					Action: action,
					IsPlaying: !media.paused,
					SeekTime: Math.floor(media.currentTime * 1000),
					Duration: Math.floor(media.duration * 1000),
					Timestamp: Date.now()
				}
			});
		}
		
		applyUpdate(update) {
			if(!update) return false;
			const media = this.mediaController.media;
			if (!media) return false;
			if (media.readyState < HTMLMediaElement.HAVE_METADATA) return false;

			this.suppressEvents = true;

			try {
				let seekTime = update.SeekTime;

				if (update.IsPlaying)
					seekTime += Math.max(0, Date.now() - update.Timestamp);

				media.currentTime = seekTime / 1000;

				if (update.IsPlaying)
					media.play().catch(() => {});
				else
					media.pause();

				return true;
			}
			finally {
				setTimeout(() => {
					this.suppressEvents = false;
				}, 3000);
			}
		}
	}

    class PyonPixMedia {
        constructor() {
			this.mediaManager = new MediaManager();
			this.mediaController = new MediaController(this.mediaManager);
			this.sync = new SyncController(this.mediaController);
			this.theatre = new Theatre(this.mediaController);
			this.keybinds = new KeybindManager(this.theatre);
			this.adapter = new SiteAdapter();
        }
		
		initialize() {
			this.waitForDocument(() => {
				this.waitForBody(() => {
					this.keybinds.initialize();
					this.theatre.initialize();
					this.mediaManager.initialize();
					this.updateLoop();
				});
			});
		}
		
		waitForDocument(callback) {
			if(document.documentElement) {
				callback();
				return;
			}

			requestAnimationFrame(() => {
				this.waitForDocument(callback);
			});
		}
		
		waitForBody(callback) {
			if(document.body) {
				callback();
				return;
			}

			requestAnimationFrame(() => {
				this.waitForBody(callback);
			});
		}
		
		updateLoop() {
			this.theatre.update();
			requestAnimationFrame(() => this.updateLoop());
		}
    }

    window.PyonPixMedia = new PyonPixMedia();
    window.PyonPixMedia.initialize();
})();
)";

    WebView->AddScriptToExecuteOnDocumentCreated(script, wrl::Callback<ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler>([this](HRESULT hr, LPCWSTR scriptId) -> HRESULT {
        if(FAILED(hr)) {
            LOGERROR(L"[%s] MediaState script injection failed.", TabId.c_str());
            return S_OK;
        }
        return S_OK;
    }).Get());
}

std::wstring BrowserTab::EscapeJson(const std::wstring& value) {
    std::wstring result;
    result.reserve(value.size());

    for(wchar_t c : value) {
        switch(c) {
            case L'\"':
                result += L"\\\"";
                break;
            case L'\\':
                result += L"\\\\";
                break;
            case L'\b':
                result += L"\\b";
                break;
            case L'\f':
                result += L"\\f";
                break;
            case L'\n':
                result += L"\\n";
                break;
            case L'\r':
                result += L"\\r";
                break;
            case L'\t':
                result += L"\\t";
                break;
            default:
                if(c < 0x20) {
                    wchar_t buffer[7];
                    swprintf(buffer, 7, L"\\u%04x", (unsigned int)c);
                    result += buffer;
                } else {
                    result += c;
                }
                break;
        }
    }

    return result;
}

WebErrorInfo BrowserTab::GetWebErrorInfoFromStatus(COREWEBVIEW2_WEB_ERROR_STATUS status, BOOL isSuccess) {
    if(isSuccess) { return { 200, L"SUCCESS", L"", L"", L"" }; }
    switch(status) {
        case COREWEBVIEW2_WEB_ERROR_STATUS_VALID_AUTHENTICATION_CREDENTIALS_REQUIRED:
            return { 401, L"AUTHENTICATION_CREDENTIALS_REQUIRED", L"Unauthorized", L"Authentication Required", L"Valid authentication credentials are required to access this resource." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_HOST_NAME_NOT_RESOLVED:
            return { 404, L"HOST_NAME_NOT_RESOLVED", L"Not Found", L"Host Not Found", L"The server hostname could not be resolved." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_VALID_PROXY_AUTHENTICATION_REQUIRED:
            return { 407, L"PROXY_AUTHENTICATION_REQUIRED", L"Unauthorized", L"Proxy Authentication Required", L"Valid authentication credentials are required for the proxy." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_DISCONNECTED:
            return { 444, L"DISCONNECTED", L"No Response", L"Connection Lost", L"The client device is not connected to a network." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_IS_INVALID:
            return { 494, L"CERTIFICATE_IS_INVALID", L"SSL Certificate Invalid", L"Invalid Security Certificate", L"The server provided an invalid or malformed SSL certificate." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_COMMON_NAME_IS_INCORRECT:
            return { 495, L"CERTIFICATE_COMMON_NAME_IS_INCORRECT", L"SSL Certificate Error", L"Certificate name mismatch", L"The SSL certificate provided by the server does not match the requested domain." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_EXPIRED:
            return { 496, L"CERTIFICATE_EXPIRED", L"SSL Certificate Expired", L"Certificate has expired", L"The server provided an expired SSL certificate." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CLIENT_CERTIFICATE_CONTAINS_ERRORS:
            return { 497, L"CLIENT_CERTIFICATE_CONTAINS_ERRORS", L"Client Certificate Invalid", L"Client certificate error", L"The client certificate contains errors and cannot be used." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_REVOKED:
            return { 498, L"CERTIFICATE_REVOKED", L"SSL Certificate Revoked", L"SSL certificate has been revoked", L"The server provided an SSL certificate that has been revoked by the issuer." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CONNECTION_ABORTED:
            return { 499, L"CONNECTION_ABORTED", L"Connection Aborted", L"Client Aborted Request", L"The request to the server was aborted before completion." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_OPERATION_CANCELED:
            return { 499, L"OPERATION_CANCELED", L"Connection Canceled", L"Client Cancelled Request", L"The request to the server was canceled before completion." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CANNOT_CONNECT:
            return { 502, L"CANNOT_CONNECT", L"Bad Gateway", L"Connection Failed", L"The client could not establish a connection to the server." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_ERROR_HTTP_INVALID_SERVER_RESPONSE:
            return { 502, L"HTTP_INVALID_SERVER_RESPONSE", L"Bad Gateway", L"Invalid Server Response", L"The server return an invalid or malformed response." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_CONNECTION_RESET:
            return { 502, L"CONNECTION_RESET", L"Bad Gateway", L"Connection Reset", L"The connection was forcibly closed by the remote host." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_SERVER_UNREACHABLE:
            return { 503, L"SERVER_UNREACHABLE", L"Service Unavailable", L"Server Unreachable", L"Failed to connect to the server, it may currently be unavailable." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_TIMEOUT:
            return { 504, L"CONNECTION_TIMED_OUT", L"Gateway Timeout", L"Request Timed Out", L"The server took too long to respond." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_REDIRECT_FAILED:
            return { 508, L"REDIRECT_FAILED", L"Redirect Failed", L"Redirect Failed", L"The request failed due to an invalid redirect." };
        case COREWEBVIEW2_WEB_ERROR_STATUS_UNEXPECTED_ERROR:
            return { 520, L"UNEXPECTED_ERROR", L"Unexpected Error", L"Unexpected Error", L"The client encountered an unexpected internal error." };
        default:
            return { 521, L"UNHANDLED_ERROR", L"Unhandled Error", L"Unhandled Error", L"The client encountered an unhandled internal error." };
    }
}
WebErrorInfo BrowserTab::GetWebErrorInfoFromHttpCode(uint32_t httpCode) {
    switch(httpCode) {
        case 200:
            return { httpCode, L"SUCCESS", L"", L"", L"" };
        case 400:
            return { httpCode, L"BAD_REQUEST", L"Bad Request", L"Bad Request", L"The client sent an invalid request to the server which could not be processed." };
        case 401:
            return { httpCode, L"UNAUTHORIZED", L"Unauthorized", L"Authentication Required", L"Valid authentication credentials are required to access this resource." };
        case 403:
            return { httpCode, L"FORBIDDEN", L"Forbidden", L"Access Denied", L"The client does not have permission to access this resource." };
        case 404:
            return { httpCode, L"NOT_FOUND", L"Not Found", L"Resource Not Found", L"The requested resource could not be found." };
        case 405:
            return { httpCode, L"METHOD_NOT_ALLOWED", L"Method Not Allowed", L"Method Not Allowed", L"The request method is not supported by the target resource." };
        case 406:
            return { httpCode, L"NOT_ACCEPTABLE", L"Not Acceptable", L"Not Acceptable", L"The server failed to provide content meeting the user agent criteria." };
        case 407:
            return { httpCode, L"PROXY_AUTHENTICATION_REQUIRED", L"Unauthorized", L"Proxy Authentication Required", L"Valid authentication credentials are required for the proxy." };
        case 408:
            return { httpCode, L"REQUEST_TIMEOUT", L"Request Timeout", L"Request Timed Out", L"The server closed the connection due to timeout." };
        case 409:
            return { httpCode, L"CONFLICT", L"Conflict", L"Request Conflict", L"The request conflicts with the current state of the target resource." };
        case 410:
            return { httpCode, L"GONE", L"Gone", L"Resource Removed", L"The requested resource is no longer available." };
        case 411:
            return { httpCode, L"LENGTH_REQUIRED", L"Length Required", L"Length Required", L"The server rejected the request because the Content-Length header is undefined." };
        case 412:
            return { httpCode, L"PRECONDITION_FAILED", L"Precondition Failed", L"Precondition Failed", L"The request was denied due to precondition header mismatch." };
        case 413:
            return { httpCode, L"CONTENT_TOO_LARGE", L"Content Too Large", L"Content Too Large", L"The length of the request body exceeds the server defined limit." };
        case 414:
            return { httpCode, L"URI_TOO_LONG", L"URI Too Long", L"URI Too Long", L"The length of the URI exceeds the server defined limit." };
        case 415:
            return { httpCode, L"UNSUPPORTED_MEDIA_TYPE", L"Unsupported Media Type", L"Unsupported Media Type", L"The requested media format is not supported by the server." };
        case 416:
            return { httpCode, L"RANGE_NOT_SATISFIABLE", L"Range Not Satisfiable", L"Range Not Satisfiable", L"The ranges specified by the Range header cannot be fulfilled." };
        case 417:
            return { httpCode, L"EXPECTATION_FAILED", L"Expectation Failed", L"Expectation Failed", L"The server failed to meet the expectations of the Expect header." };
        case 418:
            return { httpCode, L"PYON", L"Pyon", L"Pyon", L"Pyon pyon!" };
        case 421:
            return { httpCode, L"MISDIRECTED_REQUEST", L"Misdirected Request", L"Misdirected Request", L"The server is not configured to produce responses for this request." };
        case 422:
            return { httpCode, L"UNPROCESSABLE_CONTENT", L"Unprocessable Content", L"Unprocessable Content", L"The request failed due to semantic errors." };
        case 423:
            return { httpCode, L"LOCKED", L"Locked", L"Locked", L"The requested resource is locked." };
        case 424:
            return { httpCode, L"FAILED_DEPENDENCY", L"Failed Dependency", L"Failed Dependency", L"The request failed due to failure of a previous request." };
        case 425:
            return { httpCode, L"TOO_EARLY", L"Too Early", L"Too Early", L"The server refused to process the repeated request while the previous is still being processed." };
        case 426:
            return { httpCode, L"UPGRADE_REQUIRED", L"Upgrade Required", L"Upgrade Required", L"The server refused to process the request with the current protocol." };
        case 428:
            return { httpCode, L"PRECONDITION_REQUIRED", L"Precondition Required", L"Precondition Required", L"The request was denied due to missing precondition header." };
        case 429:
            return { httpCode, L"TOO_MANY_REQUESTS", L"Too Many Requests", L"Rate Limited", L"The client has sent too many requests, exceeding the server's rate limit." };
        case 431:
            return { httpCode, L"REQUEST_HEADER_FIELDS_TOO_LARGE", L"Request Header Fields Too Large", L"Request Header Fields Too Large", L"The length of the request header exceeds the server defined limit." };
        case 451:
            return { httpCode, L"UNAVAILABLE_FOR_LEGAL_REASONS", L"Unavailable For Legal Reasons", L"Unavailable For Legal Reasons", L"The client requested a resource that cannot legally be provided." };
        case 500:
            return { httpCode, L"INTERNAL_SERVER_ERROR", L"Internal Server Error", L"Internal Server Error", L"The server encountered an internal error which could not be handled." };
        case 501:
            return { httpCode, L"NOT_IMPLEMENTED", L"Not Implemented", L"Not Implemented", L"The request method is not supported by the server." };
        case 502:
            return { httpCode, L"BAD_GATEWAY", L"Bad Gateway", L"Bad Gateway", L"The server received an invalid response from the upstream server." };
        case 503:
            return { httpCode, L"SERVICE_UNAVAILABLE", L"Service Unavailable", L"Server Unavailable", L"The server is currently unable to handle the request." };
        case 504:
            return { httpCode, L"GATEWAY_TIMEOUT", L"Gateway Timeout", L"Gateway Timed Out", L"The upstream server failed to respond in time." };
        case 505:
            return { httpCode, L"HTTP_VERSION_NOT_SUPPORTED", L"HTTP Version Not Supported", L"HTTP Version Not Supported", L"The HTTP version used in the request is not supported by the server." };
        case 506:
            return { httpCode, L"VARIANT_ALSO_NEGOTIATES", L"Variant Also Negotiates", L"Variant Also Negotiates", L"The server has an internal configuration error resulting in circular referencing." };
        case 507:
            return { httpCode, L"INSUFFICIENT_STORAGE", L"Insufficient Storage", L"Insufficient Storage", L"The server is unable to complete the request due to insufficient resources." };
        case 508:
            return { httpCode, L"LOOP_DETECTED", L"Loop Detected", L"Loop Detected", L"The server detected an infinite loop while processing the request." };
        case 510:
            return { httpCode, L"NOT_EXTENDED", L"Not Extended", L"Not Extended", L"The server does not support the HTTP Extension declared in the request." };
        case 511:
            return { httpCode, L"NETWORK_AUTHENTICATION_REQUIRED", L"Network Authentication Required", L"Network Authentication Required", L"The client requires authentication to access the network." };
        default: {
            return { httpCode, L"UNHANDLED_ERROR", L"Unhandled Error", L"Unhandled Error", L"The client encountered an unhandled HTTP error." };
        }
    }
}

std::wstring BrowserTab::BuildErrorPage(WebErrorInfo error, LPCWSTR uri) {
    std::wstringstream ss;
    ss << LR"(
<!DOCTYPE html>
<head>
    <title>)"; ss << error.HttpCode; ss << L" "; ss << error.Title; ss << LR"(</title>
    <style>
    :root {
        --bg: #00000000;
        --card-bg: #00000047;
        --details-bg: #00000057;
        --main-text: #af85d7;
        --sub-text: #675879;
        --errcode-text: #b587ff;
        --button: #3f3f3f4f;
        --button-hover: #6969694f;
    }
    html, body {
        margin: 0;
        padding: 0;
        width: 100%;
        height: 100%;
        background: var(--bg);
        color: var(--main-text);
        font-family: system-ui, -apple-system, Segoe UI, sans-serif;
        user-select: none;
    }
    .container {
        display: flex;
        align-items: center;
        justify-content: center;
        height: 100%;
    }
    .card {
        background: var(--card-bg);
        border-radius: 4px;
        padding: 28px 32px;
        width: 520px;
        box-shadow: 0 10px 30px rgba(0,0,0,0.35);
    }
    .title {
        font-size: 20px;
        margin-bottom: 8px;
    }
    .subtitle {
        color: var(--sub-text);
        font-size: 14px;
        margin-bottom: 18px;
    }
    .errcode {
        font-size: 42px;
        font-weight: 600;
        color: var(--errcode-text);
        margin-bottom: 6px;
    }
    .details {
        background: var(--details-bg);
        border-radius: 4px;
        padding: 12px;
        font-size: 13px;
        color: var(--sub-text);
        margin-bottom: 18px;
        line-height: 1.4;
    }
    .actions { display: flex; gap: 10px; justify-content: flex-end; }
    .retry { background: var(--button); border: none; color: var(--main-text); padding: 8px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; }
    .retry:hover { background: var(--button-hover); }
    .retry:active { background: var(--button); }
    a { text-decoration: none; }
    </style>
</head>
<body>
    <div class="container">
        <div class="card">
            <div class="errcode">ERROR )"; ss << error.HttpCode; ss << LR"(</div>
            <div class="title">)"; ss << error.Header; ss << LR"(</div>
            <div class="subtitle">)"; ss << error.Description; ss << LR"(</div>
            <div class="details">
                <strong>URL:</strong> )"; ss << uri;
    ss << LR"(<br><br>
                <strong>STATUS:</strong> )"; ss << error.StatusCode;
    ss << LR"(</div>
            <div class="actions">
                <a class="retry" href=")"; ss << uri; ss << LR"(">Retry</a>
            </div>
        </div>
    </div>
</body>
</html>
)";
    return ss.str();
}

std::wstring BrowserTab::BuildBlankPage() {
    std::wstringstream ss;
    ss << LR"(
<!DOCTYPE html>
<html>
<head>
<title>)"; ss << TabId; ss << LR"(</title>
<style>
html,body{
    margin:0;
    height:100%;
    overflow:hidden;
    background:linear-gradient(180deg,#05060aaa,#160b18aa);
    user-select: none;
}
</style>
</head>
<body>
<script>
</body>
</html>
)";
    return ss.str();
}

std::wstring BrowserTab::BuildStarryPage() {
    std::wstringstream ss;
    ss << LR"(
<!DOCTYPE html>
<html>
<head>
<title>)"; ss << TabId; ss << LR"(</title>
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css">
<style>
html, body{
    margin:0;
    height:100%;
    overflow:hidden;
    background:linear-gradient(180deg,#05060aaa,#160b18aa);
    user-select: none;
}
#dust{
    position:fixed;
    inset:0;
    z-index:1;
}
#starfield{
    position:fixed;
    inset:0;
    z-index:2;
    pointer-events:none;
}
.star-wrap{
    position:absolute;
    will-change:transform;
}
.star{
    display:inline-block;
    transform-origin:center;
    transition:opacity 160ms linear,color 200ms linear,filter 160ms linear;
}
.star.off{
    opacity:0.12;
    color:#bfbfbf !important;
    filter:none !important;
}
</style>
</head>
<body>
<canvas id="dust"></canvas>
<div id="starfield"></div>
<script>
const Config = {
    areaDivisor: 5000,
    minStars: 40,
    maxStars: 80,
    sizeMin: 1,
    sizeMax: 36,
    colors: ["#FFD166","#FF8A00","#F72585","#9B5DE5","#4CC9F0","#06D6A0"],
    dustColors: ["#ff2e63","#c77dff","#9d4edd","#ff006e","#a4133c"],
    driftBaseX: -11,
    driftBaseY: -11,
    speedDepthMultiplier: 1.5
};

const rand = (a,b)=>Math.random()*(b-a)+a;
const pick = a=>a[Math.floor(Math.random()*a.length)];
const clamp = (v,a,b)=>Math.max(a,Math.min(b,v));
const starfield = document.getElementById("starfield");
const dust = document.getElementById("dust");
const ctx = dust.getContext("2d");

let w = innerWidth;
let h = innerHeight;
let stars = [];
let last = performance.now();

function buildDust(){
    dust.width = w;
    dust.height = h;
    ctx.clearRect(0,0,w,h);

    const count = clamp((w*h)/25000,40,800);
    for(let i=0;i<count;i++){
        const x = Math.random()*w;
        const y = Math.random()*h;
        const size = rand(0.5,2.2);
        const color = pick(Config.dustColors);
        const g = ctx.createRadialGradient(x,y,0,x,y,size*4);
        g.addColorStop(0,color+"44");
        g.addColorStop(1,"transparent");
        ctx.fillStyle = g;
        ctx.beginPath();
        ctx.arc(x,y,size*4,0,Math.PI*2);
        ctx.fill();
    }
}

function createStar(x,y,size,vx,vy){
    const radius = size * 0.55;
    const glowRadius = size * 1.6;
    const effectiveRadius = radius + glowRadius;
    const depth = (size - Config.sizeMin) / (Config.sizeMax - Config.sizeMin);
    const wrap = document.createElement("div");
    wrap.className = "star-wrap";
    const el = document.createElement("i");
    el.className = "fa-solid fa-star star";
    el.style.fontSize = size+"px";
    const color = pick(Config.colors);
    el.style.color = color;
    el.style.filter = `drop-shadow(0 0 ${size*0.8}px ${color}) drop-shadow(0 0 ${size*1.6}px ${color})`;
    wrap.appendChild(el);
    const star = { x, y, vx, vy, radius, effectiveRadius, rot: rand(-45,45), rotVel: rand(-15,15)*(0.2+depth), wrap, el };
    wrap.style.transform = `translate(${x-radius}px,${y-radius}px) rotate(${star.rot}deg)`;
    starfield.appendChild(wrap);
    stars.push(star);
    startTwinkle(el);
}

function spawnInitial(){
    const size = rand(Config.sizeMin,Config.sizeMax);
    const depth = (size - Config.sizeMin) / (Config.sizeMax - Config.sizeMin);
    const speedScale = 0.4 + depth*Config.speedDepthMultiplier;
    const vx = Config.driftBaseX*speedScale;
    const vy = Config.driftBaseY*speedScale;
    const x = rand(0,w);
    const y = rand(0,h);
    createStar(x,y,size,vx,vy);
}

function spawnFromEdge(){
    const size = rand(Config.sizeMin,Config.sizeMax);
    const depth = (size - Config.sizeMin) / (Config.sizeMax - Config.sizeMin);
    const speedScale = 0.4 + depth*Config.speedDepthMultiplier;
    const vx = Config.driftBaseX*speedScale;
    const vy = Config.driftBaseY*speedScale;
    const radius = size * 0.55;
    const glowRadius = size * 1.6;
    const effectiveRadius = radius + glowRadius;
    const edges = [];
    if(Config.driftBaseX < 0) edges.push("right");
    if(Config.driftBaseX > 0) edges.push("left");
    if(Config.driftBaseY < 0) edges.push("bottom");
    if(Config.driftBaseY > 0) edges.push("top");
    if(edges.length === 0) edges.push("bottom");
    const edge = pick(edges);
	let x = edge === "right" ? w + effectiveRadius : edge === "left" ? -effectiveRadius : edge === "bottom" ? rand(0,w) : rand(0,w);
	let y = edge === "right" ? rand(0,h) : edge === "left" ? rand(0,h) : edge === "bottom" ? h + effectiveRadius : -effectiveRadius;
    createStar(x,y,size,vx,vy);
}

function startTwinkle(el){
    function cycle(){
        setTimeout(()=>{
            el.classList.add("off");
            setTimeout(()=>{
                el.classList.remove("off");
                cycle();
            },rand(400,1200));
        },rand(900,3000));
    }
    cycle();
}

function buildStars(){
    stars = [];
    starfield.innerHTML = "";
    const total = clamp(Math.floor((w*h)/Config.areaDivisor), Config.minStars, Config.maxStars);
    for(let i=0;i<total;i++){
        spawnInitial();
    }
}

function animate(t){
    const dt = (t-last)/1000;
    last = t;
    for(let i=stars.length-1;i>=0;i--){
        const s = stars[i];
        s.x += s.vx*dt;
        s.y += s.vy*dt;
        s.rot += s.rotVel*dt;
        s.wrap.style.transform = `translate(${s.x-s.radius}px,${s.y-s.radius}px) rotate(${s.rot}deg)`;
        if(s.x < -s.effectiveRadius || s.x > w + s.effectiveRadius || s.y < -s.effectiveRadius || s.y > h + s.effectiveRadius){
            s.wrap.remove();
            stars.splice(i,1);
            spawnFromEdge();
        }
    }
    requestAnimationFrame(animate);
}

function rebuild(){
    w = innerWidth;
    h = innerHeight;
    buildDust();
    buildStars();
    last = performance.now();
}

addEventListener("resize",()=>setTimeout(rebuild,200));

rebuild();
requestAnimationFrame(animate);
</script>
</body>
</html>
)";
    return ss.str();
}