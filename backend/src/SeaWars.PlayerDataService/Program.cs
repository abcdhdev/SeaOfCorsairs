using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SeaWars.PlayerDataService.Contracts;
using SeaWars.PlayerDataService.Data;
using SeaWars.PlayerDataService.Data.Entities;
using SeaWars.PlayerDataService.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<S3Options>(builder.Configuration.GetSection("S3"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SeaWars Player Data Service", Version = "v1" });

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
builder.Services.AddDbContext<PlayerDbContext>(options => options.UseNpgsql(postgres));

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

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var s3 = sp.GetRequiredService<IOptions<S3Options>>().Value;
    if (string.IsNullOrWhiteSpace(s3.ServiceUrl))
        throw new InvalidOperationException("Missing S3:ServiceUrl (set S3__ServiceUrl).");

    var cfg = new AmazonS3Config
    {
        ServiceURL = s3.ServiceUrl,
        ForcePathStyle = s3.ForcePathStyle,
        AuthenticationRegion = s3.Region,
    };

    return new AmazonS3Client(s3.AccessKey, s3.SecretKey, cfg);
});

var app = builder.Build();
const string GuildWorldObjectType = "guild";

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrations");
    var db = scope.ServiceProvider.GetRequiredService<PlayerDbContext>();
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

var worldObjectsServerApiKey = builder.Configuration["WorldObjects:ServerApiKey"];
if (string.IsNullOrWhiteSpace(worldObjectsServerApiKey))
    throw new InvalidOperationException("Missing WorldObjects:ServerApiKey (set WorldObjects__ServerApiKey).");

app.MapGet("/v1/player/me", async (
    ClaimsPrincipal principal,
    PlayerDbContext db,
    IConnectionMultiplexer redisMux,
    IOptions<JsonOptions> jsonOptionsAccessor) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var cacheKey = $"player:state:v2:{userId.Value}";
    var redisDb = redisMux.GetDatabase();
    var cached = await redisDb.StringGetAsync(cacheKey);
    if (cached.HasValue)
        return Results.Content(cached!, "application/json");

    var entity = await db.PlayerStates.FindAsync(userId.Value);
    if (entity is null)
    {
        var now = DateTimeOffset.UtcNow;
        entity = new PlayerState
        {
            UserId = userId.Value,
            Version = 0,
            State = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlayerStates.Add(entity);
        await db.SaveChangesAsync();
    }

    using var doc = JsonDocument.Parse(entity.State);
    var response = new PlayerStateResponse(entity.Version, doc.RootElement.Clone(), entity.UpdatedAt);
    var json = System.Text.Json.JsonSerializer.Serialize(response, jsonOptionsAccessor.Value.SerializerOptions);
    await redisDb.StringSetAsync(cacheKey, json, expiry: TimeSpan.FromMinutes(10));
    return Results.Content(json, "application/json");
})
    .WithTags("Player")
    .RequireAuthorization();

app.MapPut("/v1/player/me/state", async (
    ClaimsPrincipal principal,
    UpdatePlayerStateRequest request,
    PlayerDbContext db,
    IConnectionMultiplexer redisMux,
    IOptions<JsonOptions> jsonOptionsAccessor) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    if (request.State.ValueKind == System.Text.Json.JsonValueKind.Undefined || request.State.ValueKind == System.Text.Json.JsonValueKind.Null)
        return Results.BadRequest(new ErrorResponse("invalid_state", "State is required."));

    var now = DateTimeOffset.UtcNow;
    var entity = await db.PlayerStates.FindAsync(userId.Value);
    if (entity is null)
    {
        entity = new PlayerState
        {
            UserId = userId.Value,
            Version = 0,
            State = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlayerStates.Add(entity);
        await db.SaveChangesAsync();
    }

    if (request.ExpectedVersion is not null)
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.ExpectedVersion.Value;

    entity.State = request.State.GetRawText();
    entity.Version += 1;
    entity.UpdatedAt = now;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        var current = await db.PlayerStates.AsNoTracking().SingleAsync(x => x.UserId == userId.Value);
        return Results.Conflict(new ConflictResponse(current.Version, current.UpdatedAt));
    }

    var cacheKey = $"player:state:v2:{userId.Value}";
    using var doc = JsonDocument.Parse(entity.State);
    var response = new PlayerStateResponse(entity.Version, doc.RootElement.Clone(), entity.UpdatedAt);
    var json = System.Text.Json.JsonSerializer.Serialize(response, jsonOptionsAccessor.Value.SerializerOptions);
    await redisMux.GetDatabase().StringSetAsync(cacheKey, json, expiry: TimeSpan.FromMinutes(10));

    return Results.Ok(response);
})
    .WithTags("Player")
    .RequireAuthorization();

