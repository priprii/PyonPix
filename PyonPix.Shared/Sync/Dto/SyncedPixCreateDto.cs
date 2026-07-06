using PyonPix.Shared.Structs.Pix;

namespace PyonPix.Shared.Sync.Dto;

public sealed class SyncedPixCreateDto {
    public string RequestId { get; set; } = string.Empty;
    public string LocalPixId { get; set; } = string.Empty;

    public PixDto Pix { get; set; } = new();
    public SyncedPixMetaDto Meta { get; set; } = new();
}

public sealed class SyncedPixCreateFailedDto(string requestId, string reason) {
    public string RequestId { get; set; } = requestId;
    public string Reason { get; set; } = reason;
}

public sealed class SyncedPixCreateSuccessDto(string requestId, string pixId, string? secretKey, int version) {
    public string RequestId { get; set; } = requestId;
    public string PixId { get; set; } = pixId;
    public string? SecretKey { get; set; } = secretKey;
    public int Version { get; set; } = version;
}

public class SyncedPixCreateResultDto {
    public string PixId { get; set; } = string.Empty;
    public string? SecretKey { get; set; }
    public int Version { get; set; }
}
