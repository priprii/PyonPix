#pragma once
#include "Globals.h"
#include "AudioSessionManager.h"
#include <TlHelp32.h>
#include <iostream>

AudioSessionManager::AudioSessionManager() {}
AudioSessionManager::~AudioSessionManager() { Shutdown(); }

bool AudioSessionManager::Initialize() {
    LOGVERBOSE(L"Creating Audio Manager");

    if(OnHostFailed(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), (void**)&DeviceEnumerator), L"Audio Manager Failed")) return false;
    if(OnHostFailed(DeviceEnumerator->GetDefaultAudioEndpoint(eRender, eConsole, &Device), L"Audio Manager Failed")) return false;
    if(OnHostFailed(Device->Activate(__uuidof(IAudioSessionManager2), CLSCTX_ALL, nullptr, (void**)&SessionManager), L"Audio Manager Failed")) return false;
    if(OnHostFailed(SessionManager->RegisterSessionNotification(this), L"Audio Manager Failed")) return false;

    LOGVERBOSE(L"Audio Manager Initialized");
    return true;
}

void AudioSessionManager::Shutdown() {
    if(SessionManager)
        SessionManager->UnregisterSessionNotification(this);

    std::lock_guard<std::mutex> lk(BindingMutex);
    for(auto& [pid, binding] : Bindings) {
        safe_release(binding.SimpleVolume);
        safe_release(binding.ChannelVolume);
    }

    Bindings.clear();
    TabProcesses.clear();

    safe_release(SessionManager);
    safe_release(Device);
    safe_release(DeviceEnumerator);
}

void AudioSessionManager::SetSpatialVolume(DWORD tabPid, float left, float right) {
    std::lock_guard<std::mutex> lk(BindingMutex);
    auto it = Bindings.find(tabPid);
    if(it == Bindings.end()) return;

    auto& binding = it->second;
    if(binding.ChannelVolume) {
        UINT count = 0;
        binding.ChannelVolume->GetChannelCount(&count);
        if(count >= 2) {
            binding.ChannelVolume->SetChannelVolume(0, left, nullptr);
            binding.ChannelVolume->SetChannelVolume(1, right, nullptr);
        }
    } else if(binding.SimpleVolume) {
        float master = max(left, right);
        binding.SimpleVolume->SetMasterVolume(master, nullptr);
    }
}

void AudioSessionManager::RegisterTabProcess(DWORD tabPid) {
    {
        std::lock_guard<std::mutex> lk(BindingMutex);
        TabProcesses[tabPid] = true;
        LOGVERBOSE(L"Audio Process Registered (%u)", tabPid);
    }
    TryBindExistingSessionsForTab(tabPid);
}
void AudioSessionManager::UnregisterTabProcess(DWORD tabPid) {
    std::lock_guard<std::mutex> lk(BindingMutex);

    ClearBinding(tabPid);
    TabProcesses.erase(tabPid);
    LOGVERBOSE(L"Audio Process Unregistered (%u)", tabPid);
}
void AudioSessionManager::ClearBinding(DWORD tabPid) {
    auto it = Bindings.find(tabPid);
    if(it != Bindings.end()) {
        safe_release(it->second.SimpleVolume);
        safe_release(it->second.ChannelVolume);
        Bindings.erase(it);
    }
}