app.MapGet("/v1/player/me/wallet", async (
    ClaimsPrincipal principal,
    PlayerDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var entity = await db.PlayerStates.FindAsync(userId.Value);
    if (entity is null)
    {
        var now = DateTimeOffset.UtcNow;
        entity = new PlayerState
        {
            UserId = userId.Value,
            Version = 0,
            State = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlayerStates.Add(entity);
        await db.SaveChangesAsync();
    }

    var wallet = ExtractWallet(entity.State);
    return Results.Ok(new PlayerWalletResponse(wallet.Gold, wallet.Diamond, wallet.Experience, entity.Version, entity.UpdatedAt));
})
    .WithTags("Player")
    .RequireAuthorization();

app.MapPut("/v1/player/me/wallet", async (
    ClaimsPrincipal principal,
    UpdatePlayerWalletRequest request,
    PlayerDbContext db,
    IConnectionMultiplexer redisMux,
    IOptions<JsonOptions> jsonOptionsAccessor) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    if (request.Gold < 0)
        return Results.BadRequest(new ErrorResponse("invalid_gold", "Gold must be zero or greater."));

    if (request.Diamond < 0)
        return Results.BadRequest(new ErrorResponse("invalid_diamond", "Diamond must be zero or greater."));

    if (request.Experience < 0)
        return Results.BadRequest(new ErrorResponse("invalid_experience", "Experience must be zero or greater."));

    var now = DateTimeOffset.UtcNow;
    var entity = await db.PlayerStates.FindAsync(userId.Value);
    if (entity is null)
    {
        entity = new PlayerState
        {
            UserId = userId.Value,
            Version = 0,
            State = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlayerStates.Add(entity);
        await db.SaveChangesAsync();
    }

    if (request.ExpectedVersion is not null)
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.ExpectedVersion.Value;

    entity.State = UpsertWalletStateJson(entity.State, request.Gold, request.Diamond, request.Experience);
    entity.Version += 1;
    entity.UpdatedAt = now;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        var current = await db.PlayerStates.AsNoTracking().SingleAsync(x => x.UserId == userId.Value);
        return Results.Conflict(new ConflictResponse(current.Version, current.UpdatedAt));
    }

    var cacheKey = $"player:state:v2:{userId.Value}";
    using var doc = JsonDocument.Parse(entity.State);
    var stateResponse = new PlayerStateResponse(entity.Version, doc.RootElement.Clone(), entity.UpdatedAt);
    var stateJson = System.Text.Json.JsonSerializer.Serialize(stateResponse, jsonOptionsAccessor.Value.SerializerOptions);
    await redisMux.GetDatabase().StringSetAsync(cacheKey, stateJson, expiry: TimeSpan.FromMinutes(10));

    var wallet = ExtractWallet(entity.State);
    return Results.Ok(new PlayerWalletResponse(wallet.Gold, wallet.Diamond, wallet.Experience, entity.Version, entity.UpdatedAt));
})
    .WithTags("Player")
    .RequireAuthorization();

app.MapPost("/v1/player/me/cannons/purchase", async (
    ClaimsPrincipal principal,
    PurchaseCannonRequest request,
    PlayerDbContext db,
    IConnectionMultiplexer redisMux,
    IOptions<JsonOptions> jsonOptionsAccessor) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var cannonId = NormalizeCannonId(request.CannonId);
    if (!TryResolveCannonCatalogEntry(cannonId, out var cannonCatalogEntry))
        return Results.BadRequest(new ErrorResponse("invalid_cannon", $"Unknown cannon id '{cannonId}'."));

    if (request.Gold < 0)
        return Results.BadRequest(new ErrorResponse("invalid_gold", "Gold must be zero or greater."));

    if (request.Diamond < 0)
        return Results.BadRequest(new ErrorResponse("invalid_diamond", "Diamond must be zero or greater."));

    var now = DateTimeOffset.UtcNow;
    var entity = await db.PlayerStates.FindAsync(userId.Value);
    if (entity is null)
    {
        entity = new PlayerState
        {
            UserId = userId.Value,
            Version = 0,
            State = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlayerStates.Add(entity);
        await db.SaveChangesAsync();
    }

    if (request.ExpectedVersion is not null)
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.ExpectedVersion.Value;

    var ownedCannonIds = ExtractOwnedCannonIds(entity.State);
    if (ContainsOwnedCannonId(ownedCannonIds, cannonId))
        return Results.BadRequest(new ErrorResponse("already_owned", $"{cannonCatalogEntry.DisplayName} is already owned."));

    if (request.Gold < cannonCatalogEntry.GoldCost && request.Diamond < cannonCatalogEntry.DiamondCost)
        return Results.BadRequest(new ErrorResponse("insufficient_funds", $"Not enough gold and diamonds to buy {cannonCatalogEntry.DisplayName}."));

    if (request.Gold < cannonCatalogEntry.GoldCost)
        return Results.BadRequest(new ErrorResponse("insufficient_gold", $"Not enough gold to buy {cannonCatalogEntry.DisplayName}."));

    if (request.Diamond < cannonCatalogEntry.DiamondCost)
        return Results.BadRequest(new ErrorResponse("insufficient_diamond", $"Not enough diamonds to buy {cannonCatalogEntry.DisplayName}."));

    ownedCannonIds.Add(cannonId);
    entity.State = UpsertCannonPurchaseStateJson(
        entity.State,
        request.Gold - cannonCatalogEntry.GoldCost,
        request.Diamond - cannonCatalogEntry.DiamondCost,
        ownedCannonIds);
    entity.Version += 1;
    entity.UpdatedAt = now;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        var current = await db.PlayerStates.AsNoTracking().SingleAsync(x => x.UserId == userId.Value);
        return Results.Conflict(new ConflictResponse(current.Version, current.UpdatedAt));
    }

    var cacheKey = $"player:state:v2:{userId.Value}";
    using var doc = JsonDocument.Parse(entity.State);
    var stateResponse = new PlayerStateResponse(entity.Version, doc.RootElement.Clone(), entity.UpdatedAt);
    var stateJson = System.Text.Json.JsonSerializer.Serialize(stateResponse, jsonOptionsAccessor.Value.SerializerOptions);
    await redisMux.GetDatabase().StringSetAsync(cacheKey, stateJson, expiry: TimeSpan.FromMinutes(10));

    var wallet = ExtractWallet(entity.State);
    var response = new CannonPurchaseResponse(
        cannonId,
        ExtractOwnedCannonIds(entity.State).ToArray(),
        wallet.Gold,
        wallet.Diamond,
        entity.Version,
        entity.UpdatedAt);

    return Results.Ok(response);
})
    .WithTags("Player")
    .RequireAuthorization();

