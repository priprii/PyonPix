namespace PyonPix.Shared.Sync.Dto;

public sealed class SyncedPixMembersRequestDto {
    public string PixId { get; set; } = string.Empty;
}

public sealed class SyncedPixMembersResponseDto {
    public string PixId { get; set; } = string.Empty;
    public List<SyncedPixMemberDto> Members { get; set; } = new();
}
