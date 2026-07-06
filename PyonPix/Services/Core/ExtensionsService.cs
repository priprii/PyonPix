using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PyonPix.Config;
using PyonPix.Structs.Browser;

namespace PyonPix.Services.Core;

public class ExtensionsService(Configuration config, IServiceContext services) : BaseService(config, services) {
    private string UnpackedExtensionRootPath => Path.Combine(Config.GetConfigPath(), "Data", "Extensions");

    private HttpClient Client = null!;

    private readonly object _lock = new();

    public event Action<string[]>? OnAutoCompleteResult;
    public event Action<List<ExtensionProductDetails>>? OnSearchResult;

    public event Action<string, string>? InstallExtensionRequest;
    public event Action<string, string>? UninstallExtensionRequest;
    public event Action<string, string>? EnableExtensionRequest;
    public event Action<string, string>? DisableExtensionRequest;

    public bool IsOperating;

    public override Task Initialize() {
        Directory.CreateDirectory(UnpackedExtensionRootPath);
        ResolveUnknownExtensions();

        var handler = new HttpClientHandler() {
            AllowAutoRedirect = true
        };
        Client = new(handler);
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");

        return Task.CompletedTask;
    }

    private string GetDownloadPath(string extensionId) => Path.Combine(UnpackedExtensionRootPath, $"_{extensionId}");
    private string GetInstallPath(string extensionId) => Path.Combine(UnpackedExtensionRootPath, extensionId);

