using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PyonPix.Config;
using PyonPix.Structs.Data;

namespace PyonPix.Services.Core;

public enum UDFRemovalResult {
    Success,
    Failed,
    Cancelled
}

public class DataService(Configuration config, IServiceContext services) : BaseService(config, services) {
    private PixService PixService => Services.Get<PixService>();

    private string DataRootPath => Path.Combine(Config.GetConfigPath(), "Data", "Profiles");

    private readonly object _lock = new();
    private List<UDF> UDFCache = [];

    private readonly ConcurrentDictionary<string, CancellationTokenSource> PendingRemovals = new();
    private readonly int RemovalAttempts = 5;
    private readonly int RenameAttempts = 8;
    private readonly int InitialRemovalDelay = 200;
    private readonly int InitialRenameDelay = 250;
    private readonly int MaxRemovalDelay = 3000;
    private readonly int MaxRenameDelay = 3000;

    public event Action? OnUDFCacheUpdated;
    public event Action<string, UDFRemovalResult>? OnUDFRemovalCompleted;

    public override Task Initialize() {
        return Task.CompletedTask;
    }

    public List<UDF> GetUDFSnapshot() {
        lock(_lock) {
            return UDFCache.Select(c => new UDF {
                FolderName = c.FolderName,
                FolderPath = c.FolderPath,
                PixId = c.PixId,
                PixName = c.PixName,
                PersistentCache = c.PersistentCache,
                SizeBytes = c.SizeBytes,
                LastWriteUtc = c.LastWriteUtc,
                IsRemoving = c.IsRemoving,
                PixExists = c.PixExists
            }).ToList();
        }
    }

    public async Task RefreshCacheAsync(CancellationToken? token = null) {
        var ct = token ?? CancellationToken.None;

        try {
            var newList = new List<UDF>();

            if(!Directory.Exists(DataRootPath)) {
                lock(_lock) { UDFCache = newList; }
                OnUDFCacheUpdated?.Invoke();
                return;
            }

            var dirs = Directory.EnumerateDirectories(DataRootPath);
            foreach(var dir in dirs) {
                ct.ThrowIfCancellationRequested();

                var folderName = Path.GetFileName(dir) ?? "";
                if(string.Equals(folderName, "PIX", StringComparison.OrdinalIgnoreCase)) continue;

                var p = PixService.GetPix(folderName);
                var v = PixService.GetVariant(p, false);
                var entry = new UDF() {
                    FolderName = folderName,
                    FolderPath = dir,
                    PixId = folderName,
                    PixName = p?.Info.Name,
                    PersistentCache = v?.PersistentCache ?? false,
                    SizeBytes = -1,
                    LastWriteUtc = null,
                    IsRemoving = false,
                    PixExists = p != null
                };

                try {
                    var info = new DirectoryInfo(dir);
                    entry.LastWriteUtc = info.LastWriteTimeUtc;
                } catch {
                    entry.LastWriteUtc = null;
                }

                newList.Add(entry);
            }

            var sizeTasks = newList.Select(e => Task.Run(async () => {
                try {
                    e.SizeBytes = await ComputeDirectorySizeAsync(e.FolderPath, ct).ConfigureAwait(false);
                } catch(OperationCanceledException) {
                    throw;
                } catch(Exception ex) {
                    Services.Log.Error(ex, $"[Data] Size calc failed for {e.FolderPath}");
                    e.SizeBytes = -1;
                }
            }, ct)).ToArray();

            await Task.WhenAll(sizeTasks).ConfigureAwait(false);

            lock(_lock) {
                foreach(var existing in UDFCache) {
                    if(existing.IsRemoving) {
                        var found = newList.FirstOrDefault(x => x.FolderName == existing.FolderName);
                        found?.IsRemoving = true;
                    }
                }

                foreach(var kv in PendingRemovals) {
                    var found = newList.FirstOrDefault(x => x.FolderName == kv.Key);
                    found?.IsRemoving = true;
                }

                UDFCache = newList.OrderByDescending(x => x.LastWriteUtc).ToList();
            }
            OnUDFCacheUpdated?.Invoke();
        } catch(OperationCanceledException) {
        } catch(Exception ex) {
            Services.Log.Error(ex, "[Data] RefreshCacheAsync failed");
        }
    }

    public void SetPersistent(string pixId, bool persistent) {
        if(string.IsNullOrWhiteSpace(pixId)) return;

        var v = PixService.GetVariant(pixId, false);
        if(v != null) {
            v.PersistentCache = persistent;
            Config.Save();
        }

        lock(_lock) {
            var found = UDFCache.FirstOrDefault(x => x.PixId == pixId);
            found?.PersistentCache = persistent;
        }

        OnUDFCacheUpdated?.Invoke();
    }

