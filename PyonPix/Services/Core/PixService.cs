using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using PyonPix.Config;
using PyonPix.Config.Pix;
using PyonPix.Services.Game;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Structs.Pix.Properties;
using PyonPix.Shared.Structs.Territory;
using PyonPix.Shared.Sync.Dto;
using PyonPix.Shared.Sync.Dto.Subbed;
using PyonPix.Shared.Utility;

namespace PyonPix.Services.Core;

public class PixService(Configuration config, IServiceContext services) : BaseService(config, services) {
    private StateService StateService => Services.Get<StateService>();
    private DataService DataService => Services.Get<DataService>();

    private readonly List<string> TerritoryActivationOrder = [];

    private const string PixClipboardPrefix = "PX1:";

    public readonly Dictionary<string, IPix> SpawnedPixs = [];

    public int PixSpawnLimit => Config.Global.General.PixSpawnLimit;

    public List<LocalPix> LocalPixs => Config.LocalPixs;
    public readonly Dictionary<string, SyncedPix> SyncedPixs = [];

    public Dictionary<string, PixVariant> GetPixVariantsForCurrentCharacter() {
        var contentId = StateService.LocalPlayerContentId;
        if (!Config.PixVariants.TryGetValue(contentId, out var dict)) {
            dict = [];
            Config.PixVariants[contentId] = dict;
        }
        return dict;
    }

    public Dictionary<long, Dictionary<string, PixVariant>> PixVariants = new();
    private static readonly TimeSpan SyncedVariantRetention = TimeSpan.FromDays(7);

    public event Action<IPix, bool>? PixSpawned;
    public event Action<PixUpdate>? PixUpdated;
    public event Action<IPix, bool>? PixDespawned;
    public event Action? AllPixDespawned;

    public override Task Initialize() {
        StateService.TerritoryChanged += (isUnload, isTerritoryLoading, territory) => {
            if(isUnload) {
                DespawnAll();
                return;
            }
            ReevaluateCurrentTerritory(false, isTerritoryLoading);
        };

        StateService.TerritoryLoaded += (territory) => {
            ReevaluateCurrentTerritory(false, false);
        };

        JsonSerializer.Serialize(new Pix(), JsonOptions);
        return Task.CompletedTask;
    }

    public bool IsActive(IPix? pix) => GetVariant(pix, false)?.Active ?? false;
    public bool IsSpawned(IPix? pix) => pix != null && SpawnedPixs.ContainsKey(ResolveRuntimePix(pix).Id);
    public bool IsSubscribed(string? pixId) => !string.IsNullOrWhiteSpace(pixId) && SyncedPixs.ContainsKey(pixId);

    public void Enable(IPix pix) {
        pix = ResolveRuntimePix(pix);
        var variant = GetVariant(pix, true)!;
        variant.Active = true;
        variant.LastSeenUtc = DateTime.UtcNow;

        var territory = StateService.CurrentTerritory;
        if(territory != null && pix.Territory.Matches(territory, (pix as BasePix)?.Territory.Persistent ?? false)) {
            TerritoryActivationOrder.Remove(pix.Id);
            TerritoryActivationOrder.Add(pix.Id);
        }

        Config.Save();
        ReevaluateCurrentTerritory(true, false);
    }
    public void Disable(IPix pix) {
        pix = ResolveRuntimePix(pix);
        var variant = GetVariant(pix, true)!;
        variant.Active = false;
        variant.LastSeenUtc = DateTime.UtcNow;

        if(SpawnedPixs.TryGetValue(pix.Id, out var spawned)) {
            SpawnedPixs.Remove(pix.Id);
            PixDespawned?.Invoke(spawned, true);
        }
        TerritoryActivationOrder.Remove(pix.Id);

        Config.Save();
        ReevaluateCurrentTerritory(true, false);
    }
    public void Toggle(IPix pix) {
        pix = ResolveRuntimePix(pix);
        if(IsActive(pix)) Disable(pix); else Enable(pix);
    }
    public void Toggle(string pixId) {
        pixId = pixId.ToUpper();
        if(!pixId.StartsWith(NameUtil.PixIdLocalPrefix) && !pixId.StartsWith(NameUtil.PixIdSyncedPrefix)) return;
        var p = GetRuntimePixs().FirstOrDefault(x => x.Id == pixId);
        if(p == null) return;
        Toggle(p);
    }

    //public IPix? GetPix(string? pixId) {
    //    if(pixId == null) return null;
    //    return GetRuntimePixs().FirstOrDefault(x => x.Id == pixId);
    //}
    public IPix? GetPix(string? pixId) {
        if(pixId == null) return null;

        if(SyncedPixs.TryGetValue(pixId, out var synced)) return synced;
        return LocalPixs.FirstOrDefault(x => x.Id == pixId);
    }

