using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public sealed class BackendPlayerDataClient
{
    private const string DefaultOwnedShipId = MarketShipCatalogRuntime.DefaultShipId;
    private readonly string _playerDataBaseUrl;

    public BackendPlayerDataClient(string playerDataBaseUrl)
    {
        _playerDataBaseUrl = NormalizeBaseUrl(playerDataBaseUrl);
        if (string.IsNullOrWhiteSpace(_playerDataBaseUrl))
        {
            throw new ArgumentException("Player Data base URL is required.", nameof(playerDataBaseUrl));
        }
    }

    public async Task<string> GetPlayerMeRawAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me";
        using var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ReadResponseTextOrThrow(request, url);
    }

    public async Task<PlayerStateResponse> GetPlayerStateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me";
        using var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseJsonOrThrow<PlayerStateResponse>(request, url);
    }

    public async Task<PlayerWalletResponse> GetWalletAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me/wallet";
        try
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

            await request.SendWebRequestAsync(cancellationToken);
            return ParseOrThrow<PlayerWalletResponse>(request, url);
        }
        catch (BackendApiException ex) when (ex.StatusCode == 404)
        {
            // Older player-data-service builds expose only the generic player-state endpoint.
            var playerState = await GetPlayerStateAsync(accessToken, cancellationToken);
            return ParseWalletFromPlayerStateResponse(playerState);
        }
    }

    public async Task<PlayerStateResponse> UpdatePlayerStateAsync(
        string accessToken,
        JObject state,
        int? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me/state";
        var body = new JObject
        {
            ["state"] = state ?? new JObject(),
        };

        if (expectedVersion.HasValue)
        {
            body["expectedVersion"] = expectedVersion.Value;
        }

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
        var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseJsonOrThrow<PlayerStateResponse>(request, url);
    }

    public async Task<PlayerStateResponse> UpdatePlayerStatusAsync(
        string accessToken,
        string selectedShipId,
        PlayerActionItemType activeActionItems,
        CancellationToken cancellationToken = default)
    {
        var currentState = await GetPlayerStateAsync(accessToken, cancellationToken);
        var state = currentState.state ?? new JObject();
        var ownedShipIds = EnsureDefaultShipIds(ReadStringArray(state, "ownedShips"));

        state["selectedShipId"] = ResolveSelectedShipId(selectedShipId, ownedShipIds);
        state["activeActionItems"] = NormalizeActionItemMask(activeActionItems);

        return await UpdatePlayerStateAsync(accessToken, state, currentState.version, cancellationToken);
    }

    public async Task<PlayerWalletResponse> UpdateWalletAsync(string accessToken, int gold, int diamond, int experience, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me/wallet";
        var bodyJson = expectedVersion.HasValue
            ? $"{{\"gold\":{Math.Max(0, gold)},\"diamond\":{Math.Max(0, diamond)},\"experience\":{Math.Max(0, experience)},\"expectedVersion\":{expectedVersion.Value}}}"
            : $"{{\"gold\":{Math.Max(0, gold)},\"diamond\":{Math.Max(0, diamond)},\"experience\":{Math.Max(0, experience)}}}";

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
        var bytes = Encoding.UTF8.GetBytes(bodyJson);
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        try
        {
            await request.SendWebRequestAsync(cancellationToken);
            return ParseOrThrow<PlayerWalletResponse>(request, url);
        }
        catch (BackendApiException ex) when (ex.StatusCode == 404)
        {
            return await UpdateWalletViaPlayerStateAsync(accessToken, gold, diamond, experience, expectedVersion, cancellationToken);
        }
    }

    public async Task<PlayerMarketStateResponse> GetMarketStateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var playerState = await GetPlayerStateAsync(accessToken, cancellationToken);
        return ParseMarketStateFromPlayerStateResponse(playerState);
    }

    public async Task<CannonPurchaseResponse> PurchaseCannonAsync(
        string accessToken,
        string cannonId,
        int gold,
        int diamond,
        int? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me/cannons/purchase";
        var body = new JObject
        {
            ["cannonId"] = (cannonId ?? string.Empty).Trim(),
            ["gold"] = Math.Max(0, gold),
            ["diamond"] = Math.Max(0, diamond),
        };

        if (expectedVersion.HasValue)
        {
            body["expectedVersion"] = expectedVersion.Value;
        }

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<CannonPurchaseResponse>(request, url);
    }

    public async Task<ShipPurchaseResponse> PurchaseShipAsync(
        string accessToken,
        string shipId,
        int gold,
        int diamond,
        int? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/player/me/ships/purchase";
        var body = new JObject
        {
            ["shipId"] = (shipId ?? string.Empty).Trim(),
            ["gold"] = Math.Max(0, gold),
            ["diamond"] = Math.Max(0, diamond),
        };

        if (expectedVersion.HasValue)
        {
            body["expectedVersion"] = expectedVersion.Value;
        }

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<ShipPurchaseResponse>(request, url);
    }

    public async Task<GuildListResponse> GetGuildsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/guilds";
        using var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseJsonOrThrow<GuildListResponse>(request, url);
    }

    public async Task<GuildSummaryResponse> CreateGuildAsync(
        string accessToken,
        string name,
        string tag,
        string description,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/guilds";
        var body = new JObject
        {
            ["name"] = (name ?? string.Empty).Trim(),
            ["tag"] = string.IsNullOrWhiteSpace(tag) ? string.Empty : tag.Trim(),
            ["description"] = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim(),
        };

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseJsonOrThrow<GuildSummaryResponse>(request, url);
    }

    private static string NormalizeBaseUrl(string url)
    {
        url ??= string.Empty;
        url = url.Trim();
        while (url.EndsWith("/", StringComparison.Ordinal))
        {
            url = url.Substring(0, url.Length - 1);
        }

        return url;
    }

    private static T ParseOrThrow<T>(UnityWebRequest request, string url)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var statusCode = request.responseCode;
        var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (statusCode < 200 || statusCode >= 300)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (typeof(T) == typeof(object))
        {
            return default;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw BackendApiException.FromResponse(url, statusCode, "Empty response", text);
        }

        try
        {
            return JsonUtility.FromJson<T>(text);
        }
        catch (Exception ex)
        {
            throw BackendApiException.FromResponse(url, statusCode, $"Failed to parse JSON: {ex.Message}", text);
        }
    }

    private static T ParseJsonOrThrow<T>(UnityWebRequest request, string url)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var statusCode = request.responseCode;
        var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (statusCode < 200 || statusCode >= 300)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (typeof(T) == typeof(object))
        {
            return default;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw BackendApiException.FromResponse(url, statusCode, "Empty response", text);
        }

        try
        {
            T parsed = JsonConvert.DeserializeObject<T>(text);
            if (parsed == null)
            {
                throw new InvalidOperationException("Response payload was null.");
            }

            return parsed;
        }
        catch (Exception ex)
        {
            throw BackendApiException.FromResponse(url, statusCode, $"Failed to parse JSON: {ex.Message}", text);
        }
    }

    private static string ReadResponseTextOrThrow(UnityWebRequest request, string url)
    {
        ParseOrThrow<object>(request, url);
        return request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
    }

    private async Task<PlayerWalletResponse> UpdateWalletViaPlayerStateAsync(string accessToken, int gold, int diamond, int experience, int? expectedVersion, CancellationToken cancellationToken)
    {
        var currentState = await GetPlayerStateAsync(accessToken, cancellationToken);

        var state = currentState.state ?? new JObject();
        state["gold"] = Math.Max(0, gold);
        state["diamond"] = Math.Max(0, diamond);
        state["experience"] = Math.Max(0, experience);

        var updatedState = await UpdatePlayerStateAsync(accessToken, state, expectedVersion ?? currentState.version, cancellationToken);
        return ParseWalletFromPlayerStateResponse(updatedState);
    }

    private static PlayerWalletResponse ParseWalletFromPlayerStateResponse(PlayerStateResponse playerState)
    {
        var state = playerState?.state ?? new JObject();
        var gold = ReadNonNegativeInt(state, "gold");
        var diamond = ReadNonNegativeInt(state, "diamond");
        var experience = ReadNonNegativeInt(state, "experience");

        return new PlayerWalletResponse
        {
            gold = gold,
            diamond = diamond,
            experience = experience,
            version = playerState?.version ?? 0,
            updatedAt = playerState?.updatedAt ?? string.Empty,
        };
    }

    private static PlayerMarketStateResponse ParseMarketStateFromPlayerStateResponse(PlayerStateResponse playerState)
    {
        var state = playerState?.state ?? new JObject();
        var gold = ReadNonNegativeInt(state, "gold");
        var diamond = ReadNonNegativeInt(state, "diamond");
        var experience = ReadNonNegativeInt(state, "experience");
        var ownedShipIds = EnsureDefaultShipIds(ReadStringArray(state, "ownedShips"));

        return new PlayerMarketStateResponse
        {
            gold = gold,
            diamond = diamond,
            experience = experience,
            ownedCannonIds = ReadStringArray(state, "ownedCannons"),
            ownedShipIds = ownedShipIds,
            selectedShipId = ResolveSelectedShipId(ReadStringValue(state, "selectedShipId"), ownedShipIds),
            activeActionItems = ReadActiveActionItemsMask(state),
            version = playerState?.version ?? 0,
            updatedAt = playerState?.updatedAt ?? string.Empty,
        };
    }

    private static int ReadNonNegativeInt(JObject state, string propertyName)
    {
        if (state == null || !state.TryGetValue(propertyName, out var token))
        {
            return 0;
        }

        return ReadNonNegativeIntValue(token);
    }

    private static int ReadNonNegativeIntValue(JToken token)
    {
        if (token == null)
        {
            return 0;
        }

        switch (token.Type)
        {
            case JTokenType.Integer:
            {
                var longValue = token.Value<long>();
                if (longValue <= 0)
                {
                    return 0;
                }

                return longValue >= int.MaxValue ? int.MaxValue : (int)longValue;
            }
            case JTokenType.Float:
            {
                var doubleValue = token.Value<double>();
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue) || doubleValue <= 0d)
                {
                    return 0;
                }

                return doubleValue >= int.MaxValue ? int.MaxValue : (int)Math.Floor(doubleValue);
            }
            case JTokenType.String:
            {
                var stringValue = token.Value<string>();
                if (int.TryParse(stringValue, out var parsed))
                {
                    return Math.Max(0, parsed);
                }

                return 0;
            }
            default:
                return 0;
        }
    }

    private static string[] ReadStringArray(JObject state, string propertyName)
    {
        if (state == null || !state.TryGetValue(propertyName, out var token) || token is not JArray array || array.Count == 0)
        {
            return Array.Empty<string>();
        }

        var values = new System.Collections.Generic.List<string>(array.Count);
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < array.Count; index++)
        {
            var rawValue = array[index]?.Value<string>();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            var normalizedValue = rawValue.Trim();
            if (!seen.Add(normalizedValue))
            {
                continue;
            }

            values.Add(normalizedValue);
        }

        return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
    }

    private static string ReadStringValue(JObject state, string propertyName)
    {
        if (state == null || !state.TryGetValue(propertyName, out var token) || token == null)
        {
            return string.Empty;
        }

        if (token.Type == JTokenType.String)
        {
            return token.Value<string>()?.Trim() ?? string.Empty;
        }

        return token.ToString().Trim();
    }

    private static string ResolveSelectedShipId(string selectedShipId, IReadOnlyList<string> ownedShipIds)
    {
        var normalizedSelectedShipId = MarketShipCatalogRuntime.NormalizeShipId(selectedShipId);
        if (ownedShipIds != null &&
            !string.IsNullOrWhiteSpace(normalizedSelectedShipId) &&
            ContainsOwnedShipId(ownedShipIds, normalizedSelectedShipId) &&
            MarketShipCatalogRuntime.TryGetShip(normalizedSelectedShipId, out _))
        {
            return normalizedSelectedShipId;
        }

        if (ownedShipIds != null && ownedShipIds.Count > 0)
        {
            return MarketShipCatalogRuntime.NormalizeShipId(ownedShipIds[0]);
        }

        return DefaultOwnedShipId;
    }

    private static bool ContainsOwnedShipId(IReadOnlyList<string> ownedShipIds, string shipId)
    {
        if (ownedShipIds == null)
        {
            return false;
        }

        var normalizedShipId = MarketShipCatalogRuntime.NormalizeShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId))
        {
            return false;
        }

        for (var index = 0; index < ownedShipIds.Count; index++)
        {
            if (string.Equals(ownedShipIds[index], normalizedShipId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadActiveActionItemsMask(JObject state)
    {
        if (state == null || !state.TryGetValue("activeActionItems", out var token))
        {
            return 0;
        }

        const int allowedMask = (int)(PlayerActionItemType.BlackGunpowder | PlayerActionItemType.AgwesArmorPlating);
        return ReadNonNegativeIntValue(token) & allowedMask;
    }

    private static int NormalizeActionItemMask(PlayerActionItemType activeActionItems)
    {
        const int allowedMask = (int)(PlayerActionItemType.BlackGunpowder | PlayerActionItemType.AgwesArmorPlating);
        return ((int)activeActionItems) & allowedMask;
    }

    private static string[] EnsureDefaultShipIds(string[] ownedShipIds)
    {
        if (ownedShipIds == null || ownedShipIds.Length == 0)
        {
            return new[] { DefaultOwnedShipId };
        }

        var normalizedDefaultId = MarketShipCatalogRuntime.NormalizeShipId(DefaultOwnedShipId);
        for (var index = 0; index < ownedShipIds.Length; index++)
        {
            if (string.Equals(
                    MarketShipCatalogRuntime.NormalizeShipId(ownedShipIds[index]),
                    normalizedDefaultId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ownedShipIds;
            }
        }

        var values = new string[ownedShipIds.Length + 1];
        values[0] = DefaultOwnedShipId;
        Array.Copy(ownedShipIds, 0, values, 1, ownedShipIds.Length);
        return values;
    }

}

[Serializable]
public sealed class PlayerWalletResponse
{
    public int gold;
    public int diamond;
    public int experience;
    public int version;
    public string updatedAt;
}

[Serializable]
public sealed class PlayerMarketStateResponse
{
    public int gold;
    public int diamond;
    public int experience;
    public string[] ownedCannonIds;
    public string[] ownedShipIds;
    public string selectedShipId;
    public int activeActionItems;
    public int version;
    public string updatedAt;
}

[Serializable]
public sealed class PlayerStateResponse
{
    public int version;
    public JObject state = new JObject();
    public string updatedAt;
}

[Serializable]
public sealed class CannonPurchaseResponse
{
    public string cannonId;
    public string[] ownedCannonIds;
    public int gold;
    public int diamond;
    public int version;
    public string updatedAt;
}

[Serializable]
public sealed class ShipPurchaseResponse
{
    public string shipId;
    public string[] ownedShipIds;
    public int gold;
    public int diamond;
    public int version;
    public string updatedAt;
}

[Serializable]
public sealed class GuildListResponse
{
    public string currentGuildId;
    public GuildSummaryResponse[] guilds = Array.Empty<GuildSummaryResponse>();
}

[Serializable]
public sealed class GuildSummaryResponse
{
    public string id;
    public string name;
    public string tag;
    public string description;
    public string leaderUserId;
    public string leaderDisplayName;
    public int memberCount;
    public bool isCurrentPlayerMember;
    public string createdAt;
    public string updatedAt;
}

public sealed class BackendWorldObjectClient
{
    private readonly string _playerDataBaseUrl;
    private readonly string _serverApiKey;

    public BackendWorldObjectClient(string playerDataBaseUrl, string serverApiKey)
    {
        _playerDataBaseUrl = NormalizeBaseUrl(playerDataBaseUrl);
        _serverApiKey = (serverApiKey ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(_playerDataBaseUrl))
        {
            throw new ArgumentException("Player Data base URL is required.", nameof(playerDataBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(_serverApiKey))
        {
            throw new ArgumentException("Server API key is required.", nameof(serverApiKey));
        }
    }

    public async Task<BackendWorldObjectResponse[]> GetWorldObjectsAsync(string objectType = null, CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/world-objects";
        if (!string.IsNullOrWhiteSpace(objectType))
        {
            url = $"{url}?objectType={UnityWebRequest.EscapeURL(objectType.Trim())}";
        }

        using var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        ApplyServerHeaders(request);

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<BackendWorldObjectResponse[]>(request, url) ?? Array.Empty<BackendWorldObjectResponse>();
    }

    public async Task<BackendWorldObjectResponse> CreateWorldObjectAsync(
        string objectType,
        string ownerEntityId,
        JObject state,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_playerDataBaseUrl}/v1/world-objects";
        var body = new JObject
        {
            ["objectType"] = (objectType ?? string.Empty).Trim(),
            ["ownerEntityId"] = (ownerEntityId ?? string.Empty).Trim(),
            ["state"] = state ?? new JObject(),
        };

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        ApplyServerHeaders(request);

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<BackendWorldObjectResponse>(request, url);
    }

    public async Task<BackendWorldObjectResponse> UpdateWorldObjectAsync(
        string worldObjectId,
        JObject state,
        CancellationToken cancellationToken = default)
    {
        var trimmedId = (worldObjectId ?? string.Empty).Trim();
        var url = $"{_playerDataBaseUrl}/v1/world-objects/{trimmedId}";
        var body = new JObject
        {
            ["state"] = state ?? new JObject(),
        };

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        ApplyServerHeaders(request);

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<BackendWorldObjectResponse>(request, url);
    }

    public async Task DeleteWorldObjectAsync(string worldObjectId, CancellationToken cancellationToken = default)
    {
        var trimmedId = (worldObjectId ?? string.Empty).Trim();
        var url = $"{_playerDataBaseUrl}/v1/world-objects/{trimmedId}";

        using var request = UnityWebRequest.Delete(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        ApplyServerHeaders(request);

        await request.SendWebRequestAsync(cancellationToken);
        ParseOrThrow<object>(request, url);
    }

    private void ApplyServerHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("X-Server-Api-Key", _serverApiKey);
    }

    private static string NormalizeBaseUrl(string url)
    {
        url ??= string.Empty;
        url = url.Trim();
        while (url.EndsWith("/", StringComparison.Ordinal))
        {
            url = url.Substring(0, url.Length - 1);
        }

        return url;
    }

    private static T ParseOrThrow<T>(UnityWebRequest request, string url)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var statusCode = request.responseCode;
        var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (statusCode < 200 || statusCode >= 300)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (typeof(T) == typeof(object))
        {
            return default;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw BackendApiException.FromResponse(url, statusCode, "Empty response", text);
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(text);
        }
        catch (Exception ex)
        {
            throw BackendApiException.FromResponse(url, statusCode, $"Failed to parse JSON: {ex.Message}", text);
        }
    }
}

public sealed class BackendWorldObjectResponse
{
    [JsonProperty("id")]
    public string Id = string.Empty;

    [JsonProperty("objectType")]
    public string ObjectType = string.Empty;

    [JsonProperty("ownerEntityId")]
    public string OwnerEntityId = string.Empty;

    [JsonProperty("state")]
    public JObject State = new();

    [JsonProperty("createdAt")]
    public string CreatedAt = string.Empty;

    [JsonProperty("updatedAt")]
    public string UpdatedAt = string.Empty;
}