    public void RemoveUDF(string pixId) {
        if(string.IsNullOrWhiteSpace(pixId)) return;

        lock(_lock) {
            var found = UDFCache.FirstOrDefault(x => x.PixId == pixId);
            if(found != null) {
                found?.IsRemoving = true;
            } else {
                var p = PixService.GetPix(pixId);
                var v = PixService.GetVariant(p, false);
                UDFCache.Add(new UDF {
                    FolderName = pixId,
                    FolderPath = Path.Combine(DataRootPath, pixId),
                    PixId = pixId,
                    PixName = p?.Info.Name,
                    PersistentCache = v?.PersistentCache ?? false,
                    SizeBytes = -1,
                    LastWriteUtc = null,
                    IsRemoving = true,
                    PixExists = p != null
                });
                UDFCache = UDFCache.OrderByDescending(x => x.LastWriteUtc).ToList();
            }
        }
        OnUDFCacheUpdated?.Invoke();

        var cts = new CancellationTokenSource();
        if(!PendingRemovals.TryAdd(pixId, cts)) {
            try { cts.Dispose(); } catch { }
            return;
        }

        Task.Run(async () => {
            var result = UDFRemovalResult.Failed;
            try {
                bool res = await RemoveUDFWithRetriesAsync(pixId, cts.Token).ConfigureAwait(false);
                result = res ? UDFRemovalResult.Success : UDFRemovalResult.Failed;
            } catch(OperationCanceledException) {
                result = UDFRemovalResult.Cancelled;
            } catch {
                result = UDFRemovalResult.Failed;
            } finally {
                if(PendingRemovals.TryRemove(pixId, out var existing)) {
                    try { existing.Dispose(); } catch {}
                }

                lock(_lock) {
                    var entry = UDFCache.FirstOrDefault(x => x.PixId == pixId);
                    if(result == UDFRemovalResult.Success) {
                        if(entry != null) UDFCache.Remove(entry);
                    } else {
                        entry?.IsRemoving = false;
                    }
                }

                OnUDFRemovalCompleted?.Invoke(pixId, result);
                OnUDFCacheUpdated?.Invoke();
            }
        }, cts.Token);
    }

    public void CancelPendingRemoval(string pixId) {
        if(string.IsNullOrWhiteSpace(pixId)) return;
        if(PendingRemovals.TryRemove(pixId, out var cts)) {
            try {
                cts.Cancel();
            } catch {
            } finally {
                try { cts.Dispose(); } catch { }
            }
        }

        lock(_lock) {
            var entry = UDFCache.FirstOrDefault(x => x.PixId == pixId);
            entry?.IsRemoving = false;
        }
        OnUDFCacheUpdated?.Invoke();
    }

    public async Task<bool> RenameUDFAsync(string fromPixId, string toPixId, CancellationToken? token = null) {
        if(string.IsNullOrWhiteSpace(fromPixId) || string.IsNullOrWhiteSpace(toPixId)) return false;
        if(string.Equals(fromPixId, toPixId, StringComparison.OrdinalIgnoreCase)) return true;

        var ct = token ?? CancellationToken.None;
        var fromPath = Path.Combine(DataRootPath, fromPixId);
        var toPath = Path.Combine(DataRootPath, toPixId);

        Directory.CreateDirectory(DataRootPath);

        if(!Directory.Exists(fromPath)) return !Directory.Exists(toPath);

        if(Directory.Exists(toPath)) {
            Services.Log.Warning($"[Data] Rename skipped, destination already exists: {toPath}");
            return false;
        }

        int attempt = 0;
        int delay = InitialRenameDelay;
        while(!ct.IsCancellationRequested) {
            attempt++;
            if(TryRenameUDF(fromPath, toPath)) {
                lock(_lock) {
                    var entry = UDFCache.FirstOrDefault(x => x.PixId == fromPixId);
                    if(entry != null) {
                        entry.PixId = toPixId;
                        entry.FolderName = toPixId;
                        entry.FolderPath = toPath;
                    }
                }
                OnUDFCacheUpdated?.Invoke();
                return true;
            }

            if(attempt >= RenameAttempts) return false;

            try {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            } catch(OperationCanceledException) {
                ct.ThrowIfCancellationRequested();
            }

            delay = Math.Min(delay * 2, MaxRenameDelay);
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }

    private static bool TryRenameUDF(string fromPath, string toPath) {
        try {
            if(!Directory.Exists(fromPath)) return true;
            if(Directory.Exists(toPath)) return false;
            Directory.Move(fromPath, toPath);
            return Directory.Exists(toPath);
        } catch {
            return false;
        }
    }

    private bool TryRemoveUDF(string folderPath) {
        try {
            if(!Directory.Exists(folderPath)) return true;
            Directory.Delete(folderPath, true);
            return !Directory.Exists(folderPath);
        } catch {
            return false;
        }
    }

    private async Task<bool> RemoveUDFWithRetriesAsync(string pixId, CancellationToken ct) {
        if(ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
        var udfPath = Path.Combine(DataRootPath, pixId);

        int attempt = 0;
        int delay = InitialRemovalDelay;
        while(!ct.IsCancellationRequested) {
            attempt++;
            if(!Directory.Exists(udfPath)) return true;
            if(TryRemoveUDF(udfPath)) return true;
            if(attempt >= RemovalAttempts) return false;

            try {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            } catch(OperationCanceledException) {
                ct.ThrowIfCancellationRequested();
            }
            delay = Math.Min(delay * 2, MaxRemovalDelay);
        }
        ct.ThrowIfCancellationRequested();
        return false;
    }

    private static async Task<long> ComputeDirectorySizeAsync(string path, CancellationToken ct) {
        if(string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
        return await Task.Run(() => {
            long size = 0;
            try {
                foreach(var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) {
                    ct.ThrowIfCancellationRequested();
                    try {
                        var fi = new FileInfo(file);
                        size += fi.Length;
                    } catch {}
                }
            } catch {}
            return size;
        }, ct).ConfigureAwait(false);
    }

    public override Task Dispose() {
        foreach(var kv in PendingRemovals) {
            try {
                kv.Value.Cancel();
                kv.Value.Dispose();
            } catch { }
        }
        PendingRemovals.Clear();
        return Task.CompletedTask;
    }
}