app.MapPost("/v1/player/me/ships/purchase", async (
    ClaimsPrincipal principal,
    PurchaseShipRequest request,
    PlayerDbContext db,
    IConnectionMultiplexer redisMux,
    IOptions<JsonOptions> jsonOptionsAccessor) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var shipId = NormalizeShipId(request.ShipId);
    if (!TryResolveShipCatalogEntry(shipId, out var shipCatalogEntry))
        return Results.BadRequest(new ErrorResponse("invalid_ship", $"Unknown ship id '{shipId}'."));

    if (request.Gold < 0)
        return Results.BadRequest(new ErrorResponse("invalid_gold", "Gold must be zero or greater."));

    if (request.Diamond < 0)
        return Results.BadRequest(new ErrorResponse("invalid_diamond", "Diamond must be zero or greater."));

    var now = DateTimeOffset.UtcNow;
    var entity = await db.PlayerStates.FindAsync(userId.Value);
    if (entity is null)
    {
        entity = new PlayerState
        {
            UserId = userId.Value,
            Version = 0,
            State = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlayerStates.Add(entity);
        await db.SaveChangesAsync();
    }

    if (request.ExpectedVersion is not null)
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.ExpectedVersion.Value;

    var ownedShipIds = ExtractOwnedShipIds(entity.State);
    if (ContainsOwnedShipId(ownedShipIds, shipId))
        return Results.BadRequest(new ErrorResponse("already_owned", $"{shipCatalogEntry.DisplayName} is already owned."));

    if (request.Gold < shipCatalogEntry.GoldCost && request.Diamond < shipCatalogEntry.DiamondCost)
        return Results.BadRequest(new ErrorResponse("insufficient_funds", $"Not enough gold and diamonds to buy {shipCatalogEntry.DisplayName}."));

    if (request.Gold < shipCatalogEntry.GoldCost)
        return Results.BadRequest(new ErrorResponse("insufficient_gold", $"Not enough gold to buy {shipCatalogEntry.DisplayName}."));

    if (request.Diamond < shipCatalogEntry.DiamondCost)
        return Results.BadRequest(new ErrorResponse("insufficient_diamond", $"Not enough diamonds to buy {shipCatalogEntry.DisplayName}."));

    ownedShipIds.Add(shipId);
    entity.State = UpsertShipPurchaseStateJson(
        entity.State,
        request.Gold - shipCatalogEntry.GoldCost,
        request.Diamond - shipCatalogEntry.DiamondCost,
        ownedShipIds);
    entity.Version += 1;
    entity.UpdatedAt = now;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        var current = await db.PlayerStates.AsNoTracking().SingleAsync(x => x.UserId == userId.Value);
        return Results.Conflict(new ConflictResponse(current.Version, current.UpdatedAt));
    }

    var cacheKey = $"player:state:v2:{userId.Value}";
    using var doc = JsonDocument.Parse(entity.State);
    var stateResponse = new PlayerStateResponse(entity.Version, doc.RootElement.Clone(), entity.UpdatedAt);
    var stateJson = System.Text.Json.JsonSerializer.Serialize(stateResponse, jsonOptionsAccessor.Value.SerializerOptions);
    await redisMux.GetDatabase().StringSetAsync(cacheKey, stateJson, expiry: TimeSpan.FromMinutes(10));

    var wallet = ExtractWallet(entity.State);
    var response = new ShipPurchaseResponse(
        shipId,
        ExtractOwnedShipIds(entity.State).ToArray(),
        wallet.Gold,
        wallet.Diamond,
        entity.Version,
        entity.UpdatedAt);

    return Results.Ok(response);
})
    .WithTags("Player")
    .RequireAuthorization();

app.MapGet("/v1/guilds", async (
    ClaimsPrincipal principal,
    PlayerDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var guildEntities = await db.WorldObjects
        .AsNoTracking()
        .Where(x => x.ObjectType == GuildWorldObjectType)
        .OrderBy(x => x.CreatedAt)
        .ToListAsync();

    var guilds = new List<GuildSummaryResponse>(guildEntities.Count);
    var currentGuildId = string.Empty;
    for (var index = 0; index < guildEntities.Count; index++)
    {
        if (!TryCreateGuildSummary(guildEntities[index], userId.Value, out var guildSummary))
            continue;

        guilds.Add(guildSummary);
        if (guildSummary.IsCurrentPlayerMember && string.IsNullOrWhiteSpace(currentGuildId))
            currentGuildId = guildSummary.Id;
    }

    guilds.Sort(static (left, right) =>
    {
        var memberComparison = right.IsCurrentPlayerMember.CompareTo(left.IsCurrentPlayerMember);
        if (memberComparison != 0)
            return memberComparison;

        var countComparison = right.MemberCount.CompareTo(left.MemberCount);
        if (countComparison != 0)
            return countComparison;

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    });

    return Results.Ok(new GuildListResponse(currentGuildId, guilds.ToArray()));
})
    .WithTags("Guilds")
    .RequireAuthorization();

