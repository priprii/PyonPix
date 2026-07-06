namespace PyonPix.Shared.Sync.Dto.Auth;

public record AuthFailedDto(AuthFailedReason Reason) { }
public enum AuthFailedReason { InvalidData, InvalidAuth, Forbidden }
