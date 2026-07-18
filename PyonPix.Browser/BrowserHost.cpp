#pragma once
#include "BrowserHost.h"
#include "BrowserTab.h"
#include "Globals.h"
#include "WebView2/Include/WebView2.h"
#include <DispatcherQueue.h>
#include <WebView2EnvironmentOptions.h>
#include <winrt/Windows.System.h>
#include <wrl.h>
#include <filesystem>
#include <shlwapi.h>
#include <winrt/Windows.Foundation.h>

namespace fs = std::filesystem;

BrowserHost::BrowserHost() {}

BrowserHost::~BrowserHost() {
    Shutdown();
}

bool BrowserHost::Initialize() {
    DispatcherQueueOptions dqOpts { sizeof(DispatcherQueueOptions), DQTYPE_THREAD_CURRENT, DQTAT_COM_STA };
    if(OnHostFailed(CreateDispatcherQueueController(dqOpts, reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(winrt::put_abi(WinRTDispatcher))), L"Dispatcher Creation Failed")) return false;

    AudioManager = new AudioSessionManager();
    if(OnHostFailed(!AudioManager->Initialize() ? E_FAIL : S_OK, L"AudioManager Initialization Failed")) {
        safe_release(AudioManager);
        return false;
    }

    std::wstring args =
        L"--enable-features=msWebView2EnableDrm "
        L"--disable-blink-features=AutomationControlled "
        L"--autoplay-policy=no-user-gesture-required "
        L"--disable-gpu-sandbox "
        L"--disable_vp_auto_hdr ";

    wrl::ComPtr<ICoreWebView2EnvironmentOptions> envOptions;
    if(OnHostFailed(MakeAndInitialize<CoreWebView2EnvironmentOptions>(&envOptions), L"EnvironmentOptions Creation Failed")) return false;
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

    std::wstring persistentUDF = BuildPersistentUDF();
    HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, persistentUDF.c_str(), envOptions.Get(), wrl::Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>([this](HRESULT result, ICoreWebView2Environment* env) -> HRESULT {
        if(IsShuttingDown) return E_FAIL;
        
        if(OnHostFailed(!env ? E_FAIL : FAILED(result), L"Environment Creation Failed")) return result;

        wrl::ComPtr<ICoreWebView2Environment> baseEnv(env);
        if(OnHostFailed(baseEnv.As(&Environment), L"Environment QI Failed")) return E_FAIL;

        return CreateController() ? S_OK : E_FAIL;
    }).Get());

    return SUCCEEDED(hr);
}

bool BrowserHost::CreateController() {
    HRESULT hr = Environment->CreateCoreWebView2CompositionController(Browser.GameHwnd, wrl::Callback<ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler>([this](HRESULT result, ICoreWebView2CompositionController* comp) -> HRESULT {
        if(IsShuttingDown) return E_FAIL;
        
        if(OnHostFailed(!comp ? E_FAIL : FAILED(result), L"CompositionController Failed")) return result;

        wrl::ComPtr<ICoreWebView2Controller> baseCtrl;
        if(OnHostFailed(comp->QueryInterface(IID_PPV_ARGS(baseCtrl.GetAddressOf())), L"Controller QI Failed")) return E_FAIL;
        baseCtrl.As(&Controller);

        wrl::ComPtr<ICoreWebView2> baseWebView;
        if(OnHostFailed(Controller->get_CoreWebView2(baseWebView.GetAddressOf()), L"WebView QI Failed")) return E_FAIL;
        baseWebView.As(&WebView);

        wrl::ComPtr<ICoreWebView2Settings> baseSettings;
        if(OnHostFailed(WebView->get_Settings(baseSettings.GetAddressOf()), L"WebView Settings Failed")) return E_FAIL;
        baseSettings.As(&WebViewSettings);

        wrl::ComPtr<ICoreWebView2Profile> baseProfile;
        if(OnHostFailed(WebView->get_Profile(baseProfile.GetAddressOf()), L"WebView Profile Failed")) return E_FAIL;
        baseProfile.As(&Profile);

        wrl::ComPtr<ICoreWebView2CookieManager> cookieManager;
        if(OnHostFailed(Profile->get_CookieManager(cookieManager.GetAddressOf()), L"CookieManager Failed")) return E_FAIL;
        CookieManager = cookieManager;

        Controller->put_ShouldDetectMonitorScaleChanges(FALSE);
        Controller->put_DefaultBackgroundColor({ 0, 0, 0, 0 });
        Controller->put_IsVisible(FALSE);

        WebViewSettings->put_IsPasswordAutosaveEnabled(TRUE);
        WebViewSettings->put_AreHostObjectsAllowed(FALSE);
        WebViewSettings->put_IsScriptEnabled(FALSE);
        WebViewSettings->put_IsWebMessageEnabled(FALSE);
        WebViewSettings->put_IsStatusBarEnabled(FALSE);
        WebViewSettings->put_IsBuiltInErrorPageEnabled(FALSE);
        WebViewSettings->put_AreDefaultContextMenusEnabled(FALSE);
        WebViewSettings->put_AreDefaultScriptDialogsEnabled(FALSE);
        WebViewSettings->put_AreDevToolsEnabled(FALSE);
        WebViewSettings->put_IsGeneralAutofillEnabled(FALSE);
        WebViewSettings->put_AreBrowserAcceleratorKeysEnabled(FALSE);
        WebViewSettings->put_IsZoomControlEnabled(FALSE);

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
            safe_callback(Browser.OnHostFailed, message.str().c_str());
            return S_OK;
        }).Get(), &WebViewProcessFailedEventToken);

        safe_callback(Browser.OnHostReady);

        return S_OK;
    }).Get());

    return SUCCEEDED(hr);
}

