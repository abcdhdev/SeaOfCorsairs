using System.Text.Json;

namespace SeaWars.PlayerDataService.Contracts;

public sealed record ErrorResponse(string Code, string Message);

public sealed record PlayerStateResponse(int Version, JsonElement State, DateTimeOffset UpdatedAt);

public sealed record UpdatePlayerStateRequest(JsonElement State, int? ExpectedVersion);

public sealed record PlayerWalletResponse(int Gold, int Diamond, int Experience, int Version, DateTimeOffset UpdatedAt);

public sealed record UpdatePlayerWalletRequest(int Gold, int Diamond, int Experience, int? ExpectedVersion);

public sealed record PurchaseCannonRequest(string CannonId, int Gold, int Diamond, int? ExpectedVersion);

public sealed record CannonPurchaseResponse(string CannonId, string[] OwnedCannonIds, int Gold, int Diamond, int Version, DateTimeOffset UpdatedAt);

public sealed record PurchaseShipRequest(string ShipId, int Gold, int Diamond, int? ExpectedVersion);

public sealed record ShipPurchaseResponse(string ShipId, string[] OwnedShipIds, int Gold, int Diamond, int Version, DateTimeOffset UpdatedAt);

public sealed record CreateGuildRequest(string Name, string? Tag, string? Description);

public sealed record GuildSummaryResponse(
    string Id,
    string Name,
    string Tag,
    string Description,
    string LeaderUserId,
    string LeaderDisplayName,
    int MemberCount,
    bool IsCurrentPlayerMember,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GuildListResponse(string CurrentGuildId, GuildSummaryResponse[] Guilds);

public sealed record ConflictResponse(int CurrentVersion, DateTimeOffset UpdatedAt);

public sealed record WorldObjectResponse(
    Guid Id,
    string ObjectType,
    string OwnerEntityId,
    JsonElement State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateWorldObjectRequest(string ObjectType, string OwnerEntityId, JsonElement State);

public sealed record UpdateWorldObjectRequest(JsonElement State);

public sealed record PresignLogUploadRequest(string FileName, string? ContentType);

public sealed record PresignResponse(string Url, string Method, string? ContentType, string ObjectKey, DateTimeOffset ExpiresAt);