    public PixVariant GetVariant(string pixId, bool create = false) {
        var variants = GetPixVariantsForCurrentCharacter();
        if(variants.TryGetValue(pixId, out var variant)) return variant;
        if(!create) return null!;
        variant = new PixVariant { LastSeenUtc = DateTime.UtcNow };
        variants[pixId] = variant;
        return variant;
    }
    public PixVariant? GetVariant(IPix? pix, bool create = false) {
        if(pix == null) return null;
        var id = ResolveRuntimePix(pix).Id;
        var variants = GetPixVariantsForCurrentCharacter();
        if(variants.TryGetValue(id, out var variant)) return variant;
        if(!create) return null;
        variant = new PixVariant { LastSeenUtc = DateTime.UtcNow };
        variants[id] = variant;
        return variant;
    }
    public PixVariant? TryGetVariant(string pixId) => GetPixVariantsForCurrentCharacter().TryGetValue(pixId, out var variant) ? variant : null;
    public PixVariant? TryGetVariant(IPix? pix) => pix == null ? null : TryGetVariant(ResolveRuntimePix(pix).Id);
    public PixVariant EnsureVariant(string pixId) {
        var variants = GetPixVariantsForCurrentCharacter();
        if(variants.TryGetValue(pixId, out var variant)) return variant;
        variant = new PixVariant { LastSeenUtc = DateTime.UtcNow };
        variants[pixId] = variant;
        return variant;
    }
    public PixVariant EnsureVariant(IPix pix) => EnsureVariant(ResolveRuntimePix(pix).Id);
    private void SaveVariant(PixVariant variant, bool persist = true) {
        variant.LastSeenUtc = DateTime.UtcNow;
        variant.PruneEmpty();
        if(persist) Config.Save();
    }

    private void ReevaluateCurrentTerritory(bool isUserAction, bool isTerritoryLoading) {
        CleanupPixVariants();

        var territory = StateService.CurrentTerritory;

        if(territory == null) {
            DespawnAll();
            return;
        }

        var active = GetActivePixs();
        var matching = active.Where(p => p.Territory.Matches(territory, (p as BasePix)?.Territory.Persistent ?? false)).ToList();
        matching = matching.OrderByDescending(p => TerritoryActivationOrder.IndexOf(p.Id)).ToList();
        var limit = matching.Take(PixSpawnLimit).ToList();

        ApplySpawnSet(limit, isUserAction, isTerritoryLoading);
    }

    /*
    private IEnumerable<IPix> GetAllPixs() {
        foreach(var local in LocalPixs)
            yield return local;
        foreach(var synced in SyncedPixs.Values)
            yield return synced;
    }
    */
    private List<IPix> GetActivePixs() {
        var all = GetRuntimePixs().ToDictionary(p => p.Id);
        var result = new List<IPix>();
        var variants = GetPixVariantsForCurrentCharacter();
        foreach(var kv in variants) {
            if(!kv.Value.Active) continue;
            if(all.TryGetValue(kv.Key, out var pix))
                result.Add(pix);
        }
        return result;
    }
    private IEnumerable<IPix> GetRuntimePixs() {
        foreach(var local in LocalPixs) {
            if(local.Sync.IsSynced) continue;
            yield return local;
        }
        foreach(var synced in SyncedPixs.Values)
            yield return synced;
    }
    private IPix ResolveRuntimePix(IPix pix) {
        if(pix is not BasePix bp) return pix;
        if(!bp.Sync.IsSynced) return pix;
        if(string.IsNullOrWhiteSpace(bp.Sync.SyncedPixId)) return pix;
        if(!SyncedPixs.TryGetValue(bp.Sync.SyncedPixId, out var synced)) return pix;
        return synced;
    }

    private void ApplySpawnSet(List<IPix> pixs, bool isUserAction, bool isTerritoryLoading) {
        var pixIds = pixs.Select(p => p.Id).ToHashSet();

        foreach(var kvp in SpawnedPixs.ToList()) {
            if(!pixIds.Contains(kvp.Key)) {
                SpawnedPixs.Remove(kvp.Key);
                PixDespawned?.Invoke(kvp.Value, isUserAction);
            }
        }

        if(isTerritoryLoading) return;

        foreach(var pix in pixs) {
            if(SpawnedPixs.ContainsKey(pix.Id)) continue;

            SpawnedPixs[pix.Id] = pix;
            PixSpawned?.Invoke(pix, isUserAction);

            TerritoryActivationOrder.Remove(pix.Id);
            TerritoryActivationOrder.Add(pix.Id);
        }
    }
    private void DespawnAll() {
        if(SpawnedPixs.Count == 0)
            return;

        foreach(var pix in SpawnedPixs.Values)
            PixDespawned?.Invoke(pix, false);

        SpawnedPixs.Clear();
        TerritoryActivationOrder.Clear();

        AllPixDespawned?.Invoke();
    }

    public IReadOnlyList<TerritoryData> GetPixTerritories() {
        var set = new HashSet<TerritoryData>();
        foreach(var p in GetRuntimePixs())
            set.Add(StateService.GetTerritoryData(p.Territory, true));
        return set.ToList();
    }

    public IReadOnlyList<IPix> GetOrderedPixsForTerritory(TerritoryData territory, bool persistent) {
        var pixs = GetRuntimePixs().Where(p => p.Territory.Matches(territory, persistent)).ToList();
        return pixs.OrderByDescending(p => IsActive(p)).ToList();
    }

