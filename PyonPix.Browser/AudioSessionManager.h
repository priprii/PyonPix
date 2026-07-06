#pragma once
#include <Windows.h>
#include <mmdeviceapi.h>
#include <audiopolicy.h>
#include <atomic>
#include <unordered_map>
#include <mutex>

class AudioSessionManager : public IAudioSessionNotification {
public:
    AudioSessionManager();
    ~AudioSessionManager();

    bool Initialize();
    void Shutdown();

    void SetSpatialVolume(DWORD tabPid, float left, float right);

    void RegisterTabProcess(DWORD tabPid);
    void UnregisterTabProcess(DWORD tabPid);

    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override;

private:
    struct SessionBinding {
        ISimpleAudioVolume* SimpleVolume = nullptr;
        IChannelAudioVolume* ChannelVolume = nullptr;
    };

    void ClearBinding(DWORD tabPid);

    HRESULT STDMETHODCALLTYPE OnSessionCreated(IAudioSessionControl* newSession) override;
    void TryBindExistingSessionsForTab(DWORD tabPid);

    bool IsChildProcessOf(DWORD childPid, DWORD parentPid);

private:
    std::atomic<ULONG> RefCount = 1;

    IMMDeviceEnumerator* DeviceEnumerator = nullptr;
    IMMDevice* Device = nullptr;
    IAudioSessionManager2* SessionManager = nullptr;

    std::mutex BindingMutex;
    std::unordered_map<DWORD, SessionBinding> Bindings;
    std::unordered_map<DWORD, bool> TabProcesses;
};