app.MapPost("/v1/guilds", async (
    ClaimsPrincipal principal,
    CreateGuildRequest request,
    PlayerDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var normalizedName = CollapseWhitespace(request.Name);
    if (normalizedName.Length < 3 || normalizedName.Length > 32)
        return Results.BadRequest(new ErrorResponse("invalid_guild_name", "Guild name must be between 3 and 32 characters."));

    var normalizedTag = NormalizeGuildTag(request.Tag);
    if (string.IsNullOrWhiteSpace(normalizedTag))
        return Results.BadRequest(new ErrorResponse("invalid_guild_tag", "Guild abbreviation must be exactly 3 letters."));

    if (normalizedTag.Length != 3)
        return Results.BadRequest(new ErrorResponse("invalid_guild_tag", "Guild abbreviation must be exactly 3 letters."));

    var normalizedDescription = CollapseWhitespace(request.Description);
    if (normalizedDescription.Length > 180)
        return Results.BadRequest(new ErrorResponse("invalid_guild_description", "Guild description must be 180 characters or fewer."));

    var requestedNameKey = NormalizeGuildLookupKey(normalizedName);
    var requestedTagKey = NormalizeGuildLookupKey(normalizedTag);
    var creatorUserId = userId.Value.ToString("D");

    var existingGuildEntities = await db.WorldObjects
        .AsNoTracking()
        .Where(x => x.ObjectType == GuildWorldObjectType)
        .OrderBy(x => x.CreatedAt)
        .ToListAsync();

    for (var index = 0; index < existingGuildEntities.Count; index++)
    {
        if (!TryReadGuildState(existingGuildEntities[index].State, out var existingGuildState))
            continue;

        if (existingGuildState.MemberUserIds.Contains(creatorUserId, StringComparer.OrdinalIgnoreCase))
        {
            return Results.Conflict(new ErrorResponse(
                "already_in_guild",
                $"You are already in the guild '{existingGuildState.Name}'. Leave it before creating a new one."));
        }

        if (string.Equals(NormalizeGuildLookupKey(existingGuildState.Name), requestedNameKey, StringComparison.Ordinal))
        {
            return Results.Conflict(new ErrorResponse(
                "guild_name_taken",
                $"A guild named '{normalizedName}' already exists."));
        }

        if (!string.IsNullOrWhiteSpace(requestedTagKey) &&
            !string.IsNullOrWhiteSpace(existingGuildState.Tag) &&
            string.Equals(NormalizeGuildLookupKey(existingGuildState.Tag), requestedTagKey, StringComparison.Ordinal))
        {
            return Results.Conflict(new ErrorResponse(
                "guild_tag_taken",
                $"The guild tag '{normalizedTag}' is already in use."));
        }
    }

    var creatorDisplayName = ResolveGuildDisplayName(principal);
    var now = DateTimeOffset.UtcNow;
    var guildState = new JsonObject
    {
        ["name"] = normalizedName,
        ["tag"] = normalizedTag,
        ["description"] = normalizedDescription,
        ["leaderUserId"] = creatorUserId,
        ["leaderDisplayName"] = creatorDisplayName,
        ["memberUserIds"] = CreateStringJsonArray(new[] { creatorUserId }),
    };

    var entity = new WorldObject
    {
        Id = Guid.NewGuid(),
        ObjectType = GuildWorldObjectType,
        OwnerEntityId = creatorUserId,
        State = guildState.ToJsonString(),
        CreatedAt = now,
        UpdatedAt = now,
    };

    db.WorldObjects.Add(entity);
    await db.SaveChangesAsync();

    if (!TryCreateGuildSummary(entity, userId.Value, out var guildSummary))
        return Results.BadRequest(new ErrorResponse("guild_create_failed", "Guild was created, but its data could not be loaded."));

    return Results.Ok(guildSummary);
})
    .WithTags("Guilds")
    .RequireAuthorization();

app.MapGet("/v1/world-objects", async (
    HttpRequest request,
    string? objectType,
    PlayerDbContext db) =>
{
    if (!IsAuthorizedServerRequest(request, worldObjectsServerApiKey))
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var query = db.WorldObjects.AsNoTracking().AsQueryable();
    var normalizedObjectType = NormalizeWorldObjectType(objectType);
    if (!string.IsNullOrWhiteSpace(normalizedObjectType))
        query = query.Where(x => x.ObjectType == normalizedObjectType);

    var entities = await query
        .OrderBy(x => x.CreatedAt)
        .ToListAsync();

    var response = new WorldObjectResponse[entities.Count];
    for (var index = 0; index < entities.Count; index++)
        response[index] = CreateWorldObjectResponse(entities[index]);

    return Results.Ok(response);
})
    .WithTags("WorldObjects");

app.MapPost("/v1/world-objects", async (
    HttpRequest request,
    CreateWorldObjectRequest createRequest,
    PlayerDbContext db) =>
{
    if (!IsAuthorizedServerRequest(request, worldObjectsServerApiKey))
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var normalizedObjectType = NormalizeWorldObjectType(createRequest.ObjectType);
    if (string.IsNullOrWhiteSpace(normalizedObjectType))
        return Results.BadRequest(new ErrorResponse("invalid_object_type", "ObjectType is required."));

    var normalizedOwnerEntityId = NormalizeOwnerEntityId(createRequest.OwnerEntityId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerEntityId))
        return Results.BadRequest(new ErrorResponse("invalid_owner_entity_id", "OwnerEntityId is required."));

    if (createRequest.State.ValueKind == System.Text.Json.JsonValueKind.Undefined ||
        createRequest.State.ValueKind == System.Text.Json.JsonValueKind.Null)
    {
        return Results.BadRequest(new ErrorResponse("invalid_state", "State is required."));
    }

    var now = DateTimeOffset.UtcNow;
    var entity = new WorldObject
    {
        Id = Guid.NewGuid(),
        ObjectType = normalizedObjectType,
        OwnerEntityId = normalizedOwnerEntityId,
        State = createRequest.State.GetRawText(),
        CreatedAt = now,
        UpdatedAt = now,
    };

    db.WorldObjects.Add(entity);
    await db.SaveChangesAsync();
    return Results.Ok(CreateWorldObjectResponse(entity));
})
    .WithTags("WorldObjects");