    public IPix CreateLocalPix() {
        var pix = new LocalPix(GenerateId(), StateService);
        LocalPixs.Add(pix);
        Enable(pix);
        return pix;
    }

    public void DeleteLocalPix(IPix? pix) {
        if(pix == null) return;
        if(IsSpawned(pix)) return;
        if(pix is not LocalPix lPix) return;
        if(IsActive(pix)) Disable(pix);

        LocalPixs.Remove(lPix);
        Config.Save();
    }

    private void PromoteVariantToSynced(string localPixId, string syncedPixId) {
        var variants = GetPixVariantsForCurrentCharacter();
        if(string.IsNullOrWhiteSpace(localPixId) || string.IsNullOrWhiteSpace(syncedPixId)) return;
        variants.TryGetValue(localPixId, out var localVariant);
        variants.TryGetValue(syncedPixId, out var existingSyncedVariant);
        if(localVariant != null) {
            variants.Remove(localPixId);
        }
        var variant = existingSyncedVariant ?? localVariant ?? new PixVariant();
        if(existingSyncedVariant != null && localVariant != null) {
            variant.Active = variant.Active || localVariant.Active;
            variant.PersistentCache = variant.PersistentCache || localVariant.PersistentCache;
            variant.SyncCookies = localVariant.SyncCookies;
            variant.Browser ??= localVariant.Browser;
            variant.Renderer ??= localVariant.Renderer;
            variant.Light ??= localVariant.Light;
            variant.Audio ??= localVariant.Audio;
        }
        variant.IsSynced = true;
        variant.LastSeenUtc = DateTime.UtcNow;
        variant.PruneEmpty();
        variants[syncedPixId] = variant;
    }
    private void DemoteVariantToLocal(string syncedPixId, string localPixId) {
        var variants = GetPixVariantsForCurrentCharacter();
        if(string.IsNullOrWhiteSpace(localPixId) || string.IsNullOrWhiteSpace(syncedPixId)) return;
        if(variants.TryGetValue(syncedPixId, out PixVariant? variant)) {
            variants.Remove(syncedPixId);
        } else {
            variant = new PixVariant();
        }
        variant.IsSynced = false;
        variant.LastSeenUtc = DateTime.UtcNow;
        variant.PruneEmpty();
        variants[localPixId] = variant;
    }

    public SyncedPix? CreateSyncedPix(LocalPix localPix, SyncedPixCreateDto request, SyncedPixCreateSuccessDto result) {
        return CreateSyncedPixAsync(localPix, request, result).GetAwaiter().GetResult();
    }

    public async Task<SyncedPix?> CreateSyncedPixAsync(LocalPix localPix, SyncedPixCreateDto request, SyncedPixCreateSuccessDto result) {
        if(localPix == null) return null;

        var wasActive = IsActive(localPix);
        if(wasActive) Disable(localPix);

        _ = await DataService.RenameUDFAsync(localPix.Id, result.PixId);

        var synced = new SyncedPix { Id = result.PixId, SelfRank = PixRank.Owner };

        ApplyCreatedSyncedPixData(synced, request.Pix, request.Meta);

        synced.Sync.IsSynced = true;
        synced.Sync.SyncedPixId = result.PixId;
        synced.Sync.SecretKey = result.SecretKey;

        localPix.Sync.IsSynced = true;
        localPix.Sync.SyncedPixId = result.PixId;
        localPix.Sync.SecretKey = result.SecretKey;

        PromoteVariantToSynced(localPix.Id, result.PixId);

        SyncedPixs[result.PixId] = synced;
        Config.Save();

        if(wasActive) Enable(synced);

        return synced;
    }

    public async Task<LocalPix?> RemoveSyncedPixAsync(string syncedPixId) {
        if(string.IsNullOrWhiteSpace(syncedPixId)) return null;

        SyncedPixs.TryGetValue(syncedPixId, out var synced);
        var linkedLocal = LocalPixs.FirstOrDefault(x => x.Sync.IsSynced && x.Sync.SyncedPixId == syncedPixId);
        if(linkedLocal == null && synced == null) return null;

        var wasActive = (synced != null && IsActive(synced))
            || (synced == null && (TryGetVariant(syncedPixId)?.Active == true || SpawnedPixs.ContainsKey(syncedPixId)));

        if(wasActive && synced != null) Disable(synced);

        if(linkedLocal != null) {
            if(synced != null) {
                linkedLocal.Version = synced.Version;
                linkedLocal.Info = CloneOrNew(synced.Info);
                linkedLocal.Browser = CloneOrNew(synced.Browser);
                linkedLocal.Territory = CloneOrNew(synced.Territory);
                linkedLocal.Renderer = CloneOrNew(synced.Renderer);
                linkedLocal.Light = CloneOrNew(synced.Light);
                linkedLocal.Audio = CloneOrNew(synced.Audio);
            }

            linkedLocal.Sync.IsSynced = false;
            linkedLocal.Sync.SyncedPixId = string.Empty;
            linkedLocal.Sync.SecretKey = null;

            DemoteVariantToLocal(syncedPixId, linkedLocal.Id);

            if(DataService != null) {
                _ = await DataService.RenameUDFAsync(syncedPixId, linkedLocal.Id);
            }
        } else {
            var variants = GetPixVariantsForCurrentCharacter();
            variants.Remove(syncedPixId);
        }

        SyncedPixs.Remove(syncedPixId);
        Config.Save();

        if(wasActive && linkedLocal != null) {
            Enable(linkedLocal);
        }

        return linkedLocal;
    }

