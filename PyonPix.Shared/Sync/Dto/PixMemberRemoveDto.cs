namespace PyonPix.Shared.Sync.Dto;

public sealed class PixMemberRemoveDto {
    public string PixId { get; set; } = string.Empty;
    public long CharacterId { get; set; }
}

public sealed class PixMemberRemoveSuccessDto {
    public string PixId { get; set; } = string.Empty;
    public long CharacterId { get; set; }
}

public sealed class PixMemberRemoveFailedDto {
    public string PixId { get; set; } = string.Empty;
    public long CharacterId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
