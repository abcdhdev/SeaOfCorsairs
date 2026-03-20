using Microsoft.EntityFrameworkCore;
using SeaWars.PlayerDataService.Data.Entities;

namespace SeaWars.PlayerDataService.Data;

public sealed class PlayerDbContext : DbContext
{
    public PlayerDbContext(DbContextOptions<PlayerDbContext> options) : base(options)
    {
    }

    public DbSet<PlayerState> PlayerStates => Set<PlayerState>();

    public DbSet<WorldObject> WorldObjects => Set<WorldObject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerState>(entity =>
        {
            entity.ToTable("player_states");
            entity.HasKey(x => x.UserId);

            entity.Property(x => x.State).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<WorldObject>(entity =>
        {
            entity.ToTable("world_objects");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ObjectType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.OwnerEntityId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.State).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();

            entity.HasIndex(x => x.ObjectType);
            entity.HasIndex(x => x.OwnerEntityId);
        });
    }
}

