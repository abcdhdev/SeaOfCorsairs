using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_SERVER || UNITY_EDITOR
using System.IO;
using System.Security.Cryptography;
#endif
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class MultiplayerController : MonoBehaviour
{
    private const float DefaultWaterSurfaceY = 12.5f;

    public static MultiplayerController Instance { get; private set; }

#if UNITY_SERVER || UNITY_EDITOR
    // JWT config (must match backend appsettings.json).
    private const string ExpectedIssuer = "seawars";
    private const string ExpectedAudience = "seawars";
    private const string JwtSigningKeyEnvVar = "JWT_SIGNING_KEY";
    private const string RequiredClientVersionEnvVar = "SEAWARS_REQUIRED_CLIENT_VERSION";
    private const string RequiredProtocolVersionEnvVar = "SEAWARS_REQUIRED_PROTOCOL_VERSION";
    private const string AuthBaseUrlEnvVar = "SEAWARS_AUTH_BASE_URL";
    private const string PlayerDataBaseUrlEnvVar = "SEAWARS_PLAYERDATA_BASE_URL";
    private const string ServerApiKeyEnvVar = "SEAWARS_SERVER_API_KEY";
    private const string LocalDevFallbackJwtSigningKey = "dev-insecure-change-me-please-use-at-least-32-bytes";
    private const string LocalDevFallbackServerApiKey = "dev-world-objects-api-key-change-me";

    private sealed class AuthenticatedClientSession
    {
        public string UserId = string.Empty;
        public string DisplayName = string.Empty;
        public string AccessToken = string.Empty;
        public string RefreshToken = string.Empty;
        public Player Player;
        public Action<int, int, int> WalletChangedHandler;
        public Action<string> SelectedShipChangedHandler;
        public Action<PlayerActionItemType> ActiveActionItemsChangedHandler;
        public Action InventoryChangedHandler;
        public Action ShipCannonLoadoutsChangedHandler;
        public CancellationTokenSource LifetimeCts = new();
        public SemaphoreSlim PlayerStateSyncLock = new(1, 1);
        public int WalletVersion;
        public bool HasWalletVersion;
        public bool IsApplyingPlayerState;
        public int PendingGold;
        public int PendingDiamond;
        public int PendingExperience;
        public string PendingSelectedShipId = string.Empty;
        public PlayerActionItemType PendingActiveActionItems = PlayerActionItemType.None;
        public string PendingInventorySnapshot = string.Empty;
        public string PendingShipCannonLoadoutsSnapshot = string.Empty;
        public bool WalletDirty;
        public bool WalletSaveLoopRunning;
        public bool PlayerStatusDirty;
        public bool PlayerStatusSaveLoopRunning;
    }

    // Single-session tracking (server-only).
    // Maps authenticated userId (JWT "sub") to Netcode clientId.
    private readonly Dictionary<string, ulong> _userIdToClientId = new();
    // Reverse map so we can clean up on disconnect.
    private readonly Dictionary<ulong, string> _clientIdToUserId = new();
    private readonly Dictionary<ulong, string> _clientIdToDisplayName = new();
    private readonly Dictionary<ulong, AuthenticatedClientSession> _clientSessions = new();
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("MultiplayerController: Multiple instances detected. Replacing the previous instance reference.");
        }

        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
#if UNITY_SERVER || UNITY_EDITOR
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
#endif
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-mode" && i + 1 < args.Length && args[i + 1] == "server")
            {
                StartHeadlessServer();
                return;
            }

            if (args[i] == "-mode" && i + 1 < args.Length && args[i + 1] == "client")
            {
                StartClientFromCli();
                return;
            }

            if (args[i] == "-mode" && i + 1 < args.Length && args[i + 1] == "host")
            {
                StartHostFromCli();
                return;
            }
        }
    }

