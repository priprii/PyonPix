namespace PyonPix.Shared.Sync.Dto;

public sealed class SyncedPixUnsubscribeDto {
    public string PixId { get; set; } = string.Empty;
}

public sealed class SyncedPixUnsubscribeSuccessDto {
    public string PixId { get; set; } = string.Empty;
}

public sealed class SyncedPixUnsubscribeFailedDto {
    public string PixId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
