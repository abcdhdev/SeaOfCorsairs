namespace SeaWars.AuthService.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds);

public sealed record MeResponse(Guid UserId, string Email, string? DisplayName);

public sealed record ErrorResponse(string Code, string Message);

