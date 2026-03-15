namespace SeaWars.PlayerDataService.Data.Entities;

public sealed class PlayerState
{
    public Guid UserId { get; set; }

    public int Version { get; set; }

    // Stored as jsonb in Postgres; string to avoid schema-locking the payload.
    public string State { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