void BrowserHost::Shutdown() {
    std::lock_guard<std::mutex> lk(LifecycleMutex);
    if(IsShuttingDown) return;
    IsShuttingDown = true;

    std::vector<std::unique_ptr<BrowserTab>> list;
    {
        std::lock_guard<std::mutex> lk(TabsMutex);
        for(auto& kv : Tabs) list.push_back(std::move(kv.second));
        Tabs.clear();
        PendingFocusTabId.clear();
        CurrentFocusedTabId.clear();
    }

    for(auto& tabPtr : list) {
        try {
            if(tabPtr) tabPtr->Shutdown();
        } catch(...) {}
    }

    if(AudioManager) {
        AudioManager->Shutdown();
        delete AudioManager;
        AudioManager = nullptr;
    }

    if(WebView) {
        try {
            WebView->remove_ProcessFailed(WebViewProcessFailedEventToken);
        } catch(...) {}
    }

    if(Controller) {
        try { Controller->Close(); } catch(...) {}
    }

    CookieManager = nullptr;
    Profile = nullptr;
    WebViewSettings = nullptr;
    WebView = nullptr;
    Controller = nullptr;
    Environment = nullptr;

    if(WinRTDispatcher) {
        try {
            DispatcherQueueShutdownCompleted.store(false, std::memory_order_release);
            DispatcherQueueShutdownAction = WinRTDispatcher.ShutdownQueueAsync();
        } catch(...) {
            DispatcherQueueShutdownCompleted.store(true, std::memory_order_release);
        }
    } else {
        DispatcherQueueShutdownCompleted.store(true, std::memory_order_release);
    }
}

bool BrowserHost::IsDispatcherQueueShutdownComplete() const {
    if(DispatcherQueueShutdownCompleted.load(std::memory_order_acquire))
        return true;

    if(!DispatcherQueueShutdownAction)
        return false;

    winrt::Windows::Foundation::AsyncStatus status = DispatcherQueueShutdownAction.Status();
    if(status == winrt::Windows::Foundation::AsyncStatus::Completed ||
        status == winrt::Windows::Foundation::AsyncStatus::Error ||
        status == winrt::Windows::Foundation::AsyncStatus::Canceled) {
        DispatcherQueueShutdownCompleted.store(true, std::memory_order_release);
        return true;
    }

    return false;
}

void BrowserHost::EnqueueCommand(std::function<void()> fn) {
    ::EnqueueCommand(std::move(fn));
}

std::wstring BrowserHost::BuildPersistentUDF() const {
    std::wstring folder = GetUDFPath(L"PIX");
    if(!fs::exists(folder)) fs::create_directories(folder);
    return folder;
}

std::wstring BrowserHost::BuildTabUDF(const std::wstring& tabId) const {
    std::wstring folder = GetUDFPath(tabId);
    if(!fs::exists(folder)) fs::create_directories(folder);
    return folder;
}