app.MapPut("/v1/world-objects/{id:guid}", async (
    HttpRequest request,
    Guid id,
    UpdateWorldObjectRequest updateRequest,
    PlayerDbContext db) =>
{
    if (!IsAuthorizedServerRequest(request, worldObjectsServerApiKey))
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    if (updateRequest.State.ValueKind == System.Text.Json.JsonValueKind.Undefined ||
        updateRequest.State.ValueKind == System.Text.Json.JsonValueKind.Null)
    {
        return Results.BadRequest(new ErrorResponse("invalid_state", "State is required."));
    }

    var entity = await db.WorldObjects.FindAsync(id);
    if (entity is null)
        return Results.NotFound(new ErrorResponse("not_found", "World object was not found."));

    entity.State = updateRequest.State.GetRawText();
    entity.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(CreateWorldObjectResponse(entity));
})
    .WithTags("WorldObjects");

app.MapDelete("/v1/world-objects/{id:guid}", async (
    HttpRequest request,
    Guid id,
    PlayerDbContext db) =>
{
    if (!IsAuthorizedServerRequest(request, worldObjectsServerApiKey))
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var entity = await db.WorldObjects.FindAsync(id);
    if (entity is null)
        return Results.NotFound(new ErrorResponse("not_found", "World object was not found."));

    db.WorldObjects.Remove(entity);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
    .WithTags("WorldObjects");

app.MapPost("/v1/logs/presign", (ClaimsPrincipal principal, PresignLogUploadRequest request, IAmazonS3 s3Client, IOptions<S3Options> s3OptionsAccessor) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
        return Results.Json(new ErrorResponse("unauthorized", "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized);

    var s3 = s3OptionsAccessor.Value;
    var bucket = s3.Buckets.Logs;
    var protocol = GetPresignProtocol(s3.ServiceUrl);

    var fileName = string.IsNullOrWhiteSpace(request.FileName) ? "log.txt" : request.FileName.Trim();
    fileName = Path.GetFileName(fileName); // avoid path traversal

    var key = $"users/{userId.Value:D}/{DateTime.UtcNow:yyyyMMdd}/{Guid.NewGuid():N}-{fileName}";
    var expiresAt = DateTime.UtcNow.AddMinutes(15);

    var presignRequest = new GetPreSignedUrlRequest
    {
        BucketName = bucket,
        Key = key,
        Verb = HttpVerb.PUT,
        Expires = expiresAt,
        ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType.Trim(),
        Protocol = protocol,
    };

    var url = s3Client.GetPreSignedURL(presignRequest);
    return Results.Ok(new PresignResponse(url, "PUT", presignRequest.ContentType, key, new DateTimeOffset(expiresAt)));
})
    .WithTags("Logs")
    .RequireAuthorization();

app.MapGet("/v1/assets/presign", (ClaimsPrincipal principal, string key, IAmazonS3 s3Client, IOptions<S3Options> s3OptionsAccessor) =>
{
    _ = principal; // Auth required; no per-user restriction implemented (bucket-level access assumed).

    if (string.IsNullOrWhiteSpace(key))
        return Results.BadRequest(new ErrorResponse("invalid_key", "key is required."));

    key = key.TrimStart('/');
    var s3 = s3OptionsAccessor.Value;
    var bucket = s3.Buckets.Assets;
    var protocol = GetPresignProtocol(s3.ServiceUrl);

    var expiresAt = DateTime.UtcNow.AddMinutes(15);
    var presignRequest = new GetPreSignedUrlRequest
    {
        BucketName = bucket,
        Key = key,
        Verb = HttpVerb.GET,
        Expires = expiresAt,
        Protocol = protocol,
    };

    var url = s3Client.GetPreSignedURL(presignRequest);
    return Results.Ok(new PresignResponse(url, "GET", null, key, new DateTimeOffset(expiresAt)));
})
    .WithTags("Assets")
    .RequireAuthorization();

app.Run();

const string DefaultOwnedShipId = "elite27";

static Guid? GetUserId(ClaimsPrincipal principal)
{
    var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(sub, out var id) ? id : null;
}

static bool IsAuthorizedServerRequest(HttpRequest request, string expectedApiKey)
{
    if (request.Headers.TryGetValue("X-Server-Api-Key", out var providedValue) &&
        string.Equals(providedValue.ToString(), expectedApiKey, StringComparison.Ordinal))
    {
        return true;
    }

    return false;
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

static Protocol GetPresignProtocol(string serviceUrl)
{
    return serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        ? Protocol.HTTP
        : Protocol.HTTPS;
}

static (int Gold, int Diamond, int Experience) ExtractWallet(string stateJson)
{
    var state = ParseStateObject(stateJson);
    var gold = ReadNonNegativeInt(state, "gold");
    var diamond = ReadNonNegativeInt(state, "diamond");
    if (diamond == 0)
        diamond = ReadNonNegativeInt(state, "pearls");
    var experience = ReadNonNegativeInt(state, "experience");

    return (gold, diamond, experience);
}

static string UpsertWalletStateJson(string stateJson, int gold, int diamond, int experience)
{
    var state = ParseStateObject(stateJson);
    state["gold"] = Math.Max(0, gold);
    state["diamond"] = Math.Max(0, diamond);
    state["experience"] = Math.Max(0, experience);
    state.Remove("pearls");
    return state.ToJsonString();
}

static string UpsertCannonPurchaseStateJson(string stateJson, int gold, int diamond, IReadOnlyCollection<string> ownedCannonIds)
{
    var state = ParseStateObject(stateJson);
    state["gold"] = Math.Max(0, gold);
    state["diamond"] = Math.Max(0, diamond);
    state.Remove("pearls");
    state["ownedCannons"] = CreateOwnedCannonsJsonArray(ownedCannonIds);
    return state.ToJsonString();
}

static string UpsertShipPurchaseStateJson(string stateJson, int gold, int diamond, IReadOnlyCollection<string> ownedShipIds)
{
    var state = ParseStateObject(stateJson);
    state["gold"] = Math.Max(0, gold);
    state["diamond"] = Math.Max(0, diamond);
    state.Remove("pearls");
    state["ownedShips"] = CreateOwnedShipsJsonArray(ownedShipIds);
    return state.ToJsonString();
}

static List<string> ExtractOwnedCannonIds(string stateJson)
{
    var state = ParseStateObject(stateJson);
    if (!state.TryGetPropertyValue("ownedCannons", out var ownedNode) || ownedNode is not JsonArray ownedArray)
        return new List<string>();

    var orderedIds = new List<string>();
    var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < ownedArray.Count; index++)
    {
        var rawId = ownedArray[index] is JsonValue ownedValue && ownedValue.TryGetValue<string>(out var parsedId)
            ? parsedId
            : string.Empty;
        var normalizedId = NormalizeCannonId(rawId);
        if (string.IsNullOrWhiteSpace(normalizedId) || !TryResolveCannonCatalogEntry(normalizedId, out _) || !seenIds.Add(normalizedId))
            continue;

        orderedIds.Add(normalizedId);
    }

    orderedIds.Sort(static (left, right) => CompareCannonCatalogOrder(left, right));
    return orderedIds;
}

static JsonArray CreateOwnedCannonsJsonArray(IEnumerable<string> ownedCannonIds)
{
    var orderedIds = new List<string>();
    var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rawId in ownedCannonIds)
    {
        var normalizedId = NormalizeCannonId(rawId);
        if (string.IsNullOrWhiteSpace(normalizedId) || !TryResolveCannonCatalogEntry(normalizedId, out _) || !seenIds.Add(normalizedId))
            continue;

        orderedIds.Add(normalizedId);
    }

    orderedIds.Sort(static (left, right) => CompareCannonCatalogOrder(left, right));

    var array = new JsonArray();
    for (var index = 0; index < orderedIds.Count; index++)
        array.Add(orderedIds[index]);

    return array;
}

static List<string> ExtractOwnedShipIds(string stateJson)
{
    var state = ParseStateObject(stateJson);
    if (!state.TryGetPropertyValue("ownedShips", out var ownedNode) || ownedNode is not JsonArray ownedArray)
        return new List<string> { DefaultOwnedShipId };

    var orderedIds = new List<string>();
    var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < ownedArray.Count; index++)
    {
        var rawId = ownedArray[index] is JsonValue ownedValue && ownedValue.TryGetValue<string>(out var parsedId)
            ? parsedId
            : string.Empty;
        var normalizedId = NormalizeShipId(rawId);
        if (string.IsNullOrWhiteSpace(normalizedId) || !TryResolveShipCatalogEntry(normalizedId, out _) || !seenIds.Add(normalizedId))
            continue;

        orderedIds.Add(normalizedId);
    }

    if (!seenIds.Contains(DefaultOwnedShipId))
        orderedIds.Add(DefaultOwnedShipId);

    orderedIds.Sort(static (left, right) => CompareShipCatalogOrder(left, right));
    return orderedIds;
}

static JsonArray CreateOwnedShipsJsonArray(IEnumerable<string> ownedShipIds)
{
    var orderedIds = new List<string>();
    var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rawId in ownedShipIds)
    {
        var normalizedId = NormalizeShipId(rawId);
        if (string.IsNullOrWhiteSpace(normalizedId) || !TryResolveShipCatalogEntry(normalizedId, out _) || !seenIds.Add(normalizedId))
            continue;

        orderedIds.Add(normalizedId);
    }

    if (seenIds.Add(DefaultOwnedShipId))
        orderedIds.Add(DefaultOwnedShipId);

    orderedIds.Sort(static (left, right) => CompareShipCatalogOrder(left, right));

    var array = new JsonArray();
    for (var index = 0; index < orderedIds.Count; index++)
        array.Add(orderedIds[index]);

    return array;
}

