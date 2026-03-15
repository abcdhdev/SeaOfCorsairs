using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using SeaWars.AuthService.Contracts;
using SeaWars.AuthService.Data;
using SeaWars.AuthService.Data.Entities;
using SeaWars.AuthService.Options;
using SeaWars.Backend.Common.Crypto;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SeaWars Auth Service", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var postgres = builder.Configuration.GetConnectionString("Postgres")
              ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres.");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(postgres));

var redis = builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis.");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
    throw new InvalidOperationException("Missing Jwt:SigningKey (set Jwt__SigningKey).");

var signingKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);
if (signingKeyBytes.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits) for HS256.");

var signingKey = new SymmetricSecurityKey(signingKeyBytes);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(10),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };
});
builder.Services.AddAuthorization();

builder.Services.AddSingleton<JwtSecurityTokenHandler>();
builder.Services.AddSingleton(new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrations");
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await ApplyMigrationsWithRetryAsync(db, logger, maxAttempts: 10, initialDelay: TimeSpan.FromSeconds(2));
}

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/auth/register", async (
        RegisterRequest request,
        AuthDbContext db,
        IConnectionMultiplexer redisMux,
        IOptions<AuthOptions> authOptionsAccessor,
        JwtSecurityTokenHandler jwtHandler,
        SigningCredentials signingCredentials) =>
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email))
            return Results.BadRequest(new ErrorResponse("invalid_email", "Email is invalid."));
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return Results.BadRequest(new ErrorResponse("invalid_password", "Password must be at least 8 characters."));

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (displayName is not null && displayName.Length > 64)
            return Results.BadRequest(new ErrorResponse("invalid_display_name", "Display name must be 64 characters or fewer."));

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index on email.
            return Results.Conflict(new ErrorResponse("email_taken", "Email is already registered."));
        }

        var authOptions = authOptionsAccessor.Value;
        var accessLifetime = TimeSpan.FromMinutes(Math.Max(1, authOptions.AccessTokenMinutes));
        var refreshLifetime = TimeSpan.FromDays(Math.Max(1, authOptions.RefreshTokenDays));

        var accessToken = CreateAccessToken(user, now, accessLifetime, jwtHandler, signingCredentials, jwtOptions);

        var refreshToken = TokenGenerator.CreateOpaqueToken();
        var refreshHash = TokenGenerator.Sha256Base64Url(refreshToken);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now.Add(refreshLifetime),
        });
        await db.SaveChangesAsync();

        var redisDb = redisMux.GetDatabase();
        await redisDb.StringSetAsync($"auth:rt:{refreshHash}", user.Id.ToString(), refreshLifetime);

        return Results.Ok(new TokenResponse(accessToken, refreshToken, (int)accessLifetime.TotalSeconds));
    })
    .WithTags("Auth")
    .AllowAnonymous();

app.MapPost("/v1/auth/login", async (
        HttpContext httpContext,
        LoginRequest request,
        AuthDbContext db,
        IConnectionMultiplexer redisMux,
        IOptions<AuthOptions> authOptionsAccessor,
        JwtSecurityTokenHandler jwtHandler,
        SigningCredentials signingCredentials) =>
    {
        var authOptions = authOptionsAccessor.Value;
        var maxAttempts = Math.Max(1, authOptions.LoginRateLimit.MaxAttempts);
        var window = TimeSpan.FromSeconds(Math.Clamp(authOptions.LoginRateLimit.WindowSeconds, 1, 3600));

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var attemptsKey = $"auth:login:{ip}";

        var redisDb = redisMux.GetDatabase();
        var attempts = await redisDb.StringIncrementAsync(attemptsKey);
        if (attempts == 1)
            await redisDb.KeyExpireAsync(attemptsKey, window);

        if (attempts > maxAttempts)
            return Results.Json(new ErrorResponse("rate_limited", "Too many login attempts. Try again later."), statusCode: StatusCodes.Status429TooManyRequests);

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user is null || !PasswordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            return Results.Json(new ErrorResponse("invalid_credentials", "Invalid email or password."), statusCode: StatusCodes.Status401Unauthorized);

        await redisDb.KeyDeleteAsync(attemptsKey);

        var now = DateTimeOffset.UtcNow;
        var accessLifetime = TimeSpan.FromMinutes(Math.Max(1, authOptions.AccessTokenMinutes));
        var refreshLifetime = TimeSpan.FromDays(Math.Max(1, authOptions.RefreshTokenDays));

        var accessToken = CreateAccessToken(user, now, accessLifetime, jwtHandler, signingCredentials, jwtOptions);

        var refreshToken = TokenGenerator.CreateOpaqueToken();
        var refreshHash = TokenGenerator.Sha256Base64Url(refreshToken);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now.Add(refreshLifetime),
        });
        await db.SaveChangesAsync();

        await redisDb.StringSetAsync($"auth:rt:{refreshHash}", user.Id.ToString(), refreshLifetime);

        return Results.Ok(new TokenResponse(accessToken, refreshToken, (int)accessLifetime.TotalSeconds));
    })
    .WithTags("Auth")
    .AllowAnonymous();