bool BrowserHost::CreateTab(const wchar_t* tabId, bool gpuAcceleration, int32_t x, int32_t y, uint32_t w, uint32_t h, bool syncCookies, const std::vector<std::wstring>& installedExtensionIds) {
    if(!tabId) return false;
    std::wstring key(tabId);

    {
        std::lock_guard<std::mutex> lk(TabsMutex);
        if(Tabs.find(key) != Tabs.end()) return false;
    }

    std::wstring tabFolder = BuildTabUDF(key);

    if(!CurrentFocusedTabId.empty()) {
        PendingFocusTabId = key;
    }

    PendingExtensions = installedExtensionIds;

    auto tab = std::make_unique<BrowserTab>(key, this);
    tab->SyncCookies = syncCookies;
    if(!tab->Initialize(tabFolder, gpuAcceleration, x, y, w, h)) {
        safe_callback(Browser.OnTabFailed, key.c_str(), L"Initialization Failed");
        if(PendingFocusTabId == key) {
            PendingFocusTabId.clear();
        }
        return false;
    }

    {
        std::lock_guard<std::mutex> lk(TabsMutex);
        Tabs.try_emplace(key, std::move(tab));
    }

    return true;
}

void BrowserHost::DestroyTab(const wchar_t* tabId) {
    if(!tabId) return;
    std::wstring key(tabId);

    std::unique_ptr<BrowserTab> local;
    {
        std::lock_guard<std::mutex> lk(TabsMutex);
        auto it = Tabs.find(key);
        if(it == Tabs.end()) return;
        local = std::move(it->second);
        Tabs.erase(it);
        if(PendingFocusTabId == key) PendingFocusTabId.clear();
        if(CurrentFocusedTabId == key) CurrentFocusedTabId.clear();
    }

    if(local) {
        DWORD pid = local->GetBrowserProcessId();
        if(pid != 0 && AudioManager) AudioManager->UnregisterTabProcess(pid);

        try { local->Shutdown(); } catch(...) {}
    }
}

void BrowserHost::NotifyTabReady(const std::wstring& tabId) {
    bool doFocus = false;
    DWORD pid = 0;

    {
        std::lock_guard<std::mutex> lk(TabsMutex);
        auto it = Tabs.find(tabId);
        if(it == Tabs.end()) return;

        if(!PendingFocusTabId.empty() && PendingFocusTabId == tabId) {
            doFocus = true;
        }

        pid = it->second ? it->second->GetBrowserProcessId() : 0;
    }

    if(doFocus) SetFocusedTab(tabId, true);

    if(pid != 0 && AudioManager) AudioManager->RegisterTabProcess(pid);

    safe_callback(Browser.OnTabReady, tabId.c_str());
}

BrowserTab* BrowserHost::GetTab(const wchar_t* tabId) {
    std::wstring key(tabId);
    std::lock_guard<std::mutex> lk(TabsMutex);
    auto it = Tabs.find(key);
    return it == Tabs.end() ? nullptr : it->second.get();
}

BrowserTab* BrowserHost::GetFocusedTab() {
    if(CurrentFocusedTabId.empty()) { return nullptr; }
    std::lock_guard<std::mutex> lk(TabsMutex);
    auto it = Tabs.find(CurrentFocusedTabId);
    return it == Tabs.end() ? nullptr : it->second.get();
}

void BrowserHost::SetFocusedHost() {
    if(!Controller) return;
    Controller->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
    CurrentFocusedTabId.clear();
    PendingFocusTabId.clear();
}

void BrowserHost::SetFocusedTab(const std::wstring& tabId, bool byUserInput) {
    std::unique_lock<std::mutex> lk(TabsMutex);
    if(CurrentFocusedTabId == tabId) return;

    BrowserTab* newFocusTab = nullptr;
    if(!tabId.empty()) {
        auto it2 = Tabs.find(tabId);
        if(it2 != Tabs.end()) newFocusTab = it2->second.get();
    }

    if(newFocusTab) {
        if(byUserInput) {
            lk.unlock();
            newFocusTab->MoveFocusProgrammatic();
            lk.lock();
        }
        CurrentFocusedTabId = tabId;
    } else {
        CurrentFocusedTabId.clear();
    }

    if(PendingFocusTabId == tabId) PendingFocusTabId.clear();
}

void BrowserHost::UnsetFocusedTab() {
    std::unique_lock<std::mutex> lk(TabsMutex);

    BrowserTab* tab = GetFocusedTab();
    if(tab) {
        lk.unlock();
        tab->MoveFocusProgrammatic();
        lk.lock();
    }

    CurrentFocusedTabId.clear();
    PendingFocusTabId.clear();
}