static bool ContainsOwnedCannonId(IReadOnlyList<string> ownedCannonIds, string cannonId)
{
    var normalizedId = NormalizeCannonId(cannonId);
    if (string.IsNullOrWhiteSpace(normalizedId))
        return false;

    for (var index = 0; index < ownedCannonIds.Count; index++)
    {
        if (string.Equals(ownedCannonIds[index], normalizedId, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

static string ResolveGuildDisplayName(ClaimsPrincipal principal)
{
    var displayName = CollapseWhitespace(principal.FindFirstValue("displayName"));
    return string.IsNullOrWhiteSpace(displayName) ? "Unnamed Captain" : displayName;
}

static bool ContainsOwnedShipId(IReadOnlyList<string> ownedShipIds, string shipId)
{
    var normalizedId = NormalizeShipId(shipId);
    if (string.IsNullOrWhiteSpace(normalizedId))
        return false;

    for (var index = 0; index < ownedShipIds.Count; index++)
    {
        if (string.Equals(ownedShipIds[index], normalizedId, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

static string NormalizeCannonId(string? cannonId)
{
    return string.IsNullOrWhiteSpace(cannonId)
        ? string.Empty
        : cannonId.Trim().ToLowerInvariant();
}

static string NormalizeShipId(string? shipId)
{
    return string.IsNullOrWhiteSpace(shipId)
        ? string.Empty
        : shipId.Trim().ToLowerInvariant();
}

static bool TryResolveCannonCatalogEntry(string cannonId, out CannonCatalogEntry entry)
{
    switch (NormalizeCannonId(cannonId))
    {
        case "gold_8_pounder":
            entry = new CannonCatalogEntry("gold_8_pounder", "8-Pounder", 750, 0, 0);
            return true;
        case "gold_12_pounder":
            entry = new CannonCatalogEntry("gold_12_pounder", "12-Pounder", 2000, 0, 1);
            return true;
        case "gold_16_pounder":
            entry = new CannonCatalogEntry("gold_16_pounder", "16-Pounder", 3500, 0, 2);
            return true;
        case "gold_20_pounder":
            entry = new CannonCatalogEntry("gold_20_pounder", "20-Pounder", 6000, 0, 3);
            return true;
        case "gold_24_pounder":
            entry = new CannonCatalogEntry("gold_24_pounder", "24-Pounder", 8500, 0, 4);
            return true;
        case "gold_30_pounder":
            entry = new CannonCatalogEntry("gold_30_pounder", "30-Pounder", 10000, 0, 5);
            return true;
        case "gold_50_pounder":
            entry = new CannonCatalogEntry("gold_50_pounder", "50-Pounder", 100000, 0, 6);
            return true;
        case "gold_repair_cannon_level_1":
            entry = new CannonCatalogEntry("gold_repair_cannon_level_1", "Repair Cannon Level 1", 10000, 0, 7);
            return true;
        case "elite_55_pounder":
            entry = new CannonCatalogEntry("elite_55_pounder", "55-Pounder", 15000, 150, 8);
            return true;
        case "elite_60_pounder":
            entry = new CannonCatalogEntry("elite_60_pounder", "60-Pounder", 30000, 350, 9);
            return true;
        case "elite_repair_cannon_level_2":
            entry = new CannonCatalogEntry("elite_repair_cannon_level_2", "Repair Cannon Level 2", 0, 7500, 10);
            return true;
        case "admiral_cannon":
            entry = new CannonCatalogEntry("admiral_cannon", "Admiral Cannon", 60000, 800, 11);
            return true;
        case "voodoo_cannon":
            entry = new CannonCatalogEntry("voodoo_cannon", "Voodoo Cannon", 70000, 1200, 12);
            return true;
        case "firestorm_cannon":
            entry = new CannonCatalogEntry("firestorm_cannon", "Firestorm Cannon", 80000, 1600, 13);
            return true;
        case "doomhammer_cannon":
            entry = new CannonCatalogEntry("doomhammer_cannon", "Doomhammer Cannon", 90000, 2000, 14);
            return true;
        case "devastator_cannon":
            entry = new CannonCatalogEntry("devastator_cannon", "Devastator Cannon", 100000, 2400, 15);
            return true;
        case "painbringer_cannon":
            entry = new CannonCatalogEntry("painbringer_cannon", "Painbringer Cannon", 110000, 2800, 16);
            return true;
        case "rift_cannon":
            entry = new CannonCatalogEntry("rift_cannon", "Rift Cannon", 120000, 3200, 17);
            return true;
        case "worldbreaker_cannon":
            entry = new CannonCatalogEntry("worldbreaker_cannon", "Worldbreaker Cannon", 130000, 3600, 18);
            return true;
        case "bastion_cannon":
            entry = new CannonCatalogEntry("bastion_cannon", "Bastion Cannon", 145000, 4200, 19);
            return true;
        case "overlord_cannon":
            entry = new CannonCatalogEntry("overlord_cannon", "Overlord Cannon", 160000, 5000, 20);
            return true;
        case "thunderbolt_cannon":
            entry = new CannonCatalogEntry("thunderbolt_cannon", "Thunderbolt Cannon", 180000, 6000, 21);
            return true;
        case "repair_cannon_level_3":
            entry = new CannonCatalogEntry("repair_cannon_level_3", "Repair Cannon Level 3", 0, 12000, 22);
            return true;
        case "repair_cannon_level_4":
            entry = new CannonCatalogEntry("repair_cannon_level_4", "Repair Cannon Level 4", 0, 18000, 23);
            return true;
        case "repair_cannon_level_5":
            entry = new CannonCatalogEntry("repair_cannon_level_5", "Repair Cannon Level 5", 0, 24000, 24);
            return true;
        case "lava_cannon":
            entry = new CannonCatalogEntry("lava_cannon", "Lava Cannon", 200000, 7000, 25);
            return true;
        default:
            entry = default;
            return false;
    }
}

static bool TryResolveShipCatalogEntry(string shipId, out ShipCatalogEntry entry)
{
    switch (NormalizeShipId(shipId))
    {
        case "elite27":
            entry = new ShipCatalogEntry("elite27", "Elite 27", 0, 0, 0);
            return true;
        case "elite1":
            entry = new ShipCatalogEntry("elite1", "Elite 1", 18000, 0, 1);
            return true;
        default:
            entry = default;
            return false;
    }
}

static int CompareCannonCatalogOrder(string? leftId, string? rightId)
{
    var hasLeft = TryResolveCannonCatalogEntry(leftId ?? string.Empty, out var leftEntry);
    var hasRight = TryResolveCannonCatalogEntry(rightId ?? string.Empty, out var rightEntry);

    if (hasLeft && hasRight)
        return leftEntry.SortOrder.CompareTo(rightEntry.SortOrder);

    return string.Compare(leftId, rightId, StringComparison.OrdinalIgnoreCase);
}

static int CompareShipCatalogOrder(string? leftId, string? rightId)
{
    var hasLeft = TryResolveShipCatalogEntry(leftId ?? string.Empty, out var leftEntry);
    var hasRight = TryResolveShipCatalogEntry(rightId ?? string.Empty, out var rightEntry);

    if (hasLeft && hasRight)
        return leftEntry.SortOrder.CompareTo(rightEntry.SortOrder);

    return string.Compare(leftId, rightId, StringComparison.OrdinalIgnoreCase);
}

static JsonObject ParseStateObject(string stateJson)
{
    if (string.IsNullOrWhiteSpace(stateJson))
        return new JsonObject();

    try
    {
        return JsonNode.Parse(stateJson) as JsonObject ?? new JsonObject();
    }
    catch (JsonException)
    {
        return new JsonObject();
    }
}

static bool TryReadGuildState(string stateJson, out GuildStateSnapshot guildState)
{
    var state = ParseStateObject(stateJson);
    var name = CollapseWhitespace(ReadStringValue(state, "name"));
    var tag = NormalizeGuildTag(ReadStringValue(state, "tag"));
    var description = CollapseWhitespace(ReadStringValue(state, "description"));
    var leaderUserId = ReadStringValue(state, "leaderUserId");
    var leaderDisplayName = CollapseWhitespace(ReadStringValue(state, "leaderDisplayName"));
    var memberUserIds = ReadNormalizedStringArray(state, "memberUserIds");

    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(leaderUserId))
    {
        guildState = default;
        return false;
    }

    if (memberUserIds.Length == 0)
        memberUserIds = new[] { leaderUserId };

    guildState = new GuildStateSnapshot(
        name,
        tag,
        description,
        leaderUserId,
        string.IsNullOrWhiteSpace(leaderDisplayName) ? "Unnamed Captain" : leaderDisplayName,
        memberUserIds);
    return true;
}

static bool TryCreateGuildSummary(WorldObject entity, Guid currentUserId, out GuildSummaryResponse guildSummary)
{
    if (!TryReadGuildState(entity.State, out var guildState))
    {
        guildSummary = default!;
        return false;
    }

    var currentUserIdValue = currentUserId.ToString("D");
    var isCurrentPlayerMember = guildState.MemberUserIds.Contains(currentUserIdValue, StringComparer.OrdinalIgnoreCase);

    guildSummary = new GuildSummaryResponse(
        entity.Id.ToString("D"),
        guildState.Name,
        guildState.Tag,
        guildState.Description,
        guildState.LeaderUserId,
        guildState.LeaderDisplayName,
        guildState.MemberUserIds.Length,
        isCurrentPlayerMember,
        entity.CreatedAt,
        entity.UpdatedAt);
    return true;
}

static string CollapseWhitespace(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

static string NormalizeGuildLookupKey(string? value)
{
    return CollapseWhitespace(value).ToUpperInvariant();
}

static string NormalizeGuildTag(string? value)
{
    var collapsed = CollapseWhitespace(value).Replace(" ", string.Empty, StringComparison.Ordinal);
    if (string.IsNullOrWhiteSpace(collapsed))
        return string.Empty;

    for (var index = 0; index < collapsed.Length; index++)
    {
        if (!char.IsLetter(collapsed[index]))
            return string.Empty;
    }

    return collapsed.ToUpperInvariant();
}

static string ReadStringValue(JsonObject state, string propertyName)
{
    if (!state.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
        return string.Empty;

    if (value.TryGetValue<string>(out var stringValue))
        return stringValue?.Trim() ?? string.Empty;

    return node.ToJsonString().Trim('"').Trim();
}

static string[] ReadNormalizedStringArray(JsonObject state, string propertyName)
{
    if (!state.TryGetPropertyValue(propertyName, out var node) || node is not JsonArray array)
        return Array.Empty<string>();

    var values = new List<string>(array.Count);
    var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < array.Count; index++)
    {
        if (array[index] is not JsonValue value || !value.TryGetValue<string>(out var stringValue))
            continue;

        var normalizedValue = CollapseWhitespace(stringValue);
        if (string.IsNullOrWhiteSpace(normalizedValue) || !seenValues.Add(normalizedValue))
            continue;

        values.Add(normalizedValue);
    }

    return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
}

static JsonArray CreateStringJsonArray(IEnumerable<string> values)
{
    var array = new JsonArray();
    foreach (var value in values)
    {
        var normalizedValue = CollapseWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
            continue;

        array.Add(normalizedValue);
    }

    return array;
}

static int ReadNonNegativeInt(JsonObject state, string propertyName)
{
    if (!state.TryGetPropertyValue(propertyName, out var node) || node is null)
        return 0;

    return ReadNonNegativeIntValue(node);
}

static int ReadNonNegativeIntValue(JsonNode node)
{
    if (node is not JsonValue value)
        return 0;

    if (value.TryGetValue<int>(out var intValue))
        return Math.Max(0, intValue);

    if (value.TryGetValue<long>(out var longValue))
        return longValue <= 0 ? 0 : (int)Math.Min(int.MaxValue, longValue);

    if (value.TryGetValue<double>(out var doubleValue))
    {
        if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue) || doubleValue <= 0d)
            return 0;

        return doubleValue >= int.MaxValue ? int.MaxValue : (int)Math.Floor(doubleValue);
    }

    if (value.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, out var parsed))
        return Math.Max(0, parsed);

    return 0;
}

static string NormalizeWorldObjectType(string? objectType)
{
    return string.IsNullOrWhiteSpace(objectType)
        ? string.Empty
        : objectType.Trim().ToLowerInvariant();
}

static string NormalizeOwnerEntityId(string? ownerEntityId)
{
    return string.IsNullOrWhiteSpace(ownerEntityId)
        ? string.Empty
        : ownerEntityId.Trim();
}

static WorldObjectResponse CreateWorldObjectResponse(WorldObject entity)
{
    using var doc = JsonDocument.Parse(entity.State);
    return new WorldObjectResponse(
        entity.Id,
        entity.ObjectType,
        entity.OwnerEntityId,
        doc.RootElement.Clone(),
        entity.CreatedAt,
        entity.UpdatedAt);
}

readonly record struct CannonCatalogEntry(string Id, string DisplayName, int GoldCost, int DiamondCost, int SortOrder);
readonly record struct ShipCatalogEntry(string Id, string DisplayName, int GoldCost, int DiamondCost, int SortOrder);
readonly record struct GuildStateSnapshot(
    string Name,
    string Tag,
    string Description,
    string LeaderUserId,
    string LeaderDisplayName,
    string[] MemberUserIds);