#if UNITY_SERVER || UNITY_EDITOR
    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                               NetworkManager.ConnectionApprovalResponse response)
    {
        var clientId = request.ClientNetworkId;

        // The host/server (clientId 0) is always approved - it doesn't send ConnectionData.
        if (clientId == NetworkManager.ServerClientId)
        {
            if (TryResolveHostSession(out string hostUserId, out string hostDisplayName, out string hostAccessToken, out string hostRefreshToken))
            {
                RegisterApprovedSession(clientId, hostUserId, hostDisplayName, hostAccessToken, hostRefreshToken);
                Debug.Log($"[Auth] Host approved for user {hostUserId} (displayName: {hostDisplayName}).");
            }
            else
            {
                _clientIdToDisplayName[clientId] = "Host";
                Debug.Log("[Auth] Host/Server auto-approved without backend session.");
            }

            ApproveAndSpawn(response);
            return;
        }

        // 1. Parse and validate the versioned handshake payload.
        if (!NetcodeConnectionPayload.TryParse(request.Payload, out var connectionPayload, out string payloadRejectReason))
        {
            Reject(response, payloadRejectReason);
            return;
        }

        if (!IsClientVersionCompatible(connectionPayload.ClientVersion, connectionPayload.ProtocolVersion, out string compatibilityRejectReason))
        {
            Reject(response, compatibilityRejectReason);
            return;
        }

        string jwt = connectionPayload.AccessToken;

        // 2. Structurally validate the JWT.
        if (!TryValidateJwt(jwt, out string userId, out string displayName, out string rejectReason))
        {
            Reject(response, rejectReason);
            return;
        }

        // 3. Single-session enforcement.
        if (_userIdToClientId.TryGetValue(userId, out ulong existingClientId))
        {
            Debug.Log($"[Auth] User {userId} already connected as client {existingClientId}. Kicking old session.");
            if (_clientSessions.TryGetValue(existingClientId, out var existingSession))
            {
                CleanupSession(existingClientId, existingSession);
                _clientSessions.Remove(existingClientId);
            }

            _clientIdToUserId.Remove(existingClientId);
            _clientIdToDisplayName.Remove(existingClientId);
            _userIdToClientId.Remove(userId);
            NetworkManager.Singleton.DisconnectClient(existingClientId);
        }

        // 4. Register the new session.
        RegisterApprovedSession(clientId, userId, displayName, connectionPayload.AccessToken, connectionPayload.RefreshToken);

        ApproveAndSpawn(response);
        Debug.Log($"[Auth] Client {clientId} approved for user {userId} (displayName: {displayName}).");
    }

    /// <summary>
    /// Validates a JWT by verifying HS256 signature and checking sub, exp, iss, and aud.
    /// </summary>
    private static bool TryValidateJwt(string jwt, out string userId, out string displayName, out string rejectReason)
    {
        userId = null;
        displayName = null;
        rejectReason = null;

        // JWT format: header.payload.signature
        string[] parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            rejectReason = "Malformed JWT (expected 3 parts).";
            return false;
        }

        // Decode and validate header first so we can enforce HS256 before signature check.
        JwtHeader header;
        try
        {
            string headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            header = JsonUtility.FromJson<JwtHeader>(headerJson);
        }
        catch (Exception e)
        {
            rejectReason = $"Failed to parse JWT header: {e.Message}";
            return false;
        }

        if (!string.Equals(header.alg, "HS256", StringComparison.Ordinal))
        {
            rejectReason = $"Unsupported JWT alg '{header.alg}'. Expected HS256.";
            return false;
        }

        if (!TryResolveJwtSigningKey(out string signingKey, out string signingKeyError))
        {
            rejectReason = signingKeyError;
            return false;
        }

        if (!VerifyHs256Signature(parts[0], parts[1], parts[2], signingKey))
        {
            rejectReason = "Invalid JWT signature.";
            return false;
        }

        // Decode the payload (middle part).
        string payloadJson;
        try
        {
            payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        }
        catch (Exception e)
        {
            rejectReason = $"Failed to decode JWT payload: {e.Message}";
            return false;
        }

        // Parse claims with Unity's built-in JSON utility.
        JwtPayload payload;
        try
        {
            payload = JsonUtility.FromJson<JwtPayload>(payloadJson);
        }
        catch (Exception e)
        {
            rejectReason = $"Failed to parse JWT payload JSON: {e.Message}";
            return false;
        }

        // Check required claims.
        if (string.IsNullOrWhiteSpace(payload.sub))
        {
            rejectReason = "JWT missing 'sub' claim.";
            return false;
        }

        // Validate issuer.
        if (!string.Equals(payload.iss, ExpectedIssuer, StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = $"JWT issuer mismatch: expected '{ExpectedIssuer}', got '{payload.iss}'.";
            return false;
        }

        // Validate audience.
        if (!string.Equals(payload.aud, ExpectedAudience, StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = $"JWT audience mismatch: expected '{ExpectedAudience}', got '{payload.aud}'.";
            return false;
        }

        // Validate expiry.
        if (payload.exp > 0)
        {
            var expTime = DateTimeOffset.FromUnixTimeSeconds(payload.exp);
            if (DateTimeOffset.UtcNow > expTime)
            {
                rejectReason = "JWT has expired.";
                return false;
            }
        }
        else
        {
            rejectReason = "JWT missing 'exp' claim.";
            return false;
        }

        userId = payload.sub;
        displayName = UiTextSanitizer.SanitizeForLabel(payload.displayName ?? string.Empty, collapseWhitespace: true);
        return true;
    }

    private static bool IsClientVersionCompatible(string clientVersion, int clientProtocolVersion, out string rejectReason)
    {
        rejectReason = null;

        int requiredProtocolVersion = ResolveRequiredProtocolVersion();
        if (clientProtocolVersion != requiredProtocolVersion)
        {
            rejectReason = $"Protocol version mismatch. Server requires {requiredProtocolVersion}, client sent {clientProtocolVersion}. Please update your game client.";
            return false;
        }

        string requiredClientVersion = ResolveRequiredClientVersion();
        if (!string.Equals(clientVersion, requiredClientVersion, StringComparison.Ordinal))
        {
            rejectReason = $"Client version mismatch. Server requires '{requiredClientVersion}', client is '{clientVersion}'. Please update your game client.";
            return false;
        }

        return true;
    }

    private static string ResolveRequiredClientVersion()
    {
        string configured = Environment.GetEnvironmentVariable(RequiredClientVersionEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Application.version))
        {
            return Application.version.Trim();
        }

        return "unknown";
    }

    private static int ResolveRequiredProtocolVersion()
    {
        string configured = Environment.GetEnvironmentVariable(RequiredProtocolVersionEnvVar);
        if (!string.IsNullOrWhiteSpace(configured) &&
            int.TryParse(configured.Trim(), out int parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return NetcodeConnectionPayload.CurrentProtocolVersion;
    }

    private static bool VerifyHs256Signature(string headerPart, string payloadPart, string signaturePart, string signingKey)
    {
        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(signaturePart);
        }
        catch
        {
            return false;
        }

        string signingInput = $"{headerPart}.{payloadPart}";
        byte[] signingInputBytes = Encoding.ASCII.GetBytes(signingInput);
        byte[] keyBytes = Encoding.UTF8.GetBytes(signingKey);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] expectedSignature = hmac.ComputeHash(signingInputBytes);

        return CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature);
    }

    /// <summary>
    /// Decodes a Base64Url-encoded string (no padding, - and _ instead of + and /).
    /// </summary>
    private static byte[] Base64UrlDecode(string input)
    {
        string s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private static bool TryResolveJwtSigningKey(out string signingKey, out string error)
    {
        signingKey = Environment.GetEnvironmentVariable(JwtSigningKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            signingKey = signingKey.Trim();
            error = null;
            return true;
        }

        if (TryReadDotEnvVariable(JwtSigningKeyEnvVar, out string fromDotEnv))
        {
            signingKey = fromDotEnv;
            error = null;
            return true;
        }

        // Editor-only convenience for local playmode testing.
#if UNITY_EDITOR
        signingKey = LocalDevFallbackJwtSigningKey;
        Debug.LogWarning($"[Auth] {JwtSigningKeyEnvVar} not found in process env or project .env. Falling back to local development default key.");
        error = null;
        return true;
#else
        error = $"Server missing {JwtSigningKeyEnvVar} environment variable.";
        return false;
#endif
    }

    private static bool TryReadDotEnvVariable(string key, out string value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        try
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string envPath = Path.Combine(projectRoot, ".env");
            if (!File.Exists(envPath))
            {
                return false;
            }

            foreach (string rawLine in File.ReadLines(envPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int splitIndex = line.IndexOf('=');
                if (splitIndex <= 0)
                {
                    continue;
                }

                string candidateKey = line.Substring(0, splitIndex).Trim();
                if (!string.Equals(candidateKey, key, StringComparison.Ordinal))
                {
                    continue;
                }

                string candidateValue = line.Substring(splitIndex + 1).Trim();
                if (candidateValue.Length >= 2 &&
                    ((candidateValue[0] == '"' && candidateValue[candidateValue.Length - 1] == '"') ||
                     (candidateValue[0] == '\'' && candidateValue[candidateValue.Length - 1] == '\'')))
                {
                    candidateValue = candidateValue.Substring(1, candidateValue.Length - 2);
                }

                if (string.IsNullOrWhiteSpace(candidateValue))
                {
                    return false;
                }

                value = candidateValue;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Failed to read .env for {key}: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Minimal JWT payload DTO for JsonUtility deserialization.
    /// Field names must match the JWT registered claim names exactly.
    /// </summary>
    [Serializable]
    private class JwtPayload
    {
        public string sub = "";
        public string iss = "";
        public string aud = "";
        public long exp = 0;
        public string displayName = "";
    }

    [Serializable]
    private class JwtHeader
    {
        public string alg = "";
        public string typ = "";
    }

    private static void ApproveAndSpawn(NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;

        if (SpawnPointResolver.TryGetPlayerSpawnTransform(out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            response.Position = spawnPosition;
            response.Rotation = spawnRotation;
        }
        else
        {
            Debug.LogWarning("SpawnPoint not found! Spawning at (0,0,0).");
            response.Position = GetFallbackPlayerSpawnPosition();
            response.Rotation = Quaternion.identity;
        }
    }

    private static Vector3 GetFallbackPlayerSpawnPosition()
    {
        return new Vector3(0f, DefaultWaterSurfaceY, 0f);
    }

    private static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
    {
        response.Approved = false;
        response.CreatePlayerObject = false;
        response.Reason = reason;
        Debug.LogWarning($"[Auth] Connection rejected: {reason}");
    }

    private static bool TryResolveHostSession(out string userId, out string displayName, out string accessToken, out string refreshToken)
    {
        accessToken = BackendSession.AccessToken;
        refreshToken = BackendSession.RefreshToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            userId = string.Empty;
            displayName = string.Empty;
            return false;
        }

        if (!TryValidateJwt(accessToken, out userId, out displayName, out _))
        {
            userId = string.Empty;
            displayName = string.Empty;
            accessToken = string.Empty;
            refreshToken = string.Empty;
            return false;
        }

        return true;
    }

    private void RegisterApprovedSession(ulong clientId, string userId, string displayName, string accessToken, string refreshToken)
    {
        if (_clientSessions.TryGetValue(clientId, out var existingSession))
        {
            CleanupSession(clientId, existingSession);
        }

        _userIdToClientId[userId] = clientId;
        _clientIdToUserId[clientId] = userId;
        _clientIdToDisplayName[clientId] = displayName;
        _clientSessions[clientId] = new AuthenticatedClientSession
        {
            UserId = userId ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            AccessToken = accessToken ?? string.Empty,
            RefreshToken = refreshToken ?? string.Empty,
        };
    }

    private async Task InitializeAuthenticatedPlayerAsync(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!_clientSessions.TryGetValue(clientId, out var session))
        {
            return;
        }

        try
        {
            var player = await WaitForPlayerObjectAsync(clientId, session.LifetimeCts.Token);
            if (player == null || session.LifetimeCts.IsCancellationRequested)
            {
                return;
            }

            session.Player = player;
            player.ApplyPlayerName(string.IsNullOrWhiteSpace(session.DisplayName) ? "Unknown Player" : session.DisplayName);
            player.SetOwnerEntityId(session.UserId);

            _ = await TryLoadPlayerStateIntoPlayerAsync(session, player, session.LifetimeCts.Token);
            _ = await TryLoadGuildAbbreviationIntoPlayerAsync(session, player, session.LifetimeCts.Token);

            if (session.LifetimeCts.IsCancellationRequested)
            {
                return;
            }

            SubscribeToPlayerStatusChanges(session, player);
            SubscribeToWalletChanges(session, player);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Failed to initialize player session for client {clientId}: {ex.Message}");
        }
    }

    private static async Task<Player> WaitForPlayerObjectAsync(ulong clientId, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
                client.PlayerObject != null &&
                client.PlayerObject.TryGetComponent<Player>(out var player))
            {
                return player;
            }

            await Task.Yield();
        }

        return null;
    }

    private void SubscribeToWalletChanges(AuthenticatedClientSession session, Player player)
    {
        if (session == null || player == null)
        {
            return;
        }

        UnsubscribeFromWalletChanges(session);

        session.WalletChangedHandler = (diamonds, gold, experience) =>
        {
            if (session.IsApplyingPlayerState || session.LifetimeCts.IsCancellationRequested)
            {
                return;
            }

            session.PendingGold = Mathf.Max(0, gold);
            session.PendingDiamond = Mathf.Max(0, diamonds);
            session.PendingExperience = Mathf.Max(0, experience);
            session.WalletDirty = true;

            if (!session.WalletSaveLoopRunning)
            {
                session.WalletSaveLoopRunning = true;
                _ = RunWalletSaveLoopAsync(session);
            }
        };

        player.OnRewardWalletChanged += session.WalletChangedHandler;
    }

    private static void UnsubscribeFromWalletChanges(AuthenticatedClientSession session)
    {
        if (session?.Player == null || session.WalletChangedHandler == null)
        {
            return;
        }

        session.Player.OnRewardWalletChanged -= session.WalletChangedHandler;
        session.WalletChangedHandler = null;
    }

    private void SubscribeToPlayerStatusChanges(AuthenticatedClientSession session, Player player)
    {
        if (session == null || player == null)
        {
            return;
        }

        UnsubscribeFromPlayerStatusChanges(session);

        session.SelectedShipChangedHandler = _ =>
        {
            HandleObservedPlayerStatusChanged(session, player);
        };
        session.ActiveActionItemsChangedHandler = _ =>
        {
            HandleObservedPlayerStatusChanged(session, player);
        };
        session.InventoryChangedHandler = () => HandleObservedPlayerStatusChanged(session, player);
        session.ShipCannonLoadoutsChangedHandler = () => HandleObservedPlayerStatusChanged(session, player);

        player.OnSelectedShipChanged += session.SelectedShipChangedHandler;
        player.OnActiveActionItemsChanged += session.ActiveActionItemsChangedHandler;
        player.OnInventoryChanged += session.InventoryChangedHandler;
        player.OnShipCannonLoadoutsChanged += session.ShipCannonLoadoutsChangedHandler;
    }

    private void HandleObservedPlayerStatusChanged(AuthenticatedClientSession session, Player player)
    {
        if (session == null || player == null || session.LifetimeCts.IsCancellationRequested || session.IsApplyingPlayerState)
        {
            return;
        }

        CapturePendingPlayerStatus(session, player);
        QueuePlayerStatusSave(session);
    }

    private static void UnsubscribeFromPlayerStatusChanges(AuthenticatedClientSession session)
    {
        if (session?.Player == null)
        {
            return;
        }

        if (session.SelectedShipChangedHandler != null)
        {
            session.Player.OnSelectedShipChanged -= session.SelectedShipChangedHandler;
            session.SelectedShipChangedHandler = null;
        }

        if (session.ActiveActionItemsChangedHandler != null)
        {
            session.Player.OnActiveActionItemsChanged -= session.ActiveActionItemsChangedHandler;
            session.ActiveActionItemsChangedHandler = null;
        }

        if (session.InventoryChangedHandler != null)
        {
            session.Player.OnInventoryChanged -= session.InventoryChangedHandler;
            session.InventoryChangedHandler = null;
        }

        if (session.ShipCannonLoadoutsChangedHandler != null)
        {
            session.Player.OnShipCannonLoadoutsChanged -= session.ShipCannonLoadoutsChangedHandler;
            session.ShipCannonLoadoutsChangedHandler = null;
        }
    }

    private void QueuePlayerStatusSave(AuthenticatedClientSession session)
    {
        if (session == null || session.LifetimeCts.IsCancellationRequested)
        {
            return;
        }

        session.PlayerStatusDirty = true;
        if (!session.PlayerStatusSaveLoopRunning)
        {
            session.PlayerStatusSaveLoopRunning = true;
            _ = RunPlayerStatusSaveLoopAsync(session);
        }
    }

    private async Task RunPlayerStatusSaveLoopAsync(AuthenticatedClientSession session)
    {
        try
        {
            while (!session.LifetimeCts.IsCancellationRequested)
            {
                if (!session.PlayerStatusDirty)
                {
                    break;
                }

                await session.PlayerStateSyncLock.WaitAsync(session.LifetimeCts.Token);
                bool saved = false;
                try
                {
                    session.PlayerStatusDirty = false;

                    saved = await TrySavePlayerStatusAsync(
                        session,
                        session.PendingSelectedShipId,
                        session.PendingActiveActionItems,
                        session.PendingInventorySnapshot,
                        session.PendingShipCannonLoadoutsSnapshot,
                        session.LifetimeCts.Token);
                }
                finally
                {
                    session.PlayerStateSyncLock.Release();
                }

                if (!saved)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            session.PlayerStatusSaveLoopRunning = false;
            if (session.PlayerStatusDirty && !session.LifetimeCts.IsCancellationRequested)
            {
                session.PlayerStatusSaveLoopRunning = true;
                _ = RunPlayerStatusSaveLoopAsync(session);
            }
        }
    }

    private async Task RunWalletSaveLoopAsync(AuthenticatedClientSession session)
    {
        try
        {
            while (!session.LifetimeCts.IsCancellationRequested)
            {
                await Task.Delay(150, session.LifetimeCts.Token);

                if (!session.WalletDirty)
                {
                    break;
                }

                await session.PlayerStateSyncLock.WaitAsync(session.LifetimeCts.Token);
                bool saved;
                try
                {
                    session.WalletDirty = false;
                    saved = await TrySaveWalletAsync(
                        session,
                        session.PendingGold,
                        session.PendingDiamond,
                        session.PendingExperience,
                        session.LifetimeCts.Token);
                }
                finally
                {
                    session.PlayerStateSyncLock.Release();
                }

                if (!saved)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            session.WalletSaveLoopRunning = false;
            if (session.WalletDirty && !session.LifetimeCts.IsCancellationRequested)
            {
                session.WalletSaveLoopRunning = true;
                _ = RunWalletSaveLoopAsync(session);
            }
        }
    }

    private async Task ProcessCannonPurchaseAsync(Player player, string cannonId)
    {
        if (player == null)
        {
            return;
        }

        if (!_clientSessions.TryGetValue(player.OwnerClientId, out var session))
        {
            player.NotifyCannonPurchaseResult(cannonId, false, "Unable to find your backend session.");
            return;
        }

        var acquiredPlayerStateLock = false;
        try
        {
            await session.PlayerStateSyncLock.WaitAsync(session.LifetimeCts.Token);
            acquiredPlayerStateLock = true;

            if (session.LifetimeCts.IsCancellationRequested)
            {
                return;
            }

            string normalizedCannonId = NormalizeCannonId(cannonId);
            if (!TryGetCannonPurchaseDisplayName(normalizedCannonId, out var cannonDisplayName))
            {
                player.NotifyCannonPurchaseResult(normalizedCannonId, false, "Unknown cannon.");
                return;
            }

            var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());
            for (int attempt = 0; attempt < 3; attempt++)
            {
                session.LifetimeCts.Token.ThrowIfCancellationRequested();

                try
                {
                    var purchase = await playerDataClient.PurchaseCannonAsync(
                        session.AccessToken,
                        normalizedCannonId,
                        player.Gold,
                        player.Diamonds,
                        session.HasWalletVersion ? session.WalletVersion : (int?)null,
                        session.LifetimeCts.Token);

                    session.WalletVersion = purchase.version;
                    session.HasWalletVersion = true;
                    session.PendingGold = Mathf.Max(0, purchase.gold);
                    session.PendingDiamond = Mathf.Max(0, purchase.diamond);
                    session.PendingExperience = Mathf.Max(0, player.Experience);
                    session.WalletDirty = false;

                    session.IsApplyingPlayerState = true;
                    try
                    {
                        player.ApplyPersistedWallet(purchase.gold, purchase.diamond);
                        player.ApplyPersistedInventory(ToInventoryItemStates(purchase.inventoryItems));
                        CapturePendingPlayerStatus(session, player);
                    }
                    finally
                    {
                        session.IsApplyingPlayerState = false;
                    }

                    player.NotifyCannonPurchaseResult(normalizedCannonId, true, $"{cannonDisplayName} purchased.");
                    return;
                }
                catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt < 2)
                {
                    if (!await TryRefreshSessionTokensAsync(session, session.LifetimeCts.Token))
                    {
                        player.NotifyCannonPurchaseResult(normalizedCannonId, false, $"Could not refresh your session: {ex.Message}");
                        return;
                    }
                }
                catch (BackendApiException ex) when (ex.StatusCode == 409 && attempt < 2)
                {
                    if (!await TryReloadWalletVersionAsync(session, session.LifetimeCts.Token))
                    {
                        player.NotifyCannonPurchaseResult(normalizedCannonId, false, $"Could not refresh your latest wallet state: {ex.Message}");
                        return;
                    }
                }
                catch (BackendApiException ex)
                {
                    player.NotifyCannonPurchaseResult(normalizedCannonId, false, ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    player.NotifyCannonPurchaseResult(normalizedCannonId, false, $"Purchase failed: {ex.Message}");
                    return;
                }
            }

            player.NotifyCannonPurchaseResult(normalizedCannonId, false, $"Purchase failed for {cannonDisplayName}.");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (acquiredPlayerStateLock)
            {
                session.PlayerStateSyncLock.Release();
            }
        }
    }

    private async Task ProcessShipPurchaseAsync(Player player, string shipId)
    {
        if (player == null)
        {
            return;
        }

        if (!_clientSessions.TryGetValue(player.OwnerClientId, out var session))
        {
            player.NotifyShipPurchaseResult(shipId, false, "Unable to find your backend session.");
            return;
        }

        var acquiredPlayerStateLock = false;
        try
        {
            await session.PlayerStateSyncLock.WaitAsync(session.LifetimeCts.Token);
            acquiredPlayerStateLock = true;

            if (session.LifetimeCts.IsCancellationRequested)
            {
                return;
            }

            string normalizedShipId = MarketShipCatalogRuntime.NormalizeShipId(shipId);
            if (!TryGetShipPurchaseDisplayName(normalizedShipId, out var shipDisplayName))
            {
                player.NotifyShipPurchaseResult(normalizedShipId, false, "Unknown ship.");
                return;
            }

            if (player.OwnsShip(normalizedShipId))
            {
                player.NotifyShipPurchaseResult(normalizedShipId, false, $"{shipDisplayName} is already owned.");
                return;
            }

            var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());
            for (int attempt = 0; attempt < 3; attempt++)
            {
                session.LifetimeCts.Token.ThrowIfCancellationRequested();

                try
                {
                    var purchase = await playerDataClient.PurchaseShipAsync(
                        session.AccessToken,
                        normalizedShipId,
                        player.Gold,
                        player.Diamonds,
                        session.HasWalletVersion ? session.WalletVersion : (int?)null,
                        session.LifetimeCts.Token);

                    session.WalletVersion = purchase.version;
                    session.HasWalletVersion = true;
                    session.PendingGold = Mathf.Max(0, purchase.gold);
                    session.PendingDiamond = Mathf.Max(0, purchase.diamond);
                    session.PendingExperience = Mathf.Max(0, player.Experience);
                    session.WalletDirty = false;

                    session.IsApplyingPlayerState = true;
                    try
                    {
                        player.ApplyPersistedWallet(purchase.gold, purchase.diamond);
                        player.ApplyPersistedOwnedShips(purchase.ownedShipIds);
                        CapturePendingPlayerStatus(session, player);
                    }
                    finally
                    {
                        session.IsApplyingPlayerState = false;
                    }

                    player.NotifyShipPurchaseResult(normalizedShipId, true, $"{shipDisplayName} purchased.");
                    return;
                }
                catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt < 2)
                {
                    if (!await TryRefreshSessionTokensAsync(session, session.LifetimeCts.Token))
                    {
                        player.NotifyShipPurchaseResult(normalizedShipId, false, $"Could not refresh your session: {ex.Message}");
                        return;
                    }
                }
                catch (BackendApiException ex) when (ex.StatusCode == 409 && attempt < 2)
                {
                    if (!await TryReloadWalletVersionAsync(session, session.LifetimeCts.Token))
                    {
                        player.NotifyShipPurchaseResult(normalizedShipId, false, $"Could not refresh your latest wallet state: {ex.Message}");
                        return;
                    }
                }
                catch (BackendApiException ex)
                {
                    player.NotifyShipPurchaseResult(normalizedShipId, false, ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    player.NotifyShipPurchaseResult(normalizedShipId, false, $"Purchase failed: {ex.Message}");
                    return;
                }
            }

            player.NotifyShipPurchaseResult(normalizedShipId, false, $"Purchase failed for {shipDisplayName}.");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (acquiredPlayerStateLock)
            {
                session.PlayerStateSyncLock.Release();
            }
        }
    }

    private async Task ProcessInventoryItemPurchaseAsync(Player player, string itemId)
    {
        if (player == null)
        {
            return;
        }

        if (!_clientSessions.TryGetValue(player.OwnerClientId, out var session))
        {
            player.NotifyInventoryItemPurchaseResult(itemId, false, "Unable to find your backend session.");
            return;
        }

        var acquiredPlayerStateLock = false;
        try
        {
            await session.PlayerStateSyncLock.WaitAsync(session.LifetimeCts.Token);
            acquiredPlayerStateLock = true;

            if (session.LifetimeCts.IsCancellationRequested)
            {
                return;
            }

            string normalizedItemId = PlayerInventoryState.NormalizeItemId(itemId);
            if (!TryGetInventoryPurchaseDisplayName(normalizedItemId, out string itemDisplayName, out int purchasedAmount))
            {
                player.NotifyInventoryItemPurchaseResult(normalizedItemId, false, "Unknown market item.");
                return;
            }

            var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());
            for (int attempt = 0; attempt < 3; attempt++)
            {
                session.LifetimeCts.Token.ThrowIfCancellationRequested();

                try
                {
                    var purchase = await playerDataClient.PurchaseInventoryItemAsync(
                        session.AccessToken,
                        normalizedItemId,
                        player.Gold,
                        player.Diamonds,
                        session.HasWalletVersion ? session.WalletVersion : (int?)null,
                        session.LifetimeCts.Token);

                    session.WalletVersion = purchase.version;
                    session.HasWalletVersion = true;
                    session.PendingGold = Mathf.Max(0, purchase.gold);
                    session.PendingDiamond = Mathf.Max(0, purchase.diamond);
                    session.PendingExperience = Mathf.Max(0, player.Experience);
                    session.WalletDirty = false;

                    session.IsApplyingPlayerState = true;
                    try
                    {
                        player.ApplyPersistedWallet(purchase.gold, purchase.diamond);
                        player.ApplyPersistedInventory(ToInventoryItemStates(purchase.inventoryItems));
                        CapturePendingPlayerStatus(session, player);
                    }
                    finally
                    {
                        session.IsApplyingPlayerState = false;
                    }

                    int successAmount = purchase.purchasedAmount > 0 ? purchase.purchasedAmount : purchasedAmount;
                    player.NotifyInventoryItemPurchaseResult(normalizedItemId, true, $"{itemDisplayName} x{successAmount:N0} purchased.");
                    return;
                }
                catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt < 2)
                {
                    if (!await TryRefreshSessionTokensAsync(session, session.LifetimeCts.Token))
                    {
                        player.NotifyInventoryItemPurchaseResult(normalizedItemId, false, $"Could not refresh your session: {ex.Message}");
                        return;
                    }
                }
                catch (BackendApiException ex) when (ex.StatusCode == 409 && attempt < 2)
                {
                    if (!await TryReloadWalletVersionAsync(session, session.LifetimeCts.Token))
                    {
                        player.NotifyInventoryItemPurchaseResult(normalizedItemId, false, $"Could not refresh your latest wallet state: {ex.Message}");
                        return;
                    }
                }
                catch (BackendApiException ex)
                {
                    player.NotifyInventoryItemPurchaseResult(normalizedItemId, false, ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    player.NotifyInventoryItemPurchaseResult(normalizedItemId, false, $"Purchase failed: {ex.Message}");
                    return;
                }
            }

            player.NotifyInventoryItemPurchaseResult(normalizedItemId, false, $"Purchase failed for {itemDisplayName}.");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (acquiredPlayerStateLock)
            {
                session.PlayerStateSyncLock.Release();
            }
        }
    }

    private static async Task<bool> TryLoadPlayerStateIntoPlayerAsync(AuthenticatedClientSession session, Player player, CancellationToken cancellationToken)
    {
        var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());

        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var playerState = await playerDataClient.GetMarketStateAsync(session.AccessToken, cancellationToken);
                session.WalletVersion = playerState.version;
                session.HasWalletVersion = true;
                session.PendingGold = Mathf.Max(0, playerState.gold);
                session.PendingDiamond = Mathf.Max(0, playerState.diamond);
                session.PendingExperience = Mathf.Max(0, playerState.experience);
                session.WalletDirty = false;

                session.IsApplyingPlayerState = true;
                try
                {
                    player.ApplyPersistedWallet(playerState.gold, playerState.diamond, playerState.experience);
                    player.ApplyPersistedOwnedShips(playerState.ownedShipIds);
                    player.ApplyPersistedInventory(ToInventoryItemStates(playerState.inventoryItems));
                    player.ApplyPersistedShipCannonLoadouts(ToShipCannonLoadoutStates(playerState.shipCannonLoadouts));
                    player.ApplyPersistedSelectedShip(playerState.selectedShipId);
                    player.ApplyPersistedActiveActionItems((PlayerActionItemType)playerState.activeActionItems);
                    CapturePendingPlayerStatus(session, player);
                }
                finally
                {
                    session.IsApplyingPlayerState = false;
                }

                return true;
            }
            catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt == 0)
            {
                if (!await TryRefreshSessionTokensAsync(session, cancellationToken))
                {
                    break;
                }
            }
            catch (BackendApiException ex)
            {
                Debug.LogWarning($"[PlayerState] Failed to load state for user {session.UserId} via {ex.Url}: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerState] Failed to load state for user {session.UserId}: {ex.Message}");
                break;
            }
        }

        return false;
    }

    private static async Task<bool> TryLoadGuildAbbreviationIntoPlayerAsync(AuthenticatedClientSession session, Player player, CancellationToken cancellationToken)
    {
        if (session == null || player == null)
        {
            return false;
        }

        var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());

        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var guildState = await playerDataClient.GetGuildsAsync(session.AccessToken, cancellationToken);
                string guildAbbreviation = string.Empty;

                if (guildState?.guilds != null)
                {
                    for (int index = 0; index < guildState.guilds.Length; index++)
                    {
                        GuildSummaryResponse guild = guildState.guilds[index];
                        if (guild == null)
                        {
                            continue;
                        }

                        bool matchesCurrentGuildId = !string.IsNullOrWhiteSpace(guildState.currentGuildId) &&
                                                     string.Equals(guild.id, guildState.currentGuildId, StringComparison.OrdinalIgnoreCase);
                        if (matchesCurrentGuildId || guild.isCurrentPlayerMember)
                        {
                            guildAbbreviation = guild.tag ?? string.Empty;
                            break;
                        }
                    }
                }

                player.ApplyGuildAbbreviation(guildAbbreviation);
                return true;
            }
            catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt == 0)
            {
                if (!await TryRefreshSessionTokensAsync(session, cancellationToken))
                {
                    break;
                }
            }
            catch (BackendApiException ex)
            {
                Debug.LogWarning($"[Guilds] Failed to load guild data for user {session.UserId} via {ex.Url}: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Guilds] Failed to load guild data for user {session.UserId}: {ex.Message}");
                break;
            }
        }

        player.ApplyGuildAbbreviation(string.Empty);
        return false;
    }

    private async Task RefreshPlayerGuildAbbreviationAsync(Player player)
    {
        if (player == null)
        {
            return;
        }

        if (!_clientSessions.TryGetValue(player.OwnerClientId, out var session))
        {
            return;
        }

        try
        {
            await TryLoadGuildAbbreviationIntoPlayerAsync(session, player, session.LifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<bool> TrySaveWalletAsync(AuthenticatedClientSession session, int gold, int diamond, int experience, CancellationToken cancellationToken)
    {
        var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var wallet = await playerDataClient.UpdateWalletAsync(
                    session.AccessToken,
                    gold,
                    diamond,
                    experience,
                    session.HasWalletVersion ? session.WalletVersion : (int?)null,
                    cancellationToken);

                session.WalletVersion = wallet.version;
                session.HasWalletVersion = true;
                session.PendingGold = Mathf.Max(0, wallet.gold);
                session.PendingDiamond = Mathf.Max(0, wallet.diamond);
                session.PendingExperience = Mathf.Max(0, wallet.experience);
                return true;
            }
            catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt < 2)
            {
                if (!await TryRefreshSessionTokensAsync(session, cancellationToken))
                {
                    Debug.LogWarning($"[Wallet] Access token refresh failed for user {session.UserId}: {ex.Message}");
                    return false;
                }
            }
            catch (BackendApiException ex) when (ex.StatusCode == 409 && attempt < 2)
            {
                if (!await TryReloadWalletVersionAsync(session, cancellationToken))
                {
                    Debug.LogWarning($"[Wallet] Wallet version refresh failed for user {session.UserId}: {ex.Message}");
                    return false;
                }
            }
            catch (BackendApiException ex)
            {
                Debug.LogWarning($"[Wallet] Failed to save wallet for user {session.UserId} via {ex.Url}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Wallet] Failed to save wallet for user {session.UserId}: {ex.Message}");
                return false;
            }
        }

        return false;
    }

    private static async Task<bool> TryReloadWalletVersionAsync(AuthenticatedClientSession session, CancellationToken cancellationToken)
    {
        var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());
        var wallet = await playerDataClient.GetWalletAsync(session.AccessToken, cancellationToken);
        session.WalletVersion = wallet.version;
        session.HasWalletVersion = true;
        session.PendingGold = Mathf.Max(0, wallet.gold);
        session.PendingDiamond = Mathf.Max(0, wallet.diamond);
        session.PendingExperience = Mathf.Max(0, wallet.experience);
        return true;
    }

    private static async Task<bool> TrySavePlayerStatusAsync(
        AuthenticatedClientSession session,
        string selectedShipId,
        PlayerActionItemType activeActionItems,
        string inventorySnapshot,
        string shipCannonLoadoutsSnapshot,
        CancellationToken cancellationToken)
    {
        var playerDataClient = new BackendPlayerDataClient(ResolvePlayerDataBaseUrl());

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var status = await playerDataClient.UpdatePlayerStatusAsync(
                    session.AccessToken,
                    selectedShipId,
                    activeActionItems,
                    inventorySnapshot,
                    shipCannonLoadoutsSnapshot,
                    cancellationToken);

                session.WalletVersion = status.version;
                session.HasWalletVersion = true;
                return true;
            }
            catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt < 2)
            {
                if (!await TryRefreshSessionTokensAsync(session, cancellationToken))
                {
                    Debug.LogWarning($"[PlayerStatus] Access token refresh failed for user {session.UserId}: {ex.Message}");
                    return false;
                }
            }
            catch (BackendApiException ex) when (ex.StatusCode == 409 && attempt < 2)
            {
                if (!await TryReloadWalletVersionAsync(session, cancellationToken))
                {
                    Debug.LogWarning($"[PlayerStatus] Player state version refresh failed for user {session.UserId}: {ex.Message}");
                    return false;
                }
            }
            catch (BackendApiException ex)
            {
                Debug.LogWarning($"[PlayerStatus] Failed to save status for user {session.UserId} via {ex.Url}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerStatus] Failed to save status for user {session.UserId}: {ex.Message}");
                return false;
            }
        }

        return false;
    }

    private static async Task<bool> TryRefreshSessionTokensAsync(AuthenticatedClientSession session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            return false;
        }

        try
        {
            var authClient = new BackendAuthClient(ResolveAuthBaseUrl());
            var refreshed = await authClient.RefreshAsync(session.RefreshToken, cancellationToken);
            if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.accessToken) || string.IsNullOrWhiteSpace(refreshed.refreshToken))
            {
                return false;
            }

            session.AccessToken = refreshed.accessToken;
            session.RefreshToken = refreshed.refreshToken;

            if (session.Player != null && session.Player.IsSpawned)
            {
                session.Player.SyncBackendTokensToOwner(refreshed.accessToken, refreshed.refreshToken, refreshed.expiresInSeconds);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Failed to refresh backend session tokens for user {session.UserId}: {ex.Message}");
            return false;
        }
    }

    private void CleanupSession(ulong clientId, AuthenticatedClientSession session)
    {
        if (session == null)
        {
            return;
        }

        UnsubscribeFromPlayerStatusChanges(session);
        UnsubscribeFromWalletChanges(session);

        if (!session.LifetimeCts.IsCancellationRequested)
        {
            session.LifetimeCts.Cancel();
        }

        session.LifetimeCts.Dispose();
        session.Player = null;
        session.IsApplyingPlayerState = false;
        session.WalletDirty = false;
        session.PendingExperience = 0;
        session.PendingSelectedShipId = string.Empty;
        session.PendingActiveActionItems = PlayerActionItemType.None;
        session.PendingInventorySnapshot = string.Empty;
        session.PendingShipCannonLoadoutsSnapshot = string.Empty;
        session.WalletSaveLoopRunning = false;
        session.PlayerStatusDirty = false;
        session.PlayerStatusSaveLoopRunning = false;

        if (!string.IsNullOrWhiteSpace(session.UserId) &&
            _userIdToClientId.TryGetValue(session.UserId, out ulong mappedClientId) &&
            mappedClientId == clientId)
        {
            _userIdToClientId.Remove(session.UserId);
        }

        _clientIdToUserId.Remove(clientId);
        _clientIdToDisplayName.Remove(clientId);
    }

    private static string ResolveServerApiKey()
    {
        string configured = Environment.GetEnvironmentVariable(ServerApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (TryReadDotEnvVariable(ServerApiKeyEnvVar, out string fromDotEnv))
        {
            return fromDotEnv;
        }

        return LocalDevFallbackServerApiKey;
    }

    private static string ResolveAuthBaseUrl()
    {
        return ResolveConfiguredBaseUrl(AuthBaseUrlEnvVar, BackendSession.AuthBaseUrl, BackendSession.DefaultAuthBaseUrl);
    }

    private static string ResolvePlayerDataBaseUrl()
    {
        return ResolveConfiguredBaseUrl(PlayerDataBaseUrlEnvVar, BackendSession.PlayerDataBaseUrl, BackendSession.DefaultPlayerDataBaseUrl);
    }

    private static string NormalizeCannonId(string cannonId)
    {
        return string.IsNullOrWhiteSpace(cannonId)
            ? string.Empty
            : cannonId.Trim().ToLowerInvariant();
    }

    private static bool TryGetCannonPurchaseDisplayName(string cannonId, out string displayName)
    {
        string normalizedCannonId = NormalizeCannonId(cannonId);
        if (MarketCannonCatalogRuntime.TryGetCannon(normalizedCannonId, out MarketCannonData cannon) && cannon != null)
        {
            displayName = string.IsNullOrWhiteSpace(cannon.DisplayName) ? normalizedCannonId : cannon.DisplayName;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    private static bool TryGetShipPurchaseDisplayName(string shipId, out string displayName)
    {
        string normalizedShipId = MarketShipCatalogRuntime.NormalizeShipId(shipId);
        if (MarketShipCatalogRuntime.TryGetShip(normalizedShipId, out MarketShipData ship) && ship != null)
        {
            displayName = string.IsNullOrWhiteSpace(ship.DisplayName) ? normalizedShipId : ship.DisplayName;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    private static bool TryGetInventoryPurchaseDisplayName(string itemId, out string displayName, out int purchasedAmount)
    {
        if (MarketInventoryCatalogRuntime.TryGetItem(itemId, out MarketInventoryItemData item) && item != null)
        {
            displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName;
            purchasedAmount = item.PurchaseAmount;
            return true;
        }

        displayName = string.Empty;
        purchasedAmount = 0;
        return false;
    }

    private static void CapturePendingPlayerStatus(AuthenticatedClientSession session, Player player)
    {
        if (session == null || player == null)
        {
            return;
        }

        session.PendingSelectedShipId = player.SelectedShipId ?? string.Empty;
        session.PendingActiveActionItems = player.ActiveActionItems;
        session.PendingInventorySnapshot = player.InventorySnapshot ?? string.Empty;
        session.PendingShipCannonLoadoutsSnapshot = player.ShipCannonLoadoutsSnapshot ?? string.Empty;
    }

    private static List<PlayerInventoryItemState> ToInventoryItemStates(IEnumerable<InventoryItemStackResponse> inventoryItems)
    {
        var items = new List<PlayerInventoryItemState>();
        if (inventoryItems == null)
        {
            return items;
        }

        foreach (InventoryItemStackResponse item in inventoryItems)
        {
            if (item == null)
            {
                continue;
            }

            items.Add(new PlayerInventoryItemState(item.itemId, item.amount));
        }

        return items;
    }

    private static List<ShipCannonLoadoutState> ToShipCannonLoadoutStates(IEnumerable<ShipCannonLoadoutResponse> loadouts)
    {
        var states = new List<ShipCannonLoadoutState>();
        if (loadouts == null)
        {
            return states;
        }

        foreach (ShipCannonLoadoutResponse loadout in loadouts)
        {
            if (loadout == null)
            {
                continue;
            }

            states.Add(new ShipCannonLoadoutState(loadout.shipId, ToInventoryItemStates(loadout.cannons)));
        }

        return states;
    }

    private static string ResolveConfiguredBaseUrl(string envVarName, string configuredSessionUrl, string fallbackUrl)
    {
        string configured = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return NormalizeBaseUrl(configured);
        }

        if (TryReadDotEnvVariable(envVarName, out string fromDotEnv))
        {
            return NormalizeBaseUrl(fromDotEnv);
        }

        if (!string.IsNullOrWhiteSpace(configuredSessionUrl))
        {
            return NormalizeBaseUrl(configuredSessionUrl);
        }

        return NormalizeBaseUrl(fallbackUrl);
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
#endif

    public void RequestCannonPurchase(Player player, string cannonId)
    {
        if (player == null)
        {
            return;
        }

#if UNITY_SERVER || UNITY_EDITOR
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            player.NotifyCannonPurchaseResult(cannonId, false, "The market is only available while connected to the server.");
            return;
        }

        _ = ProcessCannonPurchaseAsync(player, cannonId);
#else
        player.NotifyCannonPurchaseResult(cannonId, false, "Cannon purchases are only available on the game server.");
#endif
    }

    public void RequestShipPurchase(Player player, string shipId)
    {
        if (player == null)
        {
            return;
        }

#if UNITY_SERVER || UNITY_EDITOR
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            player.NotifyShipPurchaseResult(shipId, false, "The market is only available while connected to the server.");
            return;
        }

        _ = ProcessShipPurchaseAsync(player, shipId);
#else
        player.NotifyShipPurchaseResult(shipId, false, "Ship purchases are only available on the game server.");
#endif
    }

    public void RequestInventoryItemPurchase(Player player, string itemId)
    {
        if (player == null)
        {
            return;
        }

#if UNITY_SERVER || UNITY_EDITOR
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            player.NotifyInventoryItemPurchaseResult(itemId, false, "The market is only available while connected to the server.");
            return;
        }

        _ = ProcessInventoryItemPurchaseAsync(player, itemId);
#else
        player.NotifyInventoryItemPurchaseResult(itemId, false, "Item purchases are only available on the game server.");
#endif
    }

    public void RequestGuildAbbreviationRefresh(Player player)
    {
        if (player == null)
        {
            return;
        }

#if UNITY_SERVER || UNITY_EDITOR
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        _ = RefreshPlayerGuildAbbreviationAsync(player);
#endif
    }

    public bool TryGetAuthenticatedUserId(ulong clientId, out string userId)
    {
#if UNITY_SERVER || UNITY_EDITOR
        if (_clientSessions.TryGetValue(clientId, out var session) && !string.IsNullOrWhiteSpace(session.UserId))
        {
            userId = session.UserId;
            return true;
        }
#endif
        userId = string.Empty;
        return false;
    }

    public bool TryGetClientIdForUserId(string userId, out ulong clientId)
    {
#if UNITY_SERVER || UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(userId) && _userIdToClientId.TryGetValue(userId, out clientId))
        {
            return true;
        }
#endif
        clientId = ulong.MaxValue;
        return false;
    }

    public static string ResolvePlayerDataBaseUrlForServer()
    {
#if UNITY_SERVER || UNITY_EDITOR
        return ResolvePlayerDataBaseUrl();
#else
        return BackendSession.GetPlayerDataBaseUrlOrDefault(BackendSession.PlayerDataBaseUrl);
#endif
    }

    public static string ResolveServerApiKeyForWorldObjects()
    {
#if UNITY_SERVER || UNITY_EDITOR
        return ResolveServerApiKey();
#else
        return string.Empty;
#endif
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (NetworkManager.Singleton != null)
        {
#if UNITY_SERVER || UNITY_EDITOR
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
#endif
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

#if UNITY_SERVER || UNITY_EDITOR
        foreach (var session in _clientSessions)
        {
            CleanupSession(session.Key, session.Value);
        }

        _clientSessions.Clear();
#endif
    }

#if UNITY_SERVER || UNITY_EDITOR
    private async void OnServerStarted()
    {
        Debug.Log("Server Started");

        try
        {
            var buildManager = await WaitForIslandBuildManagerAsync();
            if (buildManager != null)
            {
                await buildManager.RestorePersistentTurretsAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldObjects] Failed to restore persistent turrets on server start: {ex.Message}");
        }
    }

    private static async Task<IslandBuildManager> WaitForIslandBuildManagerAsync()
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            if (IslandBuildManager.Instance != null)
            {
                return IslandBuildManager.Instance;
            }

            await Task.Yield();
        }

        return null;
    }
#endif

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client Connected: {clientId}");
#if UNITY_SERVER || UNITY_EDITOR
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            _ = InitializeAuthenticatedPlayerAsync(clientId);
        }