void BrowserHost::PassFocusedTab(const std::wstring& tabId) {
    std::unique_lock<std::mutex> lk(TabsMutex);
    if(CurrentFocusedTabId != tabId) return;

    if(PendingFocusTabId == tabId) PendingFocusTabId.clear();

    BrowserTab* newFocusTab = nullptr;
    for(auto& kv : Tabs) {
        BrowserTab* t = kv.second.get();
        if(!t) continue;
        if(kv.first == tabId) continue;
        if(PendingFocusTabId == kv.first) PendingFocusTabId.clear();
        newFocusTab = kv.second.get();
        break;
    }

    if(newFocusTab) {
        lk.unlock();
        newFocusTab->MoveFocusProgrammatic();
        lk.lock();
        CurrentFocusedTabId = tabId;
    } else {
        CurrentFocusedTabId.clear();
    }
}

void BrowserHost::PollTabsAndEmitFrames() {
    std::lock_guard<std::mutex> lk(TabsMutex);
    for(auto& kv : Tabs) {
        BrowserTab* t = kv.second.get();
        if(!t) continue;
        if(t->HasFrame()) {
            HANDLE hwnd = t->GetSharedHandle();
            uint32_t w = t->GetFrameWidth();
            uint32_t h = t->GetFrameHeight();
            t->ConsumeFrame();
            if(hwnd) safe_callback(Browser.OnFrameReady, kv.first.c_str(), hwnd, w, h);
        }
    }
}

bool BrowserHost::ShouldSyncCookies(const std::wstring& tabId) {
    std::lock_guard<std::mutex> lk(TabsMutex);
    auto it = Tabs.find(tabId);
    return it != Tabs.end() && it->second->SyncCookies;
}

void BrowserHost::ImportCookiesFromHostToTab(const std::wstring& tabId, ICoreWebView2CookieManager* tabCookieManager) {
    if(!CookieManager || !tabCookieManager) return;
    if(!ShouldSyncCookies(tabId)) return;

    CookieManager->GetCookies(nullptr, wrl::Callback<ICoreWebView2GetCookiesCompletedHandler>([=](HRESULT result, ICoreWebView2CookieList* cookieList) -> HRESULT {
        if(IsShuttingDown) return S_OK;
        
        if(FAILED(result) || !cookieList) {
            LOGWARN(L"[Host > %s] GetCookies Failed: 0x%08X", tabId.c_str(), result);
            return S_OK;
        }

        UINT32 count = 0;
        if(FAILED(cookieList->get_Count(&count))) return S_OK;
        for(UINT32 i = 0; i < count; i++) {
            ICoreWebView2Cookie* cookie = nullptr;
            if(FAILED(cookieList->GetValueAtIndex(i, &cookie)) || !cookie) {
                LOGWARN(L"[Host > %s] Cookie Retrieval Failed", tabId.c_str());
                continue;
            }

            HRESULT ar = tabCookieManager->AddOrUpdateCookie(cookie);
            if(FAILED(ar)) {
                LOGWARN(L"[Host > %s] Cookie Addition Failed: 0x%08X", tabId.c_str(), ar);
            }

            cookie->Release();
        }
        return S_OK;
    }).Get());
}

void BrowserHost::ImportCookiesFromTabToHost(const std::wstring& tabId, ICoreWebView2CookieManager* tabCookieManager) {
    if(!CookieManager || !tabCookieManager) return;
    if(!ShouldSyncCookies(tabId)) return;

    tabCookieManager->GetCookies(nullptr, wrl::Callback<ICoreWebView2GetCookiesCompletedHandler>([=](HRESULT result, ICoreWebView2CookieList* cookieList) -> HRESULT {
        if(IsShuttingDown) return S_OK;

        if(FAILED(result) || !cookieList) {
            LOGWARN(L"[%s > Host] GetCookies Failed: 0x%08X", tabId.c_str(), result);
            return S_OK;
        }

        UINT32 count = 0;
        if(FAILED(cookieList->get_Count(&count))) return S_OK;
        for(UINT32 i = 0; i < count; ++i) {
            ICoreWebView2Cookie* cookie = nullptr;
            if(FAILED(cookieList->GetValueAtIndex(i, &cookie)) || !cookie) {
                LOGWARN(L"[%s > Host] Cookie Retrieval Failed", tabId.c_str());
                continue;
            }

            HRESULT a = CookieManager->AddOrUpdateCookie(cookie);
            if(FAILED(a)) {
                LOGWARN(L"[%s > Host] Cookie Addition Failed: 0x%08X", tabId.c_str(), a);
            }

            cookie->Release();
        }
        return S_OK;
    }).Get());
}

void BrowserHost::UpdateSpatialAudio(const std::wstring& tabId, float left, float right) {
    if(!AudioManager) return;
    BrowserTab* tab = GetTab(tabId.c_str());
    if(!tab) return;

    DWORD pid = tab->GetBrowserProcessId();
    if(pid != 0) AudioManager->SetSpatialVolume(pid, left, right);
}

