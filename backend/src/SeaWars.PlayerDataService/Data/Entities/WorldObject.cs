namespace SeaWars.PlayerDataService.Data.Entities;

public sealed class WorldObject
{
    public Guid Id { get; set; }

    public string ObjectType { get; set; } = string.Empty;

    public Guid CreatorUserId { get; set; }

    // Stored as jsonb in Postgres; string to keep world-object payload flexible.
    public string State { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
