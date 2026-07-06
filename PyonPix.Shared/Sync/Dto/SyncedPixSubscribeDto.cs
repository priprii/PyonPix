using PyonPix.Shared.Sync.Dto.Subbed;

namespace PyonPix.Shared.Sync.Dto;

public sealed class SyncedPixSubscribeDto {
    public string PixId { get; set; } = string.Empty;
    public string? SecretKey { get; set; }
}

public sealed class SyncedPixSubscribeSuccessDto {
    public SubbedPixQueryItemDto? Pix { get; set; }
}

public sealed class SyncedPixSubscribeFailedDto {
    public string PixId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