#endif
    }

    private void OnClientDisconnected(ulong clientId)
    {
        bool isLocalClientDisconnect = NetworkManager.Singleton != null &&
                                       !NetworkManager.Singleton.IsServer &&
                                       clientId == NetworkManager.Singleton.LocalClientId;

        if (isLocalClientDisconnect && !string.IsNullOrWhiteSpace(NetworkManager.Singleton.DisconnectReason))
        {
            Debug.LogWarning($"Client Disconnected: {clientId}. Server reason: {NetworkManager.Singleton.DisconnectReason}");
        }

#if UNITY_SERVER || UNITY_EDITOR
        // Clean up session tracking.
        if (_clientSessions.TryGetValue(clientId, out var session))
        {
            _clientSessions.Remove(clientId);
            CleanupSession(clientId, session);
            Debug.Log($"Client Disconnected: {clientId} (user: {session.UserId}).");
        }
        else if (_clientIdToUserId.TryGetValue(clientId, out string userId))
        {
            _clientIdToUserId.Remove(clientId);
            _clientIdToDisplayName.Remove(clientId);
            if (_userIdToClientId.TryGetValue(userId, out ulong mapped) && mapped == clientId)
            {
                _userIdToClientId.Remove(userId);
            }

            Debug.Log($"Client Disconnected: {clientId} (user: {userId}).");
        }
        else
        {
            if (!isLocalClientDisconnect || string.IsNullOrWhiteSpace(NetworkManager.Singleton?.DisconnectReason))
            {
                Debug.Log($"Client Disconnected: {clientId}. Possible Connection Timeout or Server Shutdown.");
            }
        }
#else
        if (!isLocalClientDisconnect || string.IsNullOrWhiteSpace(NetworkManager.Singleton?.DisconnectReason))
        {
            Debug.Log($"Client Disconnected: {clientId}. Possible Connection Timeout or Server Shutdown.");
        }
#endif
    }

    private void StartHeadlessServer()
    {
#if UNITY_SERVER || UNITY_EDITOR
        Debug.Log("Starting Headless Server via CLI...");
        ConfigureTransportForCli();

        bool started = NetworkManager.Singleton.StartServer();
        if (started)
        {
            Debug.Log("Headless Server Started. Listening for connections...");
        }
        else
        {
            Debug.LogError("Failed to start Headless Server.");
        }
#else
        Debug.LogError("Cannot start server mode from a client-only build.");
#endif
    }

    private void StartClientFromCli()
    {
        Debug.Log("Starting Client via CLI...");
        ConfigureTransportForCli();
        ApplyCliConnectionPayload();

        bool started = NetworkManager.Singleton != null && NetworkManager.Singleton.StartClient();
        if (started)
        {
            Debug.Log("Client started. Connecting...");
        }
        else
        {
            Debug.LogError("Failed to start client.");
        }
    }

    private static void ApplyCliConnectionPayload()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        // Preserve existing payload if already set by gameplay/login flow.
        byte[] existing = NetworkManager.Singleton.NetworkConfig.ConnectionData;
        if (existing != null && existing.Length > 0)
        {
            return;
        }

        string accessToken = Environment.GetEnvironmentVariable("SEAWARS_ACCESS_TOKEN");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        string refreshToken = Environment.GetEnvironmentVariable("SEAWARS_REFRESH_TOKEN");
        NetworkManager.Singleton.NetworkConfig.ConnectionData = NetcodeConnectionPayload.Build(accessToken, refreshToken);
    }

    private void StartHostFromCli()
    {
#if UNITY_SERVER || UNITY_EDITOR
        Debug.Log("Starting Host via CLI...");
        ConfigureTransportForCli();

        bool started = NetworkManager.Singleton != null && NetworkManager.Singleton.StartHost();
        if (started)
        {
            Debug.Log("Host started. Listening and connecting locally...");
        }
        else
        {
            Debug.LogError("Failed to start host.");
        }
#else
        Debug.LogError("Cannot start host mode from a client-only build.");
#endif
    }

    private static void ConfigureTransportForCli()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is not UnityTransport utp)
        {
            return;
        }

        string connectAddress = Environment.GetEnvironmentVariable("SEAWARS_CONNECT_ADDRESS");
        string portRaw = Environment.GetEnvironmentVariable("SEAWARS_PORT");

        if (!string.IsNullOrWhiteSpace(connectAddress))
        {
            utp.ConnectionData.Address = connectAddress.Trim();
        }

        if (!string.IsNullOrWhiteSpace(portRaw) && ushort.TryParse(portRaw.Trim(), out ushort port))
        {
            utp.ConnectionData.Port = port;
        }

#if UNITY_SERVER || UNITY_EDITOR
        string listenAddress = Environment.GetEnvironmentVariable("SEAWARS_SERVER_LISTEN_ADDRESS");
        if (string.IsNullOrWhiteSpace(listenAddress))
        {
            listenAddress = "0.0.0.0";
        }

        utp.ConnectionData.ServerListenAddress = listenAddress.Trim();
#endif
    }
}