    public void RemoveSyncedSubscription(string syncedPixId) {
        if(string.IsNullOrWhiteSpace(syncedPixId)) return;
        if(SpawnedPixs.TryGetValue(syncedPixId, out var spawned)) {
            SpawnedPixs.Remove(syncedPixId);
            PixDespawned?.Invoke(spawned, false);
        }

        SyncedPixs.Remove(syncedPixId);
        var variants = GetPixVariantsForCurrentCharacter();
        if(variants.Remove(syncedPixId)) {
            Config.Save();
        }

        ReevaluateCurrentTerritory(false, false);
    }

    private void ReconcileSyncedVariantLinks() {
        foreach(var local in LocalPixs) {
            if(!local.Sync.IsSynced) continue;
            if(string.IsNullOrWhiteSpace(local.Sync.SyncedPixId)) continue;

            PromoteVariantToSynced(local.Id, local.Sync.SyncedPixId);
        }
    }

    public void AddOrUpdateSyncedPixs(IEnumerable<SubbedPixQueryItemDto> pixs) {
        var now = DateTime.UtcNow;
        var incoming = pixs.ToDictionary(x => x.PixId);

        ReconcileSyncedVariantLinks();

        foreach(var kv in SyncedPixs.ToList()) {
            if(!incoming.TryGetValue(kv.Key, out var dto)) {
                if(SpawnedPixs.TryGetValue(kv.Key, out var spawned)) {
                    SpawnedPixs.Remove(kv.Key);
                    PixDespawned?.Invoke(spawned, false);
                }

                SyncedPixs.Remove(kv.Key);
                continue;
            }

            var variant = GetVariant(kv.Key, true)!;
            variant.IsSynced = true;
            variant.LastSeenUtc = now;

            ApplySyncedPixState(kv.Value, dto, variant);
        }

        foreach(var dto in pixs) {
            if(SyncedPixs.ContainsKey(dto.PixId)) continue;

            var variant = GetVariant(dto.PixId, true)!;
            variant.IsSynced = true;
            variant.LastSeenUtc = now;

            var synced = new SyncedPix {
                Id = dto.PixId,
                OwnerAlias = dto.OwnerAlias,
                OwnerAliasStyle = dto.OwnerAliasStyle,
                OwnerPixStyle = dto.OwnerPixStyle,
                SelfRank = dto.SelfRank
            };
            ApplySyncedPixState(synced, dto, variant);
            SyncedPixs[dto.PixId] = synced;
        }

        ReevaluateCurrentTerritory(false, false);
    }

    public void AddOrUpdateSyncedPix(SubbedPixQueryItemDto? pix) {
        if(pix == null) return;
        var now = DateTime.UtcNow;

        ReconcileSyncedVariantLinks();

        if(SyncedPixs.ContainsKey(pix.PixId)) return;

        var variant = GetVariant(pix.PixId, true)!;
        variant.IsSynced = true;
        variant.LastSeenUtc = now;

        var synced = new SyncedPix { Id = pix.PixId, SelfRank = pix.SelfRank };
        ApplySyncedPixState(synced, pix, variant);
        SyncedPixs[pix.PixId] = synced;

        Enable(synced);
    }

    public bool CanSyncEdit(IPix? pix) => pix is SyncedPix synced && synced.CanSyncEdit;

    public void UpdateUri(IPix? pix, bool isNavigationResponse, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;

        if(isNavigationResponse) {
            PublishUpdate(pix, PixUpdateType.Uri, origin, editFinished: true, saveConfig: true, raiseEvent: false);
            return;
        }

        PublishUpdate(pix, PixUpdateType.Uri, origin, editFinished: true);
    }

