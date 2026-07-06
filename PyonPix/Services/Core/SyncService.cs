using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PyonPix.Config;
using PyonPix.Config.Pix;
using PyonPix.Events;
using PyonPix.Services.Game;
using PyonPix.Shared.Structs;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync;
using PyonPix.Shared.Sync.Dto;
using PyonPix.Shared.Sync.Dto.Auth;
using PyonPix.Shared.Sync.Dto.Client;
using PyonPix.Shared.Sync.Dto.Session;
using PyonPix.Shared.Sync.Dto.Subbed;
using PyonPix.Shared.Sync.Dto.Syncable;

namespace PyonPix.Services.Core;

public enum ConnectionState {
    Disconnected,
    Connecting,
    Connected
}
public enum ActionState {
    None,
    AuthRequired
}

public class ClientSession {
    public bool IsSecretKeyInvalid = false;
    public bool IsAuthenticated = false;
    public PremiumStatus Premium = new(false, false);
    public CharacterProperties Style = new();

    public string? AuthKey;
    public DateTime? AuthExpiration;
    public TimeSpan? AuthExpirationTime => AuthExpiration != null ? AuthExpiration - DateTime.UtcNow : null;
    public string GetAuthExpirationTime() => AuthExpirationTime != null ? $"{AuthExpirationTime.Value:mm\\:ss}" : string.Empty;
}
public class ServerSession {
    public uint UserCount;
    public uint PixCount;
}

public class SyncService(Configuration config, IServiceContext services) : BaseService(config, services) {
    private StateService StateService => Services.Get<StateService>();
    private PixService PixService => Services.Get<PixService>();

    private ClientWebSocket? Socket;
    private CancellationTokenSource? ConnectionCts;
    private Task? ReceiveLoopTask;
    private readonly SemaphoreSlim ConnectionLock = new(1, 1);
    private readonly Random RNG = new();
    private const int MaxConnectionAttempts = 1000;

    private readonly object SyncablePixsLock = new();
    public List<SyncablePixQueryItemDto> SyncablePixs = [];

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? StatusMessage { get; private set; }

    public ServerSession Server { get; private set; } = new();
    public ClientSession Client { get; private set; } = new();

    public event Action<ConnectionState, string?, StatusType>? StateChanged;
    public event Action? AuthKeyReceived;
    public event Action<bool>? StyleUpdateResponse;
    public event Action? SyncablePixsUpdated;
    public event Action<string>? SubscriptionFailed;
    public event Action<LocalPix, SyncedPix>? SyncedPixCreated;
    public event Action<string, LocalPix?>? SyncedPixDeleted;
    public event Action<PremiumStatus>? PremiumStatusChanged;
    public event Action<PixMemberChangeRankSuccessDto>? PixMemberChangeRankSuccess;
    public event Action<PixMemberChangeRankFailedDto>? PixMemberChangeRankFailed;
    public event Action<PixMemberRemoveSuccessDto>? PixMemberRemoveSuccess;
    public event Action<PixMemberRemoveFailedDto>? PixMemberRemoveFailed;
    public event Action<string>? SyncedPixUnsubscribed;

    public event Action<SyncedPixMembersResponseDto>? SyncedPixMembersUpdated;

    private bool IsSocketReady => Socket != null && ConnectionCts != null && Socket.State == WebSocketState.Open;
    public bool IsConnectedAuth => IsSocketReady && State == ConnectionState.Connected && Client.IsAuthenticated;

    private sealed record PendingSyncedPixCreate(string LocalPixId, SyncedPixCreateDto Request);
    private readonly ConcurrentDictionary<string, PendingSyncedPixCreate> _pendingSyncedPixCreates = new();

    public override Task Initialize() {
        PixService?.PixUpdated += OnPixUpdated;

        StateService?.TerritoryChanged += (_, _, territory) => {
            _ = SendTerritoryUpdateAsync(territory);
        };
        StateService?.TerritoryLoaded += territory => {
            _ = SendTerritoryUpdateAsync(territory);
            QuerySyncablePixs(); // todo: Optimize
        };

        StateService?.InitialLoad += ((_) => {
            if(!Config.Sync.AutoConnect) return;
            Connect();
        });
        Services.ClientState.Login += (() => {
            if(!Config.Sync.AutoConnect) return;
            Connect();
        });
        Services.ClientState.Logout += ((_, _) => {
            Disconnect();
        });
        return Task.CompletedTask;
    }

