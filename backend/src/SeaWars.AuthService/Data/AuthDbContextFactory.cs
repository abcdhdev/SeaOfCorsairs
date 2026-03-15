using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeaWars.AuthService.Data;

// Used by `dotnet ef` tooling without booting the whole app.
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var conn = config.GetConnectionString("Postgres")
                   ?? "Host=localhost;Port=5432;Database=authdb;Username=seawars;Password=seawars";

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AuthDbContext(options);
    }
}