HRESULT STDMETHODCALLTYPE AudioSessionManager::OnSessionCreated(IAudioSessionControl* newSession) {
    if(!newSession) return S_OK;
    IAudioSessionControl2* ctrl2 = nullptr;
    if(FAILED(newSession->QueryInterface(__uuidof(IAudioSessionControl2), (void**)&ctrl2))) return S_OK;
    DWORD sessionPid = 0;
    ctrl2->GetProcessId(&sessionPid);
    {
        std::lock_guard<std::mutex> lk(BindingMutex);
        for(auto& [tabPid, _] : TabProcesses) {
            if(IsChildProcessOf(sessionPid, tabPid)) {
                ISimpleAudioVolume* simple = nullptr;
                IChannelAudioVolume* channel = nullptr;
                newSession->QueryInterface(__uuidof(ISimpleAudioVolume), (void**)&simple);
                newSession->QueryInterface(__uuidof(IChannelAudioVolume), (void**)&channel);

                if(simple || channel) {
                    ClearBinding(tabPid);
                    Bindings[tabPid] = { simple, channel };
                    LOGVERBOSE(L"Audio Session Bound (%u:%u - %s)", tabPid, sessionPid, channel ? "stereo" : "mono");
                } else {
                    safe_release(simple);
                    safe_release(channel);
                }
            }
        }
    }
    ctrl2->Release();
    return S_OK;
}

void AudioSessionManager::TryBindExistingSessionsForTab(DWORD tabPid) {
    if(!SessionManager) return;

    IAudioSessionEnumerator* enumerator = nullptr;
    if(FAILED(SessionManager->GetSessionEnumerator(&enumerator)) || !enumerator) {
        LOGWARN(L"Audio Manager GetSessionEnumerator Failed");
        return;
    }

    int count = 0;
    if(FAILED(enumerator->GetCount(&count))) {
        safe_release(enumerator);
        return;
    }

    for(int i = 0; i < count; ++i) {
        IAudioSessionControl* ctrl = nullptr;
        if(FAILED(enumerator->GetSession(i, &ctrl)) || !ctrl) continue;

        IAudioSessionControl2* ctrl2 = nullptr;
        if(FAILED(ctrl->QueryInterface(__uuidof(IAudioSessionControl2), (void**)&ctrl2))) {
            safe_release(ctrl);
            continue;
        }

        DWORD sessionPid = 0;
        ctrl2->GetProcessId(&sessionPid);

        if(IsChildProcessOf(sessionPid, tabPid)) {
            ISimpleAudioVolume* simple = nullptr;
            IChannelAudioVolume* channel = nullptr;
            ctrl->QueryInterface(__uuidof(ISimpleAudioVolume), (void**)&simple);
            ctrl->QueryInterface(__uuidof(IChannelAudioVolume), (void**)&channel);

            if(simple || channel) {
                std::lock_guard<std::mutex> lk(BindingMutex);
                ClearBinding(tabPid);
                Bindings[tabPid] = { simple, channel };
                LOGVERBOSE(L"Pre-existing Audio Session Bound (%u:%u - %s)", tabPid, sessionPid, channel ? "stereo" : "mono");
            } else {
                safe_release(simple);
                safe_release(channel);
            }
        }
        safe_release(ctrl2);
        safe_release(ctrl);
    }

    safe_release(enumerator);
}

bool AudioSessionManager::IsChildProcessOf(DWORD childPid, DWORD parentPid) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if(snapshot == INVALID_HANDLE_VALUE)
        return false;

    PROCESSENTRY32 entry;
    entry.dwSize = sizeof(PROCESSENTRY32);
    if(Process32First(snapshot, &entry)) {
        do {
            if(entry.th32ProcessID == childPid) {
                CloseHandle(snapshot);
                return entry.th32ParentProcessID == parentPid;
            }
        } while(Process32Next(snapshot, &entry));
    }

    CloseHandle(snapshot);
    return false;
}

ULONG STDMETHODCALLTYPE AudioSessionManager::AddRef() {
    return ++RefCount;
}

ULONG STDMETHODCALLTYPE AudioSessionManager::Release() {
    ULONG count = --RefCount;
    if(!count)
        delete this;
    return count;
}

HRESULT STDMETHODCALLTYPE AudioSessionManager::QueryInterface(REFIID riid, void** ppv) {
    if(riid == __uuidof(IUnknown) ||
        riid == __uuidof(IAudioSessionNotification)) {
        *ppv = static_cast<IAudioSessionNotification*>(this);
        AddRef();
        return S_OK;
    }

    *ppv = nullptr;
    return E_NOINTERFACE;
}
