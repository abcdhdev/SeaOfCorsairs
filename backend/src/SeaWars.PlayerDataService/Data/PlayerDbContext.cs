using Microsoft.EntityFrameworkCore;
using SeaWars.PlayerDataService.Data.Entities;

namespace SeaWars.PlayerDataService.Data;

public sealed class PlayerDbContext : DbContext
{
    public PlayerDbContext(DbContextOptions<PlayerDbContext> options) : base(options)
    {
    }

    public DbSet<PlayerState> PlayerStates => Set<PlayerState>();

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
    }
}