void BrowserHost::StartExtensionOperation(const std::wstring& extensionId, const std::wstring& extensionName, ExtensionOperation op) {
    if(extensionId.empty() || extensionName.empty()) return;

    std::vector<std::wstring> tabsAsked;
    int started = 0;

    {
        std::lock_guard<std::mutex> lk(TabsMutex);
        for(auto& kv : Tabs) {
            BrowserTab* t = kv.second.get();
            if(!t) continue;

            bool initiated = false;
            switch(op) {
                case ExtensionOperation::Install:
                    initiated = t->InstallExtension(extensionId, extensionName);
                    break;
                case ExtensionOperation::Remove:
                    initiated = t->UninstallExtension(extensionId, extensionName);
                    break;
                case ExtensionOperation::Enable:
                    initiated = t->ToggleExtension(extensionId, extensionName, true);
                    break;
                case ExtensionOperation::Disable:
                    initiated = t->ToggleExtension(extensionId, extensionName, false);
                    break;
            }
            if(initiated) {
                ++started;
                tabsAsked.push_back(t->GetTabId());
            }
        }
    }

    if(started == 0) {
        safe_callback(Browser.OnExtensionOperation, (byte)op, extensionId.c_str());
        return;
    }

    {
        std::lock_guard<std::mutex> lk(PendingExtensionsMutex);
        PendingExtensionOps.try_emplace(extensionId, started, op, tabsAsked);
    }

    std::thread([this, extensionId]() {
        std::this_thread::sleep_for(std::chrono::milliseconds(10000));
        std::lock_guard<std::mutex> lk(PendingExtensionsMutex);
        auto it = PendingExtensionOps.find(extensionId);
        if(it != PendingExtensionOps.end()) {
            ExtensionOperation op = it->second.Op;
            PendingExtensionOps.erase(it);
            safe_callback(Browser.OnExtensionOperation, (byte)op, extensionId.c_str());
        }
    }).detach();
}

void BrowserHost::NotifyExtensionOperationResult(const std::wstring& extensionId, const std::wstring& tabId, bool success) {
    std::lock_guard<std::mutex> lk(PendingExtensionsMutex);
    auto it = PendingExtensionOps.find(extensionId);
    if(it == PendingExtensionOps.end()) return;

    int remaining = --(it->second.Remaining);

    if(remaining <= 0) {
        ExtensionOperation op = it->second.Op;
        PendingExtensionOps.erase(it);
        safe_callback(Browser.OnExtensionOperation, (byte)op, extensionId.c_str());
    }
}

bool BrowserHost::HasPendingExtensionOperation(const std::wstring& extensionId) {
    std::lock_guard<std::mutex> lk(PendingExtensionsMutex);
    return PendingExtensionOps.find(extensionId) != PendingExtensionOps.end();
}

void BrowserHost::RegisterTabPendingInstall(const std::wstring& tabId) {
    bool startTimer = false;
    {
        std::lock_guard<std::mutex> lk(PendingTabInstallsMutex);
        auto it = PendingTabInstalls.find(tabId);
        if(it == PendingTabInstalls.end()) {
            PendingTabInstalls.emplace(tabId, 1);
            startTimer = true;
        } else {
            it->second++;
        }
    }

    if(startTimer) {
        std::thread([this, tabId]() {
            std::this_thread::sleep_for(std::chrono::milliseconds(5000));
            bool shouldNotify = false;
            {
                std::lock_guard<std::mutex> lk(PendingTabInstallsMutex);
                auto it = PendingTabInstalls.find(tabId);
                if(it != PendingTabInstalls.end()) {
                    shouldNotify = true;
                    PendingTabInstalls.erase(it);
                }
            }
            if(shouldNotify) {
                this->NotifyTabReady(tabId);
            }
        }).detach();
    }
}

void BrowserHost::NotifyTabExtensionInstallResult(const std::wstring& extensionId, const std::wstring& tabId, bool success) {
    bool shouldNotify = false;
    {
        std::lock_guard<std::mutex> lk(PendingTabInstallsMutex);
        auto it = PendingTabInstalls.find(tabId);
        if(it == PendingTabInstalls.end()) return;
        it->second--;
        if(it->second <= 0) {
            PendingTabInstalls.erase(it);
            shouldNotify = true;
        }
    }

    if(shouldNotify) {
        LOGVERBOSE(L"[%s] Extensions Installed", tabId.c_str());
        NotifyTabReady(tabId);
    }
}
