namespace PyonPix.Shared.Sync.Dto;

public sealed class SyncedPixDeleteDto {
    public string PixId { get; set; } = string.Empty;
}

public sealed class SyncedPixDeleteSuccessDto {
    public string PixId { get; set; } = string.Empty;
}

public sealed class SyncedPixDeleteFailedDto {
    public string PixId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class SyncedPixDeletedDto {
    public string PixId { get; set; } = string.Empty;
}