    public void QuerySyncablePixs() => _ = QuerySyncablePixsAsync();
    public void SubscribePix(string pixId, string? secretKey) => _ = SubscribePixAsync(pixId, secretKey);
    public void UnsubscribePix(string pixId) => _ = UnsubscribePixAsync(pixId);
    public void DeleteSyncedPix(string pixId) => _ = DeleteSyncedPixAsync(pixId);
    public void ChangePixMemberRank(string pixId, long characterId, PixRank newRank) => _ = ChangePixMemberRankAsync(pixId, characterId, newRank);
    public void RemovePixMember(string pixId, long characterId) => _ = RemovePixMemberAsync(pixId, characterId);

    private async Task QuerySyncablePixsAsync() {
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.SyncedPixQueryRequest, new { });
    }

    private async Task SubscribePixAsync(string pixId, string? secretKey) {
        if(string.IsNullOrWhiteSpace(pixId)) return;
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.SyncedPixSubscribe, new SyncedPixSubscribeDto {
            PixId = pixId.Trim().ToUpperInvariant(),
            SecretKey = string.IsNullOrWhiteSpace(secretKey) ? null : secretKey.Trim()
        });
    }

    private async Task UnsubscribePixAsync(string pixId) {
        if(string.IsNullOrWhiteSpace(pixId)) return;
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.SyncedPixUnsubscribe, new SyncedPixUnsubscribeDto { PixId = pixId });
    }

    private async Task DeleteSyncedPixAsync(string pixId) {
        if(string.IsNullOrWhiteSpace(pixId)) return;
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.SyncedPixDelete, new SyncedPixDeleteDto { PixId = pixId });
    }

    private async Task SendTerritoryUpdateAsync(Shared.Structs.Territory.TerritoryData? territory) {
        if(territory == null) return;
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.ClientTerritoryUpdate, territory.ToDto());
    }

    private void OnPixUpdated(PixUpdate u) {
        if(u.Pix == null) return;
        if(!IsConnectedAuth) return;
        if(!u.EditFinished) return;
        if(u.Origin != PixUpdateOrigin.Local) return;
        if(!PixService.CanSyncEdit(u.Pix)) return;
        if(u.Pix is not SyncedPix syncedPix) return;

        BaseSyncedPixUpdate? update = null;
        switch(u.Type) {
            case PixUpdateType.InfoProperties: update = new SyncedPixUpdateInfoProperties(syncedPix.Id, syncedPix.Info.ToSynced()); break;
            case PixUpdateType.Uri:
            case PixUpdateType.BrowserProperties: update = new SyncedPixUpdateBrowserProperties(syncedPix.Id, syncedPix.Browser.ToSynced()); break;
            case PixUpdateType.RendererTransform:
            case PixUpdateType.RendererProperties: update = new SyncedPixUpdateRendererProperties(syncedPix.Id, syncedPix.Renderer.ToSynced()); break;
            case PixUpdateType.LightTransform:
            case PixUpdateType.LightProperties: update = new SyncedPixUpdateLightProperties(syncedPix.Id, syncedPix.Light.ToSynced()); break;
            case PixUpdateType.AudioProperties: update = new SyncedPixUpdateAudioProperties(syncedPix.Id, syncedPix.Audio.ToSynced()); break;
            case PixUpdateType.SyncProperties: update = new SyncedPixUpdateSyncProperties(syncedPix.Id, syncedPix.Sync.ToSynced()); break;
            default: update = new SyncedPixUpdate(syncedPix.Id, syncedPix.Info.ToSynced(), syncedPix.Browser.ToSynced(), syncedPix.Renderer.ToSynced(), syncedPix.Light.ToSynced(), syncedPix.Audio.ToSynced(), syncedPix.Sync.ToSynced()); break;
        }

        if(update == null) return;
        _ = SendAsync(MessageType.SyncedPixUpdate, update);
    }

    public void Update() {
        if(Client.AuthExpiration != null && Client.AuthExpirationTime!.Value.TotalSeconds <= 0) {
            Client.AuthExpiration = null;
            Disconnect();
        }
    }

    public void Connect() => _ = ConnectAsync();
    private async Task ConnectAsync() {
        await ConnectionLock.WaitAsync();
        if(State is ConnectionState.Connecting or ConnectionState.Connected) {
            ConnectionLock.Release();
            return;
        }

        ConnectionCts?.Cancel();
        ConnectionCts?.Dispose();
        ConnectionCts = new CancellationTokenSource();

        SetState(ConnectionState.Connecting, "Connecting..", StatusType.None);
        ConnectionLock.Release();

        for(int attempt = 1; attempt <= MaxConnectionAttempts; attempt++) {
            try {
                var socket = new ClientWebSocket();
                await socket.ConnectAsync(Api.Socket, ConnectionCts.Token);

                await ConnectionLock.WaitAsync();
                Socket?.Dispose();
                Socket = socket;

                ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(ConnectionCts.Token));

                await SendAsync(MessageType.AuthRequest, new AuthRequestDto(Plugin.Version, StateService.LocalPlayerContentId, Config.Sync.SecretKey, StateService.CurrentTerritory?.ToDto() ?? new()));
                SetState(ConnectionState.Connected, "Connected", StatusType.Hide);
                ConnectionLock.Release();
                return;
            } catch(OperationCanceledException) {
                SetState(ConnectionState.Disconnected, "Connection Aborted", StatusType.Warn);
                return;
            } catch(Exception ex) when(attempt < MaxConnectionAttempts) {
                Services.Log.Verbose($"[SyncService] Connection Failed (Retry {attempt}): {ex.Message}");
                StatusMessage = "Reconnecting..";
                try {
                    var delay = Math.Min(60000, 2000 * attempt) + RNG.Next(0, 500);
                    await Task.Delay(delay, ConnectionCts.Token);
                } catch(OperationCanceledException) {
                    SetState(ConnectionState.Disconnected, "Connection Aborted", StatusType.Warn);
                    return;
                }
            } catch(Exception ex) {
                Services.Log.Error($"[SyncService] Connection Failed: {ex}");
                SetState(ConnectionState.Disconnected, "Connection Failed", StatusType.Error);
                StatusMessage = "Connection Failed";
                return;
            }
        }

        SetState(ConnectionState.Disconnected, "Connection Attempts Exceeded", StatusType.Error);
    }

    public void AbortConnection() => ConnectionCts?.Cancel();

    public void Disconnect() => _ = DisconnectAsync();
    private async Task DisconnectAsync() {
        ConnectionCts?.Cancel();
        await ConnectionLock.WaitAsync();
        try {
            if(Socket is { State: WebSocketState.Open or WebSocketState.CloseReceived }) {
                try {
                    await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client Disconnected", CancellationToken.None);
                } catch { }
            }
            SetState(ConnectionState.Disconnected, "Client Disconnected", StatusType.None);
        } finally {
            ConnectionLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token) {
        if(Socket == null) return;
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        try {
            while(Socket.State == WebSocketState.Open && !token.IsCancellationRequested) {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                try {
                    do {
                        result = await Socket.ReceiveAsync(buffer, token);
                        if(result.MessageType == WebSocketMessageType.Close) {
                            Services.Log.Warning($"[SyncService] {result.CloseStatusDescription}");
                            SetState(ConnectionState.Disconnected, $"{result.CloseStatusDescription}", StatusType.Error);
                            if(result.CloseStatusDescription == "Heartbeat Timeout") {
                                StatusMessage = "Reconnecting..";
                                Connect();
                            } else {
                                StatusMessage = $"Server Disconnected";
                            }
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while(!result.EndOfMessage && !token.IsCancellationRequested);
                } catch(OperationCanceledException) {
                    break;
                } catch(WebSocketException) {
                    break;
                } catch(IOException) {
                    break;
                }
                var json = Encoding.UTF8.GetString(ms.ToArray());
                if(!SyncData.TryGetMessage(json, out SocketMessage message)) continue;
                switch(message.Type) {
                    case MessageType.Ping:
                        if(!SyncData.TryGetObject(message.Data, out ServerSessionDto session)) break;

                        Server.UserCount = session.UserCount;
                        Server.PixCount = session.PixCount;
                        await SendAsync(MessageType.Pong, null);
                        break;
                    case MessageType.AuthCreateSuccess: {
                            SetState(ConnectionState.Connected, "Connected", StatusType.Hide);
                            if(!SyncData.TryGetObject(message.Data, out AuthCreateSuccessDto authCreate)) break;

                            Client.IsSecretKeyInvalid = false;
                            Client.IsAuthenticated = true;
                            Client.AuthKey = null;
                            authCreate.Style.ApplyTo(Client.Style);
                            var cProps = Config.Sync.GetCurrentCharacterProperties(Config, StateService);
                            cProps.Alias = Client.Style.Alias;
                            if(Client.Premium.IsSupporter != authCreate.Premium.IsSupporter || Client.Premium.IsSubscriber != authCreate.Premium.IsSubscriber) {
                                Client.Premium = authCreate.Premium;
                                PremiumStatusChanged?.Invoke(Client.Premium);
                            }
                            Config.Sync.SecretKey = authCreate.SecretKey;
                            Config.Save();

                            lock(SyncablePixsLock) SyncablePixs = authCreate.SyncablePixs;
                            SyncablePixsUpdated?.Invoke();
                            break;
                        }
                    case MessageType.AuthLoginSuccess: {
                            SetState(ConnectionState.Connected, "Connected", StatusType.Hide);
                            if(!SyncData.TryGetObject(message.Data, out AuthLoginSuccessDto authLogin)) break;
                            Server.UserCount = authLogin.ServerSession.UserCount;
                            Server.PixCount = authLogin.ServerSession.PixCount;
                            Client.IsSecretKeyInvalid = false;
                            Client.IsAuthenticated = true;
                            Client.AuthKey = null;
                            authLogin.Style.ApplyTo(Client.Style);
                            var cProps = Config.Sync.GetCurrentCharacterProperties(Config, StateService);
                            if(cProps.Alias != Client.Style.Alias) {
                                cProps.Alias = Client.Style.Alias;
                                Config.Save();
                            }
                            if(Client.Premium.IsSupporter != authLogin.Premium.IsSupporter || Client.Premium.IsSubscriber != authLogin.Premium.IsSubscriber) {
                                Client.Premium = authLogin.Premium;
                                PremiumStatusChanged?.Invoke(Client.Premium);
                            }
                            PixService.AddOrUpdateSyncedPixs(authLogin.SubbedPixs);
                            lock(SyncablePixsLock) SyncablePixs = authLogin.SyncablePixs;
                            SyncablePixsUpdated?.Invoke();
                            break;
                        }
                    case MessageType.AuthRequired: {
                            if(!SyncData.TryGetObject(message.Data, out AuthRequiredDto authRequired)) break;
                            Client.IsSecretKeyInvalid = false;
                            Client.IsAuthenticated = false;
                            Client.AuthKey = authRequired.SecretKey;
                            Client.AuthExpiration = authRequired.ExpirationTimestamp;
                            SetState(ConnectionState.Connected, "Connected, Authentication Required.", StatusType.Info);
                            AuthKeyReceived?.Invoke();
                            break;
                        }
                    case MessageType.AuthFailed: {
                            if(!SyncData.TryGetObject(message.Data, out AuthFailedDto authFailed)) break;
                            var statusMessage = "Server Disconnected";
                            if(authFailed.Reason == AuthFailedReason.InvalidAuth) {
                                statusMessage = $"Invalid AuthKey";
                                Client.IsSecretKeyInvalid = true;
                            } else if(authFailed.Reason == AuthFailedReason.Forbidden) {
                                statusMessage = $"Auth Forbidden";
                            }
                            SetState(ConnectionState.Disconnected, statusMessage, StatusType.Error);
                            StatusMessage = statusMessage;
                            return;
                        }

                    case MessageType.StyleUpdateSuccess: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedCharacterProperties style)) break;
                            style.ApplyTo(Client.Style);
                            var cProps = Config.Sync.GetCurrentCharacterProperties(Config, StateService);
                            if(cProps.Alias != Client.Style.Alias) {
                                cProps.Alias = Client.Style.Alias;
                                Config.Save();
                            }
                            PixService.ApplyPixStyleUpdate(new(StateService.LocalPlayerContentId, style.Alias, style.AliasStyle, style.PixStyle));
                            StyleUpdateResponse?.Invoke(true);
                            break;
                        }
                    case MessageType.StyleUpdateFailed:
                        StyleUpdateResponse?.Invoke(false);
                        break;
                    case MessageType.SubbedPixStyleUpdated: {
                            if(!SyncData.TryGetObject(message.Data, out SubbedPixStyleUpdateDto styleUpdate)) break;
                            PixService.ApplyPixStyleUpdate(styleUpdate);
                            break;
                        }

                    case MessageType.SyncedPixCreateSuccess: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixCreateSuccessDto success)) break;
                            if(_pendingSyncedPixCreates.TryRemove(success.RequestId, out var pending)) {
                                if(PixService.GetPix(pending.LocalPixId) is LocalPix localPix) {
                                    var synced = await PixService.CreateSyncedPixAsync(localPix, pending.Request, success);
                                    if(synced != null) {
                                        SyncedPixCreated?.Invoke(localPix, synced);
                                    }
                                }
                            }
                            QuerySyncablePixs();
                            break;
                        }
                    case MessageType.SyncedPixCreateFailed: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixCreateFailedDto failed)) break;
                            _pendingSyncedPixCreates.TryRemove(failed.RequestId, out _);
                            Services.Log.Warning($"[SyncService] SyncedPixCreateFailed: {failed.Reason}");
                            break;
                        }

                    case MessageType.SubbedPixQueryResponse: {
                            if(!SyncData.TryGetObject(message.Data, out SubbedPixQueryListDto list)) break;
                            PixService.AddOrUpdateSyncedPixs(list.SubbedPixs);
                            QuerySyncablePixs();
                            break;
                        }
                    case MessageType.SyncablePixQueryResponse: {
                            if(!SyncData.TryGetObject(message.Data, out SyncablePixQueryListDto list)) break;
                            lock(SyncablePixsLock) SyncablePixs = list.SyncablePixs;
                            SyncablePixsUpdated?.Invoke();
                            break;
                        }
                    case MessageType.SyncedPixSubscribeSuccess: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixSubscribeSuccessDto dto)) break;
                            PixService.AddOrUpdateSyncedPix(dto.Pix);
                            break;
                        }
                    case MessageType.SyncedPixSubscribeFailed: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixSubscribeFailedDto failed)) break;
                            SubscriptionFailed?.Invoke(failed.Reason);
                            break;
                        }
                    case MessageType.SyncedPixUnsubscribeSuccess: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixUnsubscribeSuccessDto unsub)) break;
                            if(string.IsNullOrWhiteSpace(unsub.PixId)) break;
                            PixService.RemoveSyncedSubscription(unsub.PixId);
                            SyncedPixUnsubscribed?.Invoke(unsub.PixId);
                            break;
                        }
                    case MessageType.SyncedPixUnsubscribeFailed: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixUnsubscribeFailedDto failed)) break;
                            SubscriptionFailed?.Invoke(failed.Reason);
                            break;
                        }
                    case MessageType.SyncedPixDeleteSuccess: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixDeleteSuccessDto success)) break;
                            var local = await PixService.RemoveSyncedPixAsync(success.PixId);
                            SyncedPixDeleted?.Invoke(success.PixId, local);
                            break;
                        }
                    case MessageType.SyncedPixDeleteFailed: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixDeleteFailedDto failed)) break;
                            SubscriptionFailed?.Invoke(failed.Reason);
                            break;
                        }
                    case MessageType.SyncedPixDeleted: {
                            if(!SyncData.TryGetObject(message.Data, out SyncedPixDeletedDto deleted)) break;
                            PixService.RemoveSyncedSubscription(deleted.PixId);
                            SyncedPixUnsubscribed?.Invoke(deleted.PixId);
                            break;
                        }
                    case MessageType.SyncedPixUpdate: {
                            if(!SyncData.TryGetSyncedPixUpdate(message.Data, out BaseSyncedPixUpdate update)) break;
                            PixService.ApplyPixPropertyUpdate(update);
                            break;
                        }
                    case MessageType.SyncedPixMembersUpdate: {
                        if(SyncData.TryGetObject(message.Data, out SyncedPixMembersResponseDto membersResp))
                            SyncedPixMembersUpdated?.Invoke(membersResp);
                        break;
                    }
                    case MessageType.PremiumStatusChanged: {
                        if(SyncData.TryGetObject(message.Data, out PremiumStatus dto)) {
                            Client.Premium = dto;
                            PremiumStatusChanged?.Invoke(dto);
                            if(dto.IsSupporter || dto.IsSubscriber) {
                                await SendStyleUpdateAsync();
                            } else {
                                PixService.ApplyPixStyleUpdate(new(StateService.LocalPlayerContentId, Client.Style.Alias, null, null));
                            }
                        }
                        break;
                    }
                    case MessageType.PixMemberChangeRankSuccess: {
                        if(SyncData.TryGetObject(message.Data, out PixMemberChangeRankSuccessDto dto))
                            PixMemberChangeRankSuccess?.Invoke(dto);
                        break;
                    }
                    case MessageType.PixMemberChangeRankFailed: {
                        if(SyncData.TryGetObject(message.Data, out PixMemberChangeRankFailedDto dto))
                            PixMemberChangeRankFailed?.Invoke(dto);
                        break;
                    }
                    case MessageType.PixMemberRemoveSuccess: {
                        if(SyncData.TryGetObject(message.Data, out PixMemberRemoveSuccessDto dto))
                            PixMemberRemoveSuccess?.Invoke(dto);
                        break;
                    }
                    case MessageType.PixMemberRemoveFailed: {
                        if(SyncData.TryGetObject(message.Data, out PixMemberRemoveFailedDto dto))
                            PixMemberRemoveFailed?.Invoke(dto);
                        break;
                    }
                }
            }
        } catch(OperationCanceledException) {
        } catch(Exception ex) {
            Services.Log.Info($"[SyncService] Socket Failed: {ex}");
            SetState(ConnectionState.Disconnected, $"Error: Check /xllog for details", StatusType.Error);
        } finally {
            if(State != ConnectionState.Disconnected) {
                Services.Log.Warning($"[SyncService] Server Disconnected, Reconnecting..");
                SetState(ConnectionState.Disconnected, "Server Disconnected, Reconnecting..", StatusType.Warn);
                StatusMessage = "Reconnecting..";
                Connect();
            }
        }
    }

    public void CreateSyncedPix(IPix pix, SyncedPixMetaDto meta) => _ = CreateSyncedPixAsync(pix, meta);
    private async Task CreateSyncedPixAsync(IPix pix, SyncedPixMetaDto meta) {
        if(!IsConnectedAuth) return;

        var requestId = Guid.NewGuid().ToString("N");
        var request = new SyncedPixCreateDto {
            RequestId = requestId,
            LocalPixId = pix.Id,
            Pix = PixService.BuildPixDto(pix),
            Meta = meta
        };
        _pendingSyncedPixCreates[requestId] = new PendingSyncedPixCreate(pix.Id, request);
        await SendAsync(MessageType.SyncedPixCreate, request);
    }

    private async Task SendAsync(MessageType type, object? data) {
        if(!IsSocketReady) return;
        var buffer = SyncData.CreateMessageBuffer(type, data);
        await Socket!.SendAsync(buffer, WebSocketMessageType.Text, true, ConnectionCts!.Token);
    }

    private void SetState(ConnectionState connectionState, string? statusMessage, StatusType statusType = StatusType.Info) {
        StatusMessage = null;
        State = connectionState;
        if(Client.IsAuthenticated) Client.AuthExpiration = null;
        StateChanged?.Invoke(connectionState, statusMessage, statusType);

        if(State == ConnectionState.Disconnected) {
            Client.IsAuthenticated = false;

            try { ConnectionCts?.Cancel(); } catch { }
            ConnectionCts?.Dispose();
            ConnectionCts = null;

            if(Socket != null) {
                try { Socket.Dispose(); } catch { }
                Socket = null;
            }

            lock(SyncablePixsLock) { SyncablePixs = []; }
            SyncablePixsUpdated?.Invoke();
        }
    }

    public async Task RequestPixMembersAsync(string pixId) {
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.SyncedPixMembersRequest, new SyncedPixMembersRequestDto { PixId = pixId });
    }

    private async Task ChangePixMemberRankAsync(string pixId, long characterId, PixRank newRank) {
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.PixMemberChangeRank, new PixMemberChangeRankDto { PixId = pixId, CharacterId = characterId, NewRank = newRank });
    }
    private async Task RemovePixMemberAsync(string pixId, long characterId) {
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.PixMemberRemove, new PixMemberRemoveDto { PixId = pixId, CharacterId = characterId });
    }

    public void ReportPix(string pixId) => _ = ReportPixAsync(pixId);
    private async Task ReportPixAsync(string pixId) {
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.ReportPix, pixId);
    }

    public void ReportUser(long characterId) => _ = ReportUserAsync(characterId);
    private async Task ReportUserAsync(long characterId) {
        if(!IsConnectedAuth) return;
        await SendAsync(MessageType.ReportUser, characterId);
    }

    public void SendStyleUpdate() => _ = SendStyleUpdateAsync();
    private async Task SendStyleUpdateAsync() {
        if(!IsConnectedAuth || (!Client.Premium.IsSupporter && !Client.Premium.IsSubscriber)) return;

        var cProps = Config.Sync.GetCurrentCharacterProperties(Config, StateService);
        var scProps = cProps.ToSynced();
        if(!Client.Premium.IsSubscriber) {
            scProps.AliasStyle = null;
            scProps.PixStyle = null;
        }

        await SendAsync(MessageType.StyleUpdate, scProps);
    }

    public override async Task Dispose() {
        await DisconnectAsync();
    }
}