    public void UpdateTerritory(IPix? pix, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.Territory, origin, editFinished: true);
    }

    public void UpdateBrowserProperties(IPix? pix, bool editFinished, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.BrowserProperties, origin, editFinished);
    }

    public void UpdateRendererTransform(IPix? pix, bool editFinished, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.RendererTransform, origin, editFinished);
    }

    public void UpdateRendererProperties(IPix? pix, bool editFinished, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.RendererProperties, origin, editFinished);
    }

    public void UpdateLightTransform(IPix? pix, bool editFinished, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.LightTransform, origin, editFinished);
    }

    public void UpdateLightProperties(IPix? pix, bool editFinished = true, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.LightProperties, origin, editFinished);
    }

    public void UpdateAudio(IPix? pix, bool editFinished = true, PixUpdateOrigin origin = PixUpdateOrigin.Local) {
        if(pix == null) return;
        PublishUpdate(pix, PixUpdateType.AudioProperties, origin, editFinished);
    }

    private void PublishUpdate(IPix pix, PixUpdateType type, PixUpdateOrigin origin, bool editFinished, bool saveConfig = true, bool raiseEvent = true) {
        if(saveConfig && editFinished) Config.Save();
        if(!raiseEvent) return;
        PixUpdated?.Invoke(new(pix, type, origin, editFinished));
    }

    public PixDto BuildPixDto(IPix pix) => new() {
        Version = pix.Version,
        Browser = pix.Browser.ToSynced(),
        Renderer = pix.Renderer.ToSynced(),
        Light = pix.Light.ToSynced(),
        Audio = pix.Audio.ToSynced()
    };

    public bool ApplyPixPropertyUpdate(BaseSyncedPixUpdate update) {
        if(string.IsNullOrWhiteSpace(update.PixId)) return false;
        if(!SyncedPixs.TryGetValue(update.PixId, out var syncedPix)) return false;
        if(syncedPix.SourcePix == null) syncedPix.SourcePix = new PixDto();

        switch(update.UpdateType) {
            case PixUpdateType.InfoProperties: (update as SyncedPixUpdateInfoProperties)?.Info?.ApplyTo(syncedPix.Info); break;
            case PixUpdateType.Uri:
            case PixUpdateType.BrowserProperties: syncedPix.SourcePix.Browser = CloneOrNew((update as SyncedPixUpdateBrowserProperties)?.Browser); break;
            case PixUpdateType.RendererTransform:
            case PixUpdateType.RendererProperties: syncedPix.SourcePix.Renderer = CloneOrNew((update as SyncedPixUpdateRendererProperties)?.Renderer); break;
            case PixUpdateType.LightTransform:
            case PixUpdateType.LightProperties: syncedPix.SourcePix.Light = CloneOrNew((update as SyncedPixUpdateLightProperties)?.Light); break;
            case PixUpdateType.AudioProperties: syncedPix.SourcePix.Audio = CloneOrNew((update as SyncedPixUpdateAudioProperties)?.Audio); break;
            case PixUpdateType.SyncProperties: (update as SyncedPixUpdateSyncProperties)?.Sync?.ApplyTo(syncedPix.Sync); break;
            default:
                var u = update as SyncedPixUpdate;
                (update as SyncedPixUpdateInfoProperties)?.Info?.ApplyTo(syncedPix.Info);
                syncedPix.SourcePix.Browser = CloneOrNew(u?.Browser);
                syncedPix.SourcePix.Renderer = CloneOrNew(u?.Renderer);
                syncedPix.SourcePix.Light = CloneOrNew(u?.Light);
                syncedPix.SourcePix.Audio = CloneOrNew(u?.Audio);
                (update as SyncedPixUpdateSyncProperties)?.Sync?.ApplyTo(syncedPix.Sync);
                break;
        }

        RebuildSyncedEffectiveState(syncedPix);

        Config.Save();
        if(IsSpawned(syncedPix)) {
            PixUpdated?.Invoke(new(syncedPix, update.UpdateType, PixUpdateOrigin.Remote, true));
        }
        return true;
    }

    public void ApplyPixStyleUpdate(SubbedPixStyleUpdateDto styleUpdate) {
        foreach(var syncedPix in SyncedPixs.Values) {
            if(syncedPix.OwnerId != styleUpdate.OwnerId) continue;
            syncedPix.OwnerAlias = styleUpdate.OwnerAlias;
            syncedPix.OwnerAliasStyle = styleUpdate.OwnerAliasStyle;
            syncedPix.OwnerPixStyle = styleUpdate.OwnerPixStyle;
        }
    }

    private static PixDto ClonePixDto(PixDto source) {
        return new PixDto {
            Version = source.Version,
            Browser = CloneOrNew(source.Browser),
            Renderer = CloneOrNew(source.Renderer),
            Light = CloneOrNew(source.Light),
            Audio = CloneOrNew(source.Audio)
        };
    }

    private void ApplyCreatedSyncedPixData(BasePix target, PixDto source, SyncedPixMetaDto meta) {
        target.Version = source.Version;
        meta.ApplyTo(target.Info, target.Sync);
        meta.Territory?.ApplyTo(target.Territory);
        source.Browser?.ApplyTo(target.Browser);
        source.Renderer?.ApplyTo(target.Renderer);
        source.Light?.ApplyTo(target.Light);
        source.Audio?.ApplyTo(target.Audio);

        if(target is SyncedPix synced) {
            synced.SourcePix = ClonePixDto(source);
        }
    }

    private void ApplySyncedPixState(BasePix target, SubbedPixQueryItemDto source, PixVariant variant) {
        source.Meta.ApplyTo(target.Info, target.Sync);
        source.Meta.Territory.ApplyTo(target.Territory);
        if(target is SyncedPix synced) {
            synced.OwnerId = source.OwnerId;
            synced.OwnerAlias = source.OwnerAlias;
            synced.OwnerAliasStyle = source.OwnerAliasStyle;
            synced.OwnerPixStyle = source.OwnerPixStyle;
            synced.SelfRank = source.SelfRank;
            synced.SourcePix = ClonePixDto(source.Pix);
        }
        source.Pix.Browser?.ApplyTo(target.Browser);
        source.Pix.Renderer?.ApplyTo(target.Renderer);
        source.Pix.Light?.ApplyTo(target.Light);
        source.Pix.Audio?.ApplyTo(target.Audio);
        variant.Browser?.ApplyTo(target.Browser);
        variant.Renderer?.ApplyTo(target.Renderer);
        variant.Light?.ApplyTo(target.Light);
        variant.Audio?.ApplyTo(target.Audio);
        target.Sync.IsSynced = true;
    }

    private void RebuildSyncedEffectiveState(SyncedPix synced) {
        synced.SourcePix.Browser?.ApplyTo(synced.Browser);
        synced.SourcePix.Renderer?.ApplyTo(synced.Renderer);
        synced.SourcePix.Light?.ApplyTo(synced.Light);
        synced.SourcePix.Audio?.ApplyTo(synced.Audio);
        var variant = TryGetVariant(synced);
        if(variant?.Browser?.HasAny == true) variant?.Browser?.ApplyTo(synced.Browser);
        if(variant?.Renderer?.HasAny == true) variant?.Renderer?.ApplyTo(synced.Renderer);
        if(variant?.Light?.HasAny == true) variant?.Light?.ApplyTo(synced.Light);
        if(variant?.Audio?.HasAny == true) variant?.Audio?.ApplyTo(synced.Audio);
    }

    private void CleanupPixVariants() {
        var now = DateTime.UtcNow;
        var changed = false;
        var contentId = StateService.LocalPlayerContentId;
        if (!Config.PixVariants.TryGetValue(contentId, out var variants)) return;
        foreach(var kv in variants.ToList()) {
            var variant = kv.Value;
            if(variant.IsSynced) {
                if(!variant.Active && (now - variant.LastSeenUtc) > SyncedVariantRetention) {
                    variants.Remove(kv.Key);
                    changed = true;
                }
            } else {
                var sourceExists = LocalPixs.Any(x => x.Id == kv.Key);
                if(!variant.Active && !sourceExists) {
                    variants.Remove(kv.Key);
                    changed = true;
                }
            }
        }
        if(changed) Config.Save();
    }

    public string CopyPixToClipboard(IPix? pix) {
        if(pix == null) return string.Empty;

        var export = new Pix {
            Version = pix.Version,
            Info = pix.Info ?? new InfoPixProperties(),
            Browser = pix.Browser ?? new BrowserPixProperties(),
            Territory = pix.Territory ?? new TerritoryPixProperties(),
            Renderer = pix.Renderer ?? new RendererPixProperties(),
            Light = pix.Light ?? new LightPixProperties(),
            Audio = pix.Audio ?? new AudioPixProperties()
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var compressed = GzipCompress(bytes);
        var base64 = Convert.ToBase64String(compressed);
        var result = PixClipboardPrefix + base64;

        try { ImGui.SetClipboardText(result); } catch { }

        return result;
    }

    public IPix? PastePixFromClipboard(IPix? target = null) {
        var code = ImGui.GetClipboardText();
        if(string.IsNullOrWhiteSpace(code)) {
            try { code = ImGui.GetClipboardText() ?? string.Empty; } catch { code = string.Empty; }
        }

        if(string.IsNullOrWhiteSpace(code)) return null;
        if(!code.StartsWith(PixClipboardPrefix, StringComparison.Ordinal)) return null;

        var base64 = code.Substring(PixClipboardPrefix.Length);
        byte[] compressed;
        try {
            compressed = Convert.FromBase64String(base64);
        } catch {
            return null;
        }

        byte[] jsonBytes;
        try {
            jsonBytes = GzipDecompress(compressed);
        } catch {
            return null;
        }

        Pix? export;
        try {
            var json = Encoding.UTF8.GetString(jsonBytes);
            export = JsonSerializer.Deserialize<Pix>(json, JsonOptions);
        } catch {
            return null;
        }

        if(export == null) return null;
        if(export.Info == null && export.Browser == null && (export.Renderer == null || export.Territory == null))
            return null;

        if(target != null) {
            if(target is BasePix bp) {
                ApplyExportToExisting(bp, export);
                Config.Save();

                if(IsSpawned(target)) {
                    PixUpdated?.Invoke(new(target, PixUpdateType.All, PixUpdateOrigin.Local, true));
                }
                return target;
            } else {
                return null;
            }
        } else {
            var created = new LocalPix(GenerateId(), StateService);

            ApplyExportToExisting(created, export);

            LocalPixs.Add(created);
            Enable(created);
            Config.Save();

            return created;
        }
    }

    private const string Base36Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public string GenerateId() {
        ulong contentId = Services.PlayerState.ContentId;
        ulong time = (ulong)DateTime.UtcNow.Ticks;
        ulong random = (ulong)Random.Shared.NextInt64();

        ulong seed = contentId;
        seed ^= time + 0x9E3779B97F4A7C15UL;
        seed ^= random + (seed << 6) + (seed >> 2);

        Span<char> chars = stackalloc char[9];
        for(int i = 0; i < chars.Length; i++) {
            seed ^= seed >> 12;
            seed ^= seed << 25;
            seed ^= seed >> 27;
            seed *= 0x2545F4914F6CDD1DUL;
            chars[i] = Base36Chars[(int)(seed % 36)];
        }

        return $"{NameUtil.PixIdLocalPrefix}{new string(chars)}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IncludeFields = true
    };
    private static T CloneOrNew<T>(T? source) where T : class, new() {
        if(source == null) return new T();
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    }
    private static byte[] GzipCompress(byte[] data) {
        using var outMs = new MemoryStream();
        using(var gzip = new GZipStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return outMs.ToArray();
    }
    private static byte[] GzipDecompress(byte[] compressed) {
        using var inMs = new MemoryStream(compressed);
        using var gzip = new GZipStream(inMs, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gzip.CopyTo(outMs);
        return outMs.ToArray();
    }
    private void ApplyExportToExisting(BasePix target, Pix export) {
        target.Version = export.Version;

        target.Info = CloneOrNew(export.Info);
        target.Browser = CloneOrNew(export.Browser);
        target.Territory = CloneOrNew(export.Territory);
        target.Renderer = CloneOrNew(export.Renderer);
        target.Light = CloneOrNew(export.Light);
        target.Audio = CloneOrNew(export.Audio);
    }

    private PixFieldBinding<T> BindField<TProps, TOverrides, T>(
        IPix pix,
        Func<BasePix, TProps> livePropsSelector,
        Func<PixVariant, TOverrides> ensureOverrides,
        Func<PixVariant, TOverrides?> tryOverrides,
        Func<TProps, T> liveGetter,
        Action<TProps, T> liveSetter,
        Func<TOverrides, T?> overrideGetter,
        Action<TOverrides, T?> overrideSetter,
        PixUpdateType updateType)
        where T : struct
        where TOverrides : class {

        var runtime = ResolveRuntimePix(pix) as BasePix ?? throw new InvalidOperationException("Pix Invalid");

        var synced = runtime as SyncedPix;
        var canSyncEdit = synced?.CanSyncEdit == true;
        var useOverride = synced != null && !canSyncEdit;

        var variant = useOverride ? EnsureVariant(runtime) : null;
        var overrides = variant != null ? tryOverrides(variant) : null;

        var liveProps = livePropsSelector(runtime);
        var hasOverride = overrides != null && overrideGetter(overrides).HasValue;
        var effective = hasOverride ? overrideGetter(overrides)!.Value : liveGetter(liveProps);

        return new PixFieldBinding<T>(
            effective,
            hasOverride,
            canSyncEdit,
            commit: (value, editFinished) => {
                if(useOverride) {
                    var v = EnsureVariant(runtime);
                    var o = ensureOverrides(v);
                    overrideSetter(o, value);
                    SaveVariant(v, editFinished);

                    if(synced != null) {
                        RebuildSyncedEffectiveState(synced);
                    }

                    PublishUpdate(runtime, updateType, PixUpdateOrigin.Local, editFinished, saveConfig: false);
                    return;
                }

                liveSetter(livePropsSelector(runtime), value);
                PublishUpdate(runtime, updateType, PixUpdateOrigin.Local, editFinished);
            },
            clearOverride: editFinished => {
                if(!useOverride || variant == null) return;

                var existing = tryOverrides(variant);
                if(existing == null) return;

                overrideSetter(existing, null);
                SaveVariant(variant, editFinished);

                if(synced != null) {
                    RebuildSyncedEffectiveState(synced);
                    PublishUpdate(synced, updateType, PixUpdateOrigin.Local, editFinished, saveConfig: false);
                }
            }
        );
    }

    public PixFieldBinding<T> BindBrowserField<T>(
        IPix pix,
        Func<BrowserPixProperties, T> liveGetter,
        Action<BrowserPixProperties, T> liveSetter,
        Func<BrowserPixVariantOverrides, T?> overrideGetter,
        Action<BrowserPixVariantOverrides, T?> overrideSetter) where T : struct {

        return BindField<BrowserPixProperties, BrowserPixVariantOverrides, T>(
            pix,
            p => p.Browser,
            v => v.EnsureBrowser(),
            v => v.Browser,
            liveGetter,
            liveSetter,
            overrideGetter,
            overrideSetter,
            PixUpdateType.BrowserProperties);
    }

    public PixFieldBinding<T> BindRendererTransformField<T>(
        IPix pix,
        Func<RendererPixProperties, T> liveGetter,
        Action<RendererPixProperties, T> liveSetter,
        Func<RendererPixVariantOverrides, T?> overrideGetter,
        Action<RendererPixVariantOverrides, T?> overrideSetter) where T : struct {

        return BindField<RendererPixProperties, RendererPixVariantOverrides, T>(
            pix,
            p => p.Renderer,
            v => v.EnsureRenderer(),
            v => v.Renderer,
            liveGetter,
            liveSetter,
            overrideGetter,
            overrideSetter,
            PixUpdateType.RendererTransform);
    }

    public PixFieldBinding<T> BindRendererPropertyField<T>(
        IPix pix,
        Func<RendererPixProperties, T> liveGetter,
        Action<RendererPixProperties, T> liveSetter,
        Func<RendererPixVariantOverrides, T?> overrideGetter,
        Action<RendererPixVariantOverrides, T?> overrideSetter) where T : struct {

        return BindField<RendererPixProperties, RendererPixVariantOverrides, T>(
            pix,
            p => p.Renderer,
            v => v.EnsureRenderer(),
            v => v.Renderer,
            liveGetter,
            liveSetter,
            overrideGetter,
            overrideSetter,
            PixUpdateType.RendererProperties);
    }

    public PixFieldBinding<T> BindLightTransformField<T>(
        IPix pix,
        Func<LightPixProperties, T> liveGetter,
        Action<LightPixProperties, T> liveSetter,
        Func<LightPixVariantOverrides, T?> overrideGetter,
        Action<LightPixVariantOverrides, T?> overrideSetter) where T : struct {

        return BindField<LightPixProperties, LightPixVariantOverrides, T>(
            pix,
            p => p.Light,
            v => v.EnsureLight(),
            v => v.Light,
            liveGetter,
            liveSetter,
            overrideGetter,
            overrideSetter,
            PixUpdateType.LightTransform);
    }

    public PixFieldBinding<T> BindLightPropertyField<T>(
        IPix pix,
        Func<LightPixProperties, T> liveGetter,
        Action<LightPixProperties, T> liveSetter,
        Func<LightPixVariantOverrides, T?> overrideGetter,
        Action<LightPixVariantOverrides, T?> overrideSetter) where T : struct {

        return BindField<LightPixProperties, LightPixVariantOverrides, T>(
            pix,
            p => p.Light,
            v => v.EnsureLight(),
            v => v.Light,
            liveGetter,
            liveSetter,
            overrideGetter,
            overrideSetter,
            PixUpdateType.LightProperties);
    }

    public PixFieldBinding<T> BindAudioField<T>(
        IPix pix,
        Func<AudioPixProperties, T> liveGetter,
        Action<AudioPixProperties, T> liveSetter,
        Func<AudioPixVariantOverrides, T?> overrideGetter,
        Action<AudioPixVariantOverrides, T?> overrideSetter) where T : struct {

        return BindField<AudioPixProperties, AudioPixVariantOverrides, T>(
            pix,
            p => p.Audio,
            v => v.EnsureAudio(),
            v => v.Audio,
            liveGetter,
            liveSetter,
            overrideGetter,
            overrideSetter,
            PixUpdateType.AudioProperties);
    }

    public OwnerFieldBinding<T> BindOwnerField<T>(
    IPix pix,
    Func<BasePix, T> getter,
    Action<BasePix, T> setter
) {
        var runtime = ResolveRuntimePix(pix) as BasePix
            ?? throw new InvalidOperationException("Pix Invalid");

        var canEdit = runtime is not SyncedPix synced || synced.CanSyncEdit;

        return new OwnerFieldBinding<T>(
            getter(runtime),
            canEdit,
            (value, editFinished) => {
                if(!canEdit) return;

                setter(runtime, value);

                if(editFinished) {
                    Config.Save();
                }
            }
        );
    }

    public void UpdateInfoProperties(IPix? pix, bool editFinished = true) {
        if(pix == null) return;
        if(editFinished) Config.Save();
        if(editFinished && pix is SyncedPix synced && synced.SelfRank == PixRank.Owner) {
            PixUpdated?.Invoke(new(pix, PixUpdateType.InfoProperties, PixUpdateOrigin.Local, true));
        }
    }

    public void UpdateSyncProperties(IPix? pix, bool editFinished = true) {
        if(pix == null) return;
        if(editFinished) Config.Save();
        if(editFinished && pix is SyncedPix synced && synced.SelfRank == PixRank.Owner) {
            PixUpdated?.Invoke(new(pix, PixUpdateType.SyncProperties, PixUpdateOrigin.Local, true));
        }
    }
}

public sealed class PixFieldBinding<T> {
    public T Value { get; private set; }
    public bool HasOverride { get; private set; }
    public bool CanSyncEdit { get; }

    private readonly Action<T, bool> _commit;
    private readonly Action<bool> _clearOverride;

    public PixFieldBinding(T value, bool hasOverride, bool canSyncEdit, Action<T, bool> commit, Action<bool> clearOverride) {
        Value = value;
        HasOverride = hasOverride;
        CanSyncEdit = canSyncEdit;
        _commit = commit;
        _clearOverride = clearOverride;
    }

    public void Commit(T value, bool editFinished = true) {
        Value = value;
        if(!CanSyncEdit) HasOverride = true;
        _commit(value, editFinished);
    }

    public void ResetOverride(bool editFinished = true) {
        HasOverride = false;
        _clearOverride(editFinished);
    }
}

public sealed class OwnerFieldBinding<T> {
    public T Value { get; private set; }
    public bool CanEdit { get; }

    private readonly Action<T, bool> _commit;

    public OwnerFieldBinding(T value, bool canEdit, Action<T, bool> commit) {
        Value = value;
        CanEdit = canEdit;
        _commit = commit;
    }

    public void Commit(T value, bool editFinished = true) {
        Value = value;
        _commit(value, editFinished);
    }
}
