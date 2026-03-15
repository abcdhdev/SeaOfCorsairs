using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeaWars.PlayerDataService.Data;

// Used by `dotnet ef` tooling without booting the whole app.
public sealed class PlayerDbContextFactory : IDesignTimeDbContextFactory<PlayerDbContext>
{
    public PlayerDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var conn = config.GetConnectionString("Postgres")
                   ?? "Host=localhost;Port=5432;Database=playerdb;Username=seawars;Password=seawars";

        var options = new DbContextOptionsBuilder<PlayerDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new PlayerDbContext(options);
    }
}

