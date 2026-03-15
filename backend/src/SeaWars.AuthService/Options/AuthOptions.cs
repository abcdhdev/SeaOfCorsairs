namespace SeaWars.AuthService.Options;

public sealed class AuthOptions
{
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    public LoginRateLimitOptions LoginRateLimit { get; set; } = new();
}

public sealed class LoginRateLimitOptions
{
    public int MaxAttempts { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