app.MapPost("/v1/auth/refresh", async (
        RefreshRequest request,
        AuthDbContext db,
        IConnectionMultiplexer redisMux,
        IOptions<AuthOptions> authOptionsAccessor,
        JwtSecurityTokenHandler jwtHandler,
        SigningCredentials signingCredentials) =>
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Results.BadRequest(new ErrorResponse("invalid_refresh_token", "Refresh token is required."));

        var now = DateTimeOffset.UtcNow;
        var authOptions = authOptionsAccessor.Value;
        var accessLifetime = TimeSpan.FromMinutes(Math.Max(1, authOptions.AccessTokenMinutes));
        var refreshLifetime = TimeSpan.FromDays(Math.Max(1, authOptions.RefreshTokenDays));

        var oldHash = TokenGenerator.Sha256Base64Url(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == oldHash);
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now)
            return Results.Json(new ErrorResponse("invalid_refresh_token", "Refresh token is invalid or expired."), statusCode: StatusCodes.Status401Unauthorized);

        var user = await db.Users.FindAsync(token.UserId);
        if (user is null)
            return Results.Json(new ErrorResponse("invalid_refresh_token", "Refresh token is invalid."), statusCode: StatusCodes.Status401Unauthorized);

        // Rotate refresh token.
        token.RevokedAt = now;
        token.RevocationReason = "rotated";

        var newRefreshToken = TokenGenerator.CreateOpaqueToken();
        var newHash = TokenGenerator.Sha256Base64Url(newRefreshToken);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = newHash,
            CreatedAt = now,
            ExpiresAt = now.Add(refreshLifetime),
        });
        await db.SaveChangesAsync();

        var redisDb = redisMux.GetDatabase();
        await redisDb.KeyDeleteAsync($"auth:rt:{oldHash}");
        await redisDb.StringSetAsync($"auth:rt:{newHash}", user.Id.ToString(), refreshLifetime);

        var accessToken = CreateAccessToken(user, now, accessLifetime, jwtHandler, signingCredentials, jwtOptions);
        return Results.Ok(new TokenResponse(accessToken, newRefreshToken, (int)accessLifetime.TotalSeconds));
    })
    .WithTags("Auth")
    .AllowAnonymous();

app.MapPost("/v1/auth/logout", async (
        LogoutRequest request,
        AuthDbContext db,
        IConnectionMultiplexer redisMux) =>
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Results.BadRequest(new ErrorResponse("invalid_refresh_token", "Refresh token is required."));

        var now = DateTimeOffset.UtcNow;
        var hash = TokenGenerator.Sha256Base64Url(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null)
            return Results.Ok(new { status = "ok" }); // idempotent

        if (token.RevokedAt is null)
        {
            token.RevokedAt = now;
            token.RevocationReason = "logout";
            await db.SaveChangesAsync();
        }

        await redisMux.GetDatabase().KeyDeleteAsync($"auth:rt:{hash}");
        return Results.Ok(new { status = "ok" });
    })
    .WithTags("Auth")
    .AllowAnonymous();

app.MapGet("/v1/auth/me", async (ClaimsPrincipal principal, AuthDbContext db) =>
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
            return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return Results.NotFound(new ErrorResponse("not_found", "User not found."));

        return Results.Ok(new MeResponse(user.Id, user.Email, user.DisplayName));
    })
    .WithTags("Auth")
    .RequireAuthorization();

app.Run();

static string CreateAccessToken(
    UserAccount user,
    DateTimeOffset now,
    TimeSpan lifetime,
    JwtSecurityTokenHandler handler,
    SigningCredentials credentials,
    JwtOptions jwtOptions)
{
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
    };

    if (!string.IsNullOrWhiteSpace(user.DisplayName))
        claims.Add(new("displayName", user.DisplayName));

    var token = new JwtSecurityToken(
        issuer: jwtOptions.Issuer,
        audience: jwtOptions.Audience,
        claims: claims,
        notBefore: now.UtcDateTime,
        expires: now.Add(lifetime).UtcDateTime,
        signingCredentials: credentials);

    return handler.WriteToken(token);
}

static async Task ApplyMigrationsWithRetryAsync(DbContext db, ILogger logger, int maxAttempts, TimeSpan initialDelay)
{
    var delay = initialDelay;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(ex, "Database migration attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s", attempt, maxAttempts, delay.TotalSeconds);
            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 15));
        }
    }
}