    public void ResolveUnknownExtensions() {
        if(!Directory.Exists(UnpackedExtensionRootPath)) return;

        var dirs = Directory.EnumerateDirectories(UnpackedExtensionRootPath);
        foreach(var dir in dirs) {
            var folderName = Path.GetFileName(dir) ?? string.Empty;
            if(GetExtension(folderName) != null) continue;
            var manifestPath = Path.Combine(dir, "manifest.json");
            if(!File.Exists(manifestPath)) continue;

            try {
                var json = File.ReadAllText(manifestPath);
                if(string.IsNullOrWhiteSpace(json)) continue;
                var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json);
                if(manifest == null) continue;

                var ext = new Extension() {
                    CrxId = folderName,
                    Name = manifest.Name ?? string.Empty,
                    Developer = manifest.Developer ?? string.Empty,
                    Version = manifest.Version ?? string.Empty,
                    LastUpdated = DateTime.UtcNow,
                    IsDownloaded = true
                };

                AddOrUpdateConfigExtension(ext);
            } catch { continue; }
        }
    }

    public Extension? GetExtension(string extensionId) {
        if(string.IsNullOrWhiteSpace(extensionId)) return null;
        lock(_lock) {
            Config.Extensions.TryGetValue(extensionId, out var e);
            return e;
        }
    }
    public string[] GetExtensionsToInstall() {
        lock(_lock) {
            var extensions = Config.Extensions.Where(kv => kv.Value.IsInstalled).Select(kv => kv.Key).ToArray();
            foreach(var id in extensions) {
                if(Config.Extensions.TryGetValue(id, out var e)) {
                    e.IsInstalled = true;
                }
            }
            Config.Save();
            return extensions ?? [];
        }
    }
    private Extension EnsureExtension(string crxId) {
        lock(_lock) {
            if(!Config.Extensions.TryGetValue(crxId, out var e)) {
                e = new Extension { CrxId = crxId };
                Config.Extensions[crxId] = e;
            }
            return e;
        }
    }
    public void AddOrUpdateConfigExtension(Extension e) {
        lock(_lock) {
            Config.Extensions[e.CrxId] = e;
            Config.Save();
        }
    }
    public void RemoveConfigExtension(string extensionId) {
        lock(_lock) {
            if(Config.Extensions.Remove(extensionId)) {
                Config.Save();
            }
        }
    }

    public void InstallExtension(string crxId) {
        var info = GetExtension(crxId);
        if(info == null) return;
        IsOperating = true;
        InstallExtensionRequest?.Invoke(crxId, info.Name);
        info.IsInstalled = true;
        info.IsEnabled = true;
        AddOrUpdateConfigExtension(info);
    }
    public void UninstallExtension(string crxId) {
        var info = GetExtension(crxId);
        if(info == null) return;
        IsOperating = true;
        UninstallExtensionRequest?.Invoke(crxId, info.Name);
        info.IsInstalled = false;
        info.IsEnabled = false;
        AddOrUpdateConfigExtension(info);
    }
    public void EnableExtension(string crxId) {
        var info = GetExtension(crxId);
        if(info == null) return;
        IsOperating = true;
        EnableExtensionRequest?.Invoke(crxId, info.Name);
        info.IsEnabled = true;
        AddOrUpdateConfigExtension(info);
    }
    public void DisableExtension(string crxId) {
        var info = GetExtension(crxId);
        if(info == null) return;
        IsOperating = true;
        DisableExtensionRequest?.Invoke(crxId, info.Name);
        info.IsEnabled = false;
        AddOrUpdateConfigExtension(info);
    }
    public void RemoveExtension(string crxId) {
        var e = GetExtension(crxId);
        if(e == null) return;
        var installPath = GetInstallPath(crxId);
        if(string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) {
            RemoveConfigExtension(crxId);
            return;
        }
        IsOperating = true;
        TryDeleteDirectory(installPath);
        RemoveConfigExtension(crxId);
        IsOperating = false;
    }

    public async Task CheckUpdateAllAsync(bool autoUpdate, CancellationToken ct = default) {
        if(IsOperating) return;
        if(Config.Extensions.Count == 0) return;
        IsOperating = true;
        foreach(var ext in Config.Extensions) {
            await CheckUpdateAsync(ext.Key, autoUpdate, ct);
        }
        IsOperating = false;
    }
    private async Task CheckUpdateAsync(string crxId, bool autoUpdate, CancellationToken ct = default) {
        var ext = GetExtension(crxId);
        if(ext == null) return;

        if(!ext.IsUpdateAvailable) {
            var details = await GetProductDetailsAsync(crxId, ct);
            if(details == null) return;

            var updateAvailable = !string.Equals(ext.Version, details.Version, StringComparison.OrdinalIgnoreCase);
            if(!updateAvailable) return;

            ext.IsUpdateAvailable = true;
        }
        
        if(autoUpdate) {
            await UpdateAsync(crxId, ct);
        } else {
            AddOrUpdateConfigExtension(ext);
        }
    }

    public async Task UpdateAsync(string crxId, CancellationToken ct = default) {
        await DownloadAndExtractCrxAsync(crxId, ct).ConfigureAwait(false);
        var info = GetExtension(crxId);
        if(info != null && info.IsInstalled) {
            IsOperating = true;
            InstallExtensionRequest?.Invoke(crxId, info.Name);
            info.IsEnabled = true;
        }
        AddOrUpdateConfigExtension(info ?? EnsureExtension(crxId));
    }

    public async Task<ExtensionAutoCompleteResult?> AutoCompleteAsync(string query, CancellationToken ct = default) {
        if(string.IsNullOrWhiteSpace(query) || query.Length <= 2) {
            OnAutoCompleteResult?.Invoke([]);
            return null;
        }
        string url = $"https://microsoftedge.microsoft.com/edgestorewebautocomplete/v1/search?q={Uri.EscapeDataString(query)}";

        using var resp = await Client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<ExtensionAutoCompleteResult>(stream, JsonSerializerOptions.Default, ct).ConfigureAwait(false);
        OnAutoCompleteResult?.Invoke(result == null || result.AutoCompleteList == null ? [] : result.AutoCompleteList);
        return result;
    }

    public async Task<ExtensionSearchResult[]> SearchAsync(string query, int page = 1, CancellationToken ct = default) {
        if(string.IsNullOrWhiteSpace(query) || query.Length <= 2) {
            OnSearchResult?.Invoke([]);
            return [];
        }
        string url = $"https://microsoftedge.microsoft.com/addons/v4/getfilteredorderedsearch?filteredCategories=Edge-Extensions&filteredAddon=0&filterFeaturedAddons=false&filteredRating=0&sortBy=Relevance&pgNo={page}&Query={Uri.EscapeDataString(query)}";

        using var resp = await Client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var root = await JsonSerializer.DeserializeAsync<ExtensionSearchRoot>(stream, JsonSerializerOptions.Default, ct).ConfigureAwait(false);
        if(root?.Results == null) {
            OnSearchResult?.Invoke([]);
            return [];
        }

        var detailResults = new List<ExtensionProductDetails>();
        foreach(var result in root.Results) {
            var detailResult = await GetProductDetailsAsync(result.CrxId, ct);
            if(detailResult == null) continue;
            detailResults.Add(detailResult);
        }
        OnSearchResult?.Invoke(detailResults);
        return root.Results;
    }

    private async Task<ExtensionProductDetails?> GetProductDetailsAsync(string crxId, CancellationToken ct = default) {
        if(string.IsNullOrWhiteSpace(crxId)) return null;
        string url = $"https://microsoftedge.microsoft.com/addons/getproductdetailsbycrxid/{Uri.EscapeDataString(crxId)}";

        using var resp = await Client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var details = await JsonSerializer.DeserializeAsync<ExtensionProductDetails>(stream, JsonSerializerOptions.Default, ct).ConfigureAwait(false);
        if(details != null && details.ShortDescription != null) {
            details.ShortDescription = details.ShortDescription.Replace("\n", " ");
        }
        return details;
    }

    public async Task DownloadAndExtractCrxAsync(string crxId, CancellationToken ct = default) {
        if(string.IsNullOrWhiteSpace(crxId)) { throw new ArgumentNullException(nameof(crxId)); }
        IsOperating = true;
        string downloadUrl = $"https://edge.microsoft.com/extensionwebstorebase/v1/crx?response=redirect&x=id%3D{Uri.EscapeDataString(crxId)}%26installsource%3Dondemand%26uc";

        using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        req.Headers.Accept.ParseAdd("*/*");

        HttpResponseMessage? resp = null;
        resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if(resp.StatusCode == System.Net.HttpStatusCode.Found) {
            if(resp.Headers?.Location == null) { throw new InvalidOperationException("Redirect without Location header"); }
            resp.Dispose();
            resp = await Client.GetAsync(resp.Headers.Location, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        } else {
            try {
                resp.EnsureSuccessStatusCode();
            } catch (Exception ex) {
                Services.Log.Error(ex, "Extension Download Failed");
                IsOperating = false;
            }
        }

        using var crxStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var tempDir = GetDownloadPath(crxId);
        Directory.CreateDirectory(tempDir);
        try {
            await ExtractCrxToDirectoryAsync(crxStream, tempDir, ct).ConfigureAwait(false);

            var finalDir = GetInstallPath(crxId);
            if(Directory.Exists(finalDir)) {
                TryDeleteDirectory(finalDir);
            }
            Directory.Move(tempDir, finalDir);

            var ext = EnsureExtension(crxId);
            ext.IsDownloaded = true;
            ext.IsUpdateAvailable = false;

            var details = await GetProductDetailsAsync(crxId, ct).ConfigureAwait(false);
            if(details != null) {
                ext.Name = details.Name ?? ext.Name;
                ext.Description = details.ShortDescription ?? ext.Description;
                ext.Developer = details.DeveloperName ?? ext.Developer;
                ext.Version = details.Version ?? ext.Version;
                ext.LastUpdated = details.LastUpdateDate == null ? DateTime.UtcNow : DateTimeOffset.FromUnixTimeSeconds((long)details.LastUpdateDate);
            }

            AddOrUpdateConfigExtension(ext);
        } finally {
            if(Directory.Exists(tempDir)) {
                TryDeleteDirectory(tempDir);
            }
            resp?.Dispose();
            IsOperating = false;
        }
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if(Directory.Exists(path)) Directory.Delete(path, true);
        } catch { }
    }

    private static async Task ExtractCrxToDirectoryAsync(Stream crxStream, string targetDirectory, CancellationToken ct) {
        using var ms = new MemoryStream();
        await crxStream.CopyToAsync(ms, 81920, ct).ConfigureAwait(false);
        var data = ms.ToArray();

        // zip sig: 50 4B 03 04
        int zipStart = -1;
        for(int i = 0; i < data.Length - 4; i++) {
            if(data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x03 && data[i + 3] == 0x04) {
                zipStart = i;
                break;
            }
        }

        if(zipStart < 0) return;

        using var zipStream = new MemoryStream(data, zipStart, data.Length - zipStart);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(targetDirectory);
    }

    public override Task Dispose() {
        Client.Dispose();
        return Task.CompletedTask;
    }
}
