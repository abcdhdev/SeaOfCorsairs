namespace SeaWars.PlayerDataService.Options;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "seawars";
    public string Audience { get; set; } = "seawars";
    public string SigningKey { get; set; } = string.Empty;
}

