namespace SeaWars.PlayerDataService.Data.Entities;

public sealed class WorldObject
{
    public Guid Id { get; set; }

    public string ObjectType { get; set; } = string.Empty;

    public string OwnerEntityId { get; set; } = string.Empty;

    // Stored as jsonb in Postgres; string to keep world-object payload flexible.
    public string State { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
