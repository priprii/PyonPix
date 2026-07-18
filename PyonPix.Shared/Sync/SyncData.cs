using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync.Dto;

namespace PyonPix.Shared.Sync;

public static class SyncData {
    public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] CreateMessageBuffer(MessageType type, object? data) {
        var msg = new SocketMessage(type, data == null ? null : JsonSerializer.SerializeToElement(data, JsonOptions));
        var json = JsonSerializer.Serialize(msg, JsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    public static bool TryGetMessage(string? json, out SocketMessage message) {
        message = default!;
        if(string.IsNullOrEmpty(json)) return false;
        try {
            message = JsonSerializer.Deserialize<SocketMessage>(json, JsonOptions)!;
            return message != null;
        } catch {
            return false;
        }
    }

    public static bool TryGetObject<T>(JsonElement? data, out T dto) {
        dto = default!;
        if(data == null) return false;
        try {
            dto = data.Value.Deserialize<T>(JsonOptions)!;
            return dto != null;
        } catch {
            return false;
        }
    }

    public static bool TryGetSyncedPixUpdate(JsonElement? data, out BaseSyncedPixUpdate update) {
        update = null!;
        if(data == null) return false;
        try {
            if(!data.Value.TryGetProperty("PixId", out var pixIdProp)) return false;
            if(!data.Value.TryGetProperty("UpdateType", out var typeProp)) return false;
            var pixId = pixIdProp.GetString();
            if(string.IsNullOrWhiteSpace(pixId)) return false;
            if(!Enum.TryParse<PixUpdateType>(typeProp.GetString(), true, out var updateType)) return false;

            BaseSyncedPixUpdate? result = updateType switch {
                PixUpdateType.All => data.Value.Deserialize<SyncedPixUpdate>(JsonOptions),
                PixUpdateType.Uri => data.Value.Deserialize<SyncedPixUpdateUri>(JsonOptions),
                PixUpdateType.InfoProperties => data.Value.Deserialize<SyncedPixUpdateInfoProperties>(JsonOptions),
                PixUpdateType.BrowserProperties => data.Value.Deserialize<SyncedPixUpdateBrowserProperties>(JsonOptions),
                PixUpdateType.MediaState => data.Value.Deserialize<SyncedPixUpdateMediaState>(JsonOptions),
                PixUpdateType.RendererTransform or PixUpdateType.RendererProperties => data.Value.Deserialize<SyncedPixUpdateRendererProperties>(JsonOptions),
                PixUpdateType.LightTransform or PixUpdateType.LightProperties => data.Value.Deserialize<SyncedPixUpdateLightProperties>(JsonOptions),
                PixUpdateType.AudioProperties => data.Value.Deserialize<SyncedPixUpdateAudioProperties>(JsonOptions),
                PixUpdateType.SyncProperties => data.Value.Deserialize<SyncedPixUpdateSyncProperties>(JsonOptions),
                _ => null
            };
            if(result == null) return false;
            update = result;
            return true;
        } catch(Exception ex) {
            Console.WriteLine($"{ex.Source} Failed: {ex}");
            return false;
        }
    }
}
