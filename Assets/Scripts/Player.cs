using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using SeaWars.Utility;

/// <summary>
/// Core player component that handles identity, health, and combat.
/// Attach to the player prefab alongside ClickToMove.
/// Uses NetworkVariables for automatic state synchronization.
/// </summary>
public class Player : NetworkBehaviour, IHealthSystem, ICombat, ITargetable
{
    /// <summary>
    /// Static event fired when the LOCAL player spawns.
    /// Subscribe from Awake() - static events survive instance creation.
    /// </summary>
    public static event Action<Transform> LocalPlayerSpawned;

    /// <summary>
    /// Reference to the current local player, or null if not spawned yet.
    /// </summary>
    public static Player LocalPlayer { get; private set; }

    [Header("Config")]
    [SerializeField] private PrefabGameplayConfig gameplayConfig;

    [Header("Health Settings")]
    [SerializeField] private int m_maxHealth = 100;
    [SerializeField, Min(0.5f)] private float respawnDelaySeconds = 5f;

    // NetworkVariable for automatic health synchronization across all clients
    private NetworkVariable<int> m_networkHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<bool> m_networkIsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<double> m_networkRespawnAtServerTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Identity")]
    public NetworkVariable<Unity.Collections.FixedString64Bytes> PlayerName = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        "Player",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<FixedString128Bytes> m_ownerEntityId = new NetworkVariable<FixedString128Bytes>(
        new FixedString128Bytes(),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_networkPearls = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_networkGold = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_networkExperience = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<FixedString512Bytes> m_ownedCannonIdsCsv = new NetworkVariable<FixedString512Bytes>(
        new FixedString512Bytes(),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server
    );

    [Header("Repair Settings")]
    [SerializeField] public float repairRate = 2.0f;
    [SerializeField] public int repairAmount = 5;

    private Coroutine repairCoroutine;
    private Coroutine respawnCoroutine;
    private readonly List<ulong> combatAggressorObjectIds = new List<ulong>();
    private NetworkVariable<int> m_networkCombatAggressorCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Boarding")]
    [SerializeField, Min(0f)] private float boardingDistance = 25f;
    [SerializeField, Range(0f, 1f)] private float boardingTargetHealthFraction = 0.5f;
    private NetworkVariable<bool> m_networkHasBoardedThisLife = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private int selectedCannonAmmoIndex;

    public event Action<float> OnHealthChanged = delegate { };
    public event Action<int, int, int> OnRewardWalletChanged = delegate { };
    public event Action OnOwnedCannonsChanged = delegate { };
    public event Action<string, bool, string> OnCannonPurchaseResult = delegate { };
    public event Action<string, bool> OnIslandActionFeedback = delegate { };

    // Public properties
    public int MaxHealth => m_maxHealth;
    public int CurrentHealth => m_networkHealth.Value;
    public bool IsDead => m_networkIsDead.Value || CurrentHealth <= 0;
    public bool IsDeadNetworkState => m_networkIsDead.Value;
    public int Pearls => m_networkPearls.Value;
    public int Diamonds => m_networkPearls.Value;
    public int Gold => m_networkGold.Value;
    public int Experience => m_networkExperience.Value;
    public string OwnerEntityId => m_ownerEntityId.Value.ToString();
    public string OwnedCannonIdsCsv => m_ownedCannonIdsCsv.Value.ToString();
    public GameObject TargetGameObject => gameObject;
    public bool CanBeTargeted => IsSpawned && isActiveAndEnabled && CurrentHealth > 0;
    public float RespawnTimeRemainingSeconds
    {
        get
        {
            if (!m_networkIsDead.Value || NetworkManager == null || !IsSpawned)
            {
                return 0f;
            }

            double respawnAt = m_networkRespawnAtServerTime.Value;
            double serverTime = NetworkManager.ServerTime.Time;

            if (double.IsNaN(respawnAt) || double.IsInfinity(respawnAt) ||
                double.IsNaN(serverTime) || double.IsInfinity(serverTime))
            {
                return 0f;
            }

            double remaining = respawnAt - serverTime;
            if (remaining <= 0d)
            {
                return 0f;
            }

            return remaining > int.MaxValue - 1
                ? int.MaxValue - 1
                : (float)remaining;
        }
    }

    /// <summary>
    /// The network client ID of this player.
    /// </summary>
    public ulong ClientId => OwnerClientId;

    private void Awake()
    {
        if (TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.SpawnWithObservers = true;
            networkObject.CheckObjectVisibility = clientId => FogOfWarNetworkVisibilityController.ShouldPlayerBeVisibleToClient(this, clientId);
        }

        ShadowCastingUtility.DisableShadowCastingInChildren(transform);
        EnsureWorldNameplate();
        ApplyGameplayConfig();

        // Subscribe to network health changes
        m_networkHealth.OnValueChanged += OnNetworkHealthChanged;
        PlayerName.OnValueChanged += OnPlayerNameChanged;
        m_networkPearls.OnValueChanged += OnRewardWalletValueChanged;
        m_networkGold.OnValueChanged += OnRewardWalletValueChanged;
        m_networkExperience.OnValueChanged += OnRewardWalletValueChanged;
        m_ownedCannonIdsCsv.OnValueChanged += OnOwnedCannonIdsChanged;
    }

    public override void OnDestroy()
    {
        CombatTargetingUtility.Unregister(this);

        m_networkHealth.OnValueChanged -= OnNetworkHealthChanged;
        PlayerName.OnValueChanged -= OnPlayerNameChanged;
        m_networkPearls.OnValueChanged -= OnRewardWalletValueChanged;
        m_networkGold.OnValueChanged -= OnRewardWalletValueChanged;
        m_networkExperience.OnValueChanged -= OnRewardWalletValueChanged;
        m_ownedCannonIdsCsv.OnValueChanged -= OnOwnedCannonIdsChanged;
        base.OnDestroy();
    }

    /// <summary>
    /// Called when health changes on the network (synced from server to all clients).
    /// </summary>
    private void OnNetworkHealthChanged(int previousValue, int newValue)
    {
        int maxHealth = Mathf.Max(MaxHealth, 1);
        OnHealthChanged?.Invoke(Mathf.Clamp01(newValue / (float)maxHealth));

        // Floating damage / heal numbers
        int delta = previousValue - newValue;
        if (delta > 0)
        {
            DamageNumberService.Show(transform.position, delta, false);
        }
        else if (delta < 0)
        {
            DamageNumberService.Show(transform.position, -delta, true);
        }

        if (newValue <= 0 && previousValue > 0)
        {
            // Death was just triggered
            OnDeath();
        }
        else if (previousValue <= 0 && newValue > 0)
        {
            OnRespawned();
        }
    }

    private void OnPlayerNameChanged(Unity.Collections.FixedString64Bytes previousValue, Unity.Collections.FixedString64Bytes newValue)
    {
        if (TryGetComponent<WorldNameplateUI>(out var ui))
        {
            ui.SetDisplayNameOverride(newValue.ToString());
        }
    }

    private void OnRewardWalletValueChanged(int previousValue, int newValue)
    {
        OnRewardWalletChanged?.Invoke(Pearls, Gold, Experience);
    }

    private void OnOwnedCannonIdsChanged(FixedString512Bytes previousValue, FixedString512Bytes newValue)
    {
        OnOwnedCannonsChanged?.Invoke();
    }

    public void ApplyPersistedWallet(int gold, int diamond)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedWallet is server-only.");
            return;
        }

        m_networkGold.Value = Mathf.Max(0, gold);
        m_networkPearls.Value = Mathf.Max(0, diamond);
    }

    public void SetOwnerEntityId(string ownerEntityId)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: SetOwnerEntityId is server-only.");
            return;
        }

        string normalizedOwnerEntityId = string.IsNullOrWhiteSpace(ownerEntityId)
            ? string.Empty
            : ownerEntityId.Trim();
        m_ownerEntityId.Value = new FixedString128Bytes(normalizedOwnerEntityId);
    }

    public void ApplyPersistedOwnedCannons(IReadOnlyList<string> ownedCannonIds)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedOwnedCannons is server-only.");
            return;
        }

        m_ownedCannonIdsCsv.Value = new FixedString512Bytes(BuildOwnedCannonsCsv(ownedCannonIds));
    }

    public bool OwnsCannon(string cannonId)
    {
        return ContainsOwnedCannonId(m_ownedCannonIdsCsv.Value.ToString(), cannonId);
    }

    public string[] GetOwnedCannonIds()
    {
        return ParseOwnedCannonsCsv(m_ownedCannonIdsCsv.Value.ToString());
    }

    public bool RequestCannonPurchase(string cannonId)
    {
        if (!IsOwner)
        {
            return false;
        }

        string normalizedCannonId = NormalizeOwnedCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedCannonId))
        {
            return false;
        }

        RequestCannonPurchaseServerRpc(normalizedCannonId);
        return true;
    }

    public void NotifyCannonPurchaseResult(string cannonId, bool success, string message)
    {
        if (!IsServer)
        {
            return;
        }

        PushCannonPurchaseResultClientRpc(
            NormalizeOwnedCannonId(cannonId),
            success,
            message ?? string.Empty);
    }

    public void SyncBackendTokensToOwner(string accessToken, string refreshToken, int expiresInSeconds)
    {
        if (!IsServer)
        {
            return;
        }

        PushBackendTokensClientRpc(accessToken ?? string.Empty, refreshToken ?? string.Empty, Mathf.Max(1, expiresInSeconds));
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushBackendTokensClientRpc(string accessToken, string refreshToken, int expiresInSeconds)
    {
        BackendSession.SetTokens(accessToken, refreshToken, expiresInSeconds);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestCannonPurchaseServerRpc(string cannonId)
    {
        if (MultiplayerController.Instance == null)
        {
            NotifyCannonPurchaseResult(cannonId, false, "The multiplayer controller is not ready.");
            return;
        }

        MultiplayerController.Instance.RequestCannonPurchase(this, NormalizeOwnedCannonId(cannonId));
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushCannonPurchaseResultClientRpc(string cannonId, bool success, string message)
    {
        OnCannonPurchaseResult?.Invoke(
            NormalizeOwnedCannonId(cannonId),
            success,
            string.IsNullOrWhiteSpace(message) ? (success ? "Purchase completed." : "Purchase failed.") : message);
    }

    public bool RequestBuildTurret(Vector3 requestedPosition)
    {
        if (!IsOwner)
        {
            return false;
        }

        if (IsServer)
        {
            _ = HandleBuildTurretRequestAsync(requestedPosition);
        }
        else
        {
            RequestBuildTurretServerRpc(requestedPosition);
        }

        return true;
    }

    public bool RequestMoveTurret(ulong turretNetworkObjectId, Vector3 requestedPosition)
    {
        if (!IsOwner || turretNetworkObjectId == 0)
        {
            return false;
        }

        if (IsServer)
        {
            _ = HandleMoveTurretRequestAsync(turretNetworkObjectId, requestedPosition);
        }
        else
        {
            RequestMoveTurretServerRpc(turretNetworkObjectId, requestedPosition);
        }

        return true;
    }

    public bool RequestDeleteTurret(ulong turretNetworkObjectId)
    {
        if (!IsOwner || turretNetworkObjectId == 0)
        {
            return false;
        }

        if (IsServer)
        {
            _ = HandleDeleteTurretRequestAsync(turretNetworkObjectId);
        }
        else
        {
            RequestDeleteTurretServerRpc(turretNetworkObjectId);
        }

        return true;
    }

    public bool TrySpendGold(int amount)
    {
        if (!IsServer || amount < 0)
        {
            return false;
        }

        if (amount == 0)
        {
            return true;
        }

        if (m_networkGold.Value < amount)
        {
            return false;
        }

        m_networkGold.Value -= amount;
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestBuildTurretServerRpc(Vector3 requestedPosition)
    {
        _ = HandleBuildTurretRequestAsync(requestedPosition);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestMoveTurretServerRpc(ulong turretNetworkObjectId, Vector3 requestedPosition)
    {
        _ = HandleMoveTurretRequestAsync(turretNetworkObjectId, requestedPosition);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestDeleteTurretServerRpc(ulong turretNetworkObjectId)
    {
        _ = HandleDeleteTurretRequestAsync(turretNetworkObjectId);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushIslandActionFeedbackClientRpc(string message, bool success)
    {
        OnIslandActionFeedback?.Invoke(message ?? string.Empty, success);
    }

    private async Task HandleBuildTurretRequestAsync(Vector3 requestedPosition)
    {
        if (!TryGetIslandBuildManager(out IslandBuildManager buildManager, out string failureMessage))
        {
            SendIslandActionFeedback(false, failureMessage);
            return;
        }

        try
        {
            var result = await buildManager.TryServerBuildTurretAsync(this, requestedPosition);
            SendIslandActionFeedback(result.success, result.message);
        }
        catch (Exception ex)
        {
            SendIslandActionFeedback(false, $"Build failed: {ex.Message}");
        }
    }

    private async Task HandleMoveTurretRequestAsync(ulong turretNetworkObjectId, Vector3 requestedPosition)
    {
        if (!TryGetIslandBuildManager(out IslandBuildManager buildManager, out string failureMessage))
        {
            SendIslandActionFeedback(false, failureMessage);
            return;
        }

        try
        {
            var result = await buildManager.TryServerMoveTurretAsync(this, turretNetworkObjectId, requestedPosition);
            SendIslandActionFeedback(result.success, result.message);
        }
        catch (Exception ex)
        {
            SendIslandActionFeedback(false, $"Move failed: {ex.Message}");
        }
    }

    private async Task HandleDeleteTurretRequestAsync(ulong turretNetworkObjectId)
    {
        if (!TryGetIslandBuildManager(out IslandBuildManager buildManager, out string failureMessage))
        {
            SendIslandActionFeedback(false, failureMessage);
            return;
        }

        try
        {
            var result = await buildManager.TryServerDeleteTurretAsync(this, turretNetworkObjectId);
            SendIslandActionFeedback(result.success, result.message);
        }
        catch (Exception ex)
        {
            SendIslandActionFeedback(false, $"Delete failed: {ex.Message}");
        }
    }

    private bool TryGetIslandBuildManager(out IslandBuildManager buildManager, out string failureMessage)
    {
        buildManager = IslandBuildManager.Instance;
        if (buildManager != null)
        {
            failureMessage = string.Empty;
            return true;
        }

        failureMessage = "Island build manager is not available.";
        return false;
    }

    private void SendIslandActionFeedback(bool success, string message)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? (success ? "Island action completed." : "Island action failed.")
            : message;

        if (IsOwner)
        {
            OnIslandActionFeedback?.Invoke(normalizedMessage, success);
        }
        else
        {
            PushIslandActionFeedbackClientRpc(normalizedMessage, success);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CombatTargetingUtility.Register(this);

        if (TryGetComponent<WorldNameplateUI>(out var ui))
        {
            ui.SetDisplayNameOverride(PlayerName.Value.ToString());
        }

        if (IsServer)
        {
            m_networkHasBoardedThisLife.Value = false;
            m_networkIsDead.Value = false;
            m_networkRespawnAtServerTime.Value = 0d;
            combatAggressorObjectIds.Clear();
            SyncCombatAggressorCount();
        }
        selectedCannonAmmoIndex = 0;
        ApplyInitialCannonAmmoSelection();

        // Initialize health on server
        if (IsServer)
        {
            m_networkHealth.Value = m_maxHealth;
        }

        // Register with PlayerManager
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.RegisterPlayer(this);
        }
        else
        {
            Debug.LogWarning("Player: PlayerManager.Instance is null. Make sure PlayerManager exists in the scene.");
        }

        if (IsServer)
        {
            FogOfWarNetworkVisibilityController.Register(this);
        }

        // Set static reference for local player
        if (IsOwner)
        {
            LocalPlayer = this;
            Debug.Log($"Player: Local player spawned - {gameObject.name}");
            LocalPlayerSpawned?.Invoke(transform);
        }

        OnRewardWalletChanged?.Invoke(Pearls, Gold, Experience);
    }

    public override void OnNetworkDespawn()
    {
        CombatTargetingUtility.Unregister(this);

        if (IsServer)
        {
            FogOfWarNetworkVisibilityController.Unregister(this);
        }

        if (repairCoroutine != null)
        {
            StopCoroutine(repairCoroutine);
            repairCoroutine = null;
        }

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        base.OnNetworkDespawn();

        // Unregister from PlayerManager
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.UnregisterPlayer(OwnerClientId);
        }

        // Clear static reference
        if (IsOwner)
        {
            LocalPlayer = null;
        }
    }

    #region Health System

    /// <summary>
    /// Server-authoritative damage entrypoint.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: Ignoring non-server TakeDamage call.");
            return;
        }

        ApplyDamage(damage);
    }

    /// <summary>
    /// Server-only: Actually apply damage to the player.
    /// </summary>
    private void ApplyDamage(int damage)
    {
        if (damage <= 0) return;
        if (!IsServer) return;
        if (m_networkHealth.Value <= 0) return;

        int newHealth = Mathf.Max(m_networkHealth.Value - damage, 0);
        m_networkHealth.Value = newHealth;

        if (newHealth <= 0)
        {
            HandleDeath();
        }
    }

    /// <summary>
    /// Server-only: Handle player death.
    /// </summary>
    private void HandleDeath()
    {
        if (!IsServer) return;
        if (m_networkIsDead.Value) return;

        if (repairCoroutine != null)
        {
            StopCoroutine(repairCoroutine);
            repairCoroutine = null;
        }

        StopAttack();
        combatAggressorObjectIds.Clear();
        SyncCombatAggressorCount();
        SetMovementEnabled(false);

        float respawnDelay = Mathf.Max(0.5f, respawnDelaySeconds);
        m_networkIsDead.Value = true;
        m_networkRespawnAtServerTime.Value = NetworkManager != null
            ? NetworkManager.ServerTime.Time + respawnDelay
            : respawnDelay;

        NotifyDeathClientRpc();

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
        }

        respawnCoroutine = StartCoroutine(RespawnAfterDelay(respawnDelay));
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    private void NotifyDeathClientRpc()
    {
        Debug.Log($"Player {gameObject.name} has died!");
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (!IsServer || !IsSpawned)
        {
            yield break;
        }

        RespawnServer();
        respawnCoroutine = null;
    }

    /// <summary>
    /// Called on all clients when death occurs (via OnValueChanged callback).
    /// </summary>
    private void OnDeath()
    {
        // Client-side death effects can be triggered here
        // e.g., play death animation, sound effects, etc.
    }

    private void OnRespawned()
    {
        // Client-side respawn effects can be triggered here.
    }

    private void RespawnServer()
    {
        if (!IsServer)
        {
            return;
        }

        if (TryGetSpawnTransform(out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            if (TryGetComponent(out NavMeshAgent navMeshAgent) && navMeshAgent.enabled)
            {
                navMeshAgent.ResetPath();
                navMeshAgent.Warp(spawnPosition);
            }
            else
            {
                transform.position = spawnPosition;
            }

            transform.rotation = spawnRotation;
        }

        m_networkIsDead.Value = false;
        m_networkRespawnAtServerTime.Value = 0d;
        m_networkHealth.Value = m_maxHealth;
        m_networkHasBoardedThisLife.Value = false;
        combatAggressorObjectIds.Clear();
        SyncCombatAggressorCount();

        SetMovementEnabled(true);
    }

    private void SetMovementEnabled(bool enabled)
    {
        if (!IsServer)
        {
            return;
        }

        if (TryGetComponent(out NavMeshAgent navMeshAgent) && navMeshAgent.enabled)
        {
            navMeshAgent.isStopped = !enabled;
            if (!enabled)
            {
                navMeshAgent.ResetPath();
                navMeshAgent.velocity = Vector3.zero;
            }
        }
    }

    private static bool TryGetSpawnTransform(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        return SpawnPointResolver.TryGetPlayerSpawnTransform(out spawnPosition, out spawnRotation);
    }

    #endregion

    #region Rewards

    public void GrantReward(NpcReward reward)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: GrantReward is server-only.");
            return;
        }

        AddToWallet(m_networkPearls, reward.Pearls);
        AddToWallet(m_networkGold, reward.Gold);
        AddToWallet(m_networkExperience, reward.Experience);
    }

    private static void AddToWallet(NetworkVariable<int> walletValue, int amountToAdd)
    {
        if (amountToAdd <= 0)
        {
            return;
        }

        long newValue = (long)walletValue.Value + amountToAdd;
        walletValue.Value = newValue >= int.MaxValue ? int.MaxValue : (int)newValue;
    }

    #endregion

    #region Repair System

    public void ToggleRepairing()
    {
        if (IsDead)
        {
            return;
        }

        if (repairCoroutine == null)
        {
            StartRepairing();
        }
        else
        {
            StopRepairing();
        }
    }

    public void StartRepairing()
    {
        if (IsDead)
        {
            return;
        }

        if (!IsServer) 
        {
            StartRepairingServerRpc();
            return;
        }

        if (combatAggressorObjectIds.Count > 0) return;

        if (repairCoroutine == null)
        {
            repairCoroutine = StartCoroutine(RepairWithInterval());
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StartRepairingServerRpc()
    {
        StartRepairing();
    }

    public void StopRepairing()
    {
        if (!IsServer)
        {
            StopRepairingServerRpc();
            return;
        }

        if (repairCoroutine != null)
        {
            StopCoroutine(repairCoroutine);
            repairCoroutine = null;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StopRepairingServerRpc()
    {
        StopRepairing();
    }

    private IEnumerator RepairWithInterval()
    {
        while (true)
        {
            if (m_networkHealth.Value < MaxHealth)
            {
                Repair(repairAmount);
            }
            yield return new WaitForSeconds(repairRate);
        }
    }

    private void Repair(int amount)
    {
        if (!IsServer) return;
        
        m_networkHealth.Value = Mathf.Min(m_networkHealth.Value + amount, MaxHealth);
    }

    #endregion

    #region Combat System

    public void StartAttack(GameObject target)
    {
        if (IsDead)
        {
            return;
        }

        if (TryGetComponent(out PlayerAttack attack))
        {
            attack.StartAttack(target);
        }
    }

    public void StopAttack()
    {
        if (TryGetComponent(out PlayerAttack attack))
        {
            attack.StopAttack();
        }
    }

    public void EnterCombat(GameObject aggressor)
    {
        if (!IsServer)
        {
            return;
        }

        EnterCombatLocal(aggressor);
    }

    public void ExitCombat(GameObject aggressor)
    {
        if (!IsServer)
        {
            return;
        }

        ExitCombatLocal(aggressor);
    }

    private void EnterCombatLocal(GameObject aggressor)
    {
        if (IsDead)
        {
            return;
        }

        if (aggressor == null)
        {
            return;
        }

        var aggressorNetObj = aggressor.GetComponent<NetworkObject>();
        if (aggressorNetObj == null)
        {
            return;
        }

        ulong aggressorId = aggressorNetObj.NetworkObjectId;
        if (!combatAggressorObjectIds.Contains(aggressorId))
        {
            combatAggressorObjectIds.Add(aggressorId);
            SyncCombatAggressorCount();
            StopRepairing(); // Can't repair in combat
        }
    }

    private void ExitCombatLocal(GameObject aggressor)
    {
        if (aggressor == null)
        {
            return;
        }

        var aggressorNetObj = aggressor.GetComponent<NetworkObject>();
        if (aggressorNetObj == null)
        {
            return;
        }

        ulong aggressorId = aggressorNetObj.NetworkObjectId;
        if (combatAggressorObjectIds.Contains(aggressorId))
        {
            combatAggressorObjectIds.Remove(aggressorId);
            SyncCombatAggressorCount();
        }
    }

    private void SyncCombatAggressorCount()
    {
        if (!IsServer)
        {
            return;
        }

        int aggressorCount = combatAggressorObjectIds.Count;
        if (m_networkCombatAggressorCount.Value != aggressorCount)
        {
            m_networkCombatAggressorCount.Value = aggressorCount;
        }
    }

    /// <summary>
    /// Check if this player is currently in combat.
    /// </summary>
    public bool IsInCombat => m_networkCombatAggressorCount.Value > 0;

    #endregion

    #region Boarding

    public void RequestBoardNpc(NPC targetNpc)
    {
        if (targetNpc == null)
        {
            Debug.LogWarning("Player: Select an NPC before boarding.");
            return;
        }

        if (IsDead)
        {
            Debug.Log("Player: Cannot board while dead.");
            return;
        }

        if (!IsOwner)
        {
            return;
        }

        if (m_networkHasBoardedThisLife.Value)
        {
            Debug.Log("Player: Boarding can only be used once per life.");
            return;
        }

        if (IsServer)
        {
            TryBoardNpcInternal(targetNpc);
            return;
        }

        NetworkObject npcNetworkObject = targetNpc.GetComponent<NetworkObject>();
        if (npcNetworkObject == null || !npcNetworkObject.IsSpawned)
        {
            Debug.LogWarning("Player: Target NPC is not network-spawned.");
            return;
        }

        BoardNpcServerRpc(npcNetworkObject.NetworkObjectId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void BoardNpcServerRpc(ulong npcNetworkObjectId)
    {
        if (NetworkManager == null || NetworkManager.SpawnManager == null)
        {
            return;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(npcNetworkObjectId, out NetworkObject npcNetworkObject))
        {
            return;
        }

        NPC npc = npcNetworkObject.GetComponent<NPC>();
        if (npc == null)
        {
            return;
        }

        TryBoardNpcInternal(npc);
    }

    private bool TryBoardNpcInternal(NPC targetNpc)
    {
        if (!IsServer)
        {
            return false;
        }

        if (IsDead)
        {
            return false;
        }

        if (targetNpc == null)
        {
            return false;
        }

        if (m_networkHasBoardedThisLife.Value)
        {
            Debug.Log("Player: Boarding can only be used once per life.");
            return false;
        }

        int maxHealth = Mathf.Max(targetNpc.MaxHealth, 1);
        int currentHealth = Mathf.Clamp(targetNpc.CurrentHealth, 0, maxHealth);
        float healthFraction = currentHealth / (float)maxHealth;

        if (currentHealth <= 0)
        {
            Debug.Log("Player: Can't board a dead target.");
            return false;
        }

        if (healthFraction > Mathf.Clamp01(boardingTargetHealthFraction))
        {
            Debug.Log("Player: Target HP must be low enough to board.");
            return false;
        }

        float maxDistance = Mathf.Max(0f, boardingDistance);
        if (maxDistance > 0f)
        {
            float sqrDistance = (targetNpc.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance > maxDistance * maxDistance)
            {
                Debug.Log("Player: Move closer to the target to board.");
                return false;
            }
        }

        if (!targetNpc.TryMarkBoarded())
        {
            Debug.Log("Player: This NPC has already been boarded.");
            return false;
        }

        m_networkHasBoardedThisLife.Value = true;

        if (currentHealth > 0)
        {
            targetNpc.TakeDamage(currentHealth, gameObject);
        }

        return true;
    }

    #endregion

    #region Cannon Ammo

    public IReadOnlyList<CannonAmmoDefinition> GetCannonAmmoOptions()
    {
        return gameplayConfig != null ? gameplayConfig.CannonAmmoTypes : Array.Empty<CannonAmmoDefinition>();
    }

    public int SelectedCannonAmmoIndex => selectedCannonAmmoIndex;

    public bool TrySelectCannonAmmo(int ammoIndex)
    {
        if (!IsServer && !IsOwner)
        {
            return false;
        }

        IReadOnlyList<CannonAmmoDefinition> options = GetCannonAmmoOptions();
        if (options == null || options.Count == 0)
        {
            Debug.LogWarning("Player: No cannon ammo types configured on gameplayConfig.");
            return false;
        }

        ammoIndex = Mathf.Clamp(ammoIndex, 0, options.Count - 1);
        CannonAmmoDefinition ammo = options[ammoIndex];
        if (ammo == null)
        {
            return false;
        }

        ApplyAmmoToCannon(ammo);
        selectedCannonAmmoIndex = ammoIndex;

        if (!IsServer)
        {
            SelectCannonAmmoServerRpc(ammoIndex);
        }

        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SelectCannonAmmoServerRpc(int ammoIndex)
    {
        IReadOnlyList<CannonAmmoDefinition> options = GetCannonAmmoOptions();
        if (options == null || options.Count == 0)
        {
            return;
        }

        ammoIndex = Mathf.Clamp(ammoIndex, 0, options.Count - 1);
        CannonAmmoDefinition ammo = options[ammoIndex];
        if (ammo == null)
        {
            return;
        }

        ApplyAmmoToCannon(ammo);
        selectedCannonAmmoIndex = ammoIndex;
    }

    private void ApplyAmmoToCannon(CannonAmmoDefinition ammo)
    {
        if (ammo == null)
        {
            return;
        }

        if (TryGetComponent(out Cannon cannon))
        {
            cannon.ApplyAmmoOverride(ammo.Damage, ammo.ProjectileMaterial);
        }
        else
        {
            Cannon childCannon = GetComponentInChildren<Cannon>();
            if (childCannon != null)
            {
                childCannon.ApplyAmmoOverride(ammo.Damage, ammo.ProjectileMaterial);
            }
        }

        // Keep server-side damage in sync
        if (TryGetComponent(out PlayerAttack attack))
        {
            attack.ApplyAmmoOverride(ammo.Damage);
        }
    }

    private void ApplyInitialCannonAmmoSelection()
    {
        IReadOnlyList<CannonAmmoDefinition> options = GetCannonAmmoOptions();
        if (options == null || options.Count == 0)
        {
            return;
        }

        int clampedIndex = Mathf.Clamp(selectedCannonAmmoIndex, 0, options.Count - 1);
        CannonAmmoDefinition selectedAmmo = options[clampedIndex];
        if (selectedAmmo == null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == null)
                {
                    continue;
                }

                clampedIndex = i;
                selectedAmmo = options[i];
                break;
            }
        }

        if (selectedAmmo == null)
        {
            return;
        }

        selectedCannonAmmoIndex = clampedIndex;
        ApplyAmmoToCannon(selectedAmmo);
    }

    #endregion

    private static string NormalizeOwnedCannonId(string cannonId)
    {
        return string.IsNullOrWhiteSpace(cannonId)
            ? string.Empty
            : cannonId.Trim().ToLowerInvariant();
    }

    private static string BuildOwnedCannonsCsv(IReadOnlyList<string> ownedCannonIds)
    {
        if (ownedCannonIds == null || ownedCannonIds.Count == 0)
        {
            return string.Empty;
        }

        var uniqueOwnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedOwnedIds = new List<string>(ownedCannonIds.Count);
        for (int index = 0; index < ownedCannonIds.Count; index++)
        {
            string normalizedId = NormalizeOwnedCannonId(ownedCannonIds[index]);
            if (string.IsNullOrWhiteSpace(normalizedId) || !uniqueOwnedIds.Add(normalizedId))
            {
                continue;
            }

            normalizedOwnedIds.Add(normalizedId);
        }

        normalizedOwnedIds.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", normalizedOwnedIds);
    }

    private static string[] ParseOwnedCannonsCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        string[] splitValues = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (splitValues.Length == 0)
        {
            return Array.Empty<string>();
        }

        var uniqueOwnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedOwnedIds = new List<string>(splitValues.Length);
        for (int index = 0; index < splitValues.Length; index++)
        {
            string normalizedId = NormalizeOwnedCannonId(splitValues[index]);
            if (string.IsNullOrWhiteSpace(normalizedId) || !uniqueOwnedIds.Add(normalizedId))
            {
                continue;
            }

            normalizedOwnedIds.Add(normalizedId);
        }

        return normalizedOwnedIds.Count == 0 ? Array.Empty<string>() : normalizedOwnedIds.ToArray();
    }

    private static bool ContainsOwnedCannonId(string csv, string cannonId)
    {
        string normalizedId = NormalizeOwnedCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        string[] ownedIds = ParseOwnedCannonsCsv(csv);
        for (int index = 0; index < ownedIds.Length; index++)
        {
            if (string.Equals(ownedIds[index], normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureWorldNameplate()
    {
        if (!TryGetComponent<WorldNameplateUI>(out _))
        {
            gameObject.AddComponent<WorldNameplateUI>();
        }
    }

    private void ApplyGameplayConfig()
    {
        if (gameplayConfig == null)
        {
            return;
        }

        m_maxHealth = gameplayConfig.MaxHealth;
        repairRate = gameplayConfig.RepairRate;
        repairAmount = gameplayConfig.RepairAmount;

        if (TryGetComponent(out NavMeshAgent navMeshAgent))
        {
            navMeshAgent.speed = gameplayConfig.NavMeshSpeed;
            navMeshAgent.acceleration = gameplayConfig.NavMeshAcceleration;
            navMeshAgent.angularSpeed = gameplayConfig.NavMeshAngularSpeed;
            navMeshAgent.stoppingDistance = gameplayConfig.NavMeshStoppingDistance;
        }

        if (TryGetComponent(out WorldNameplateUI worldNameplate))
        {
            worldNameplate.ApplySettings(
                gameplayConfig.ShowWorldNameplate,
                gameplayConfig.WorldNameplateMaxRenderDistance,
                gameplayConfig.HealthBarPlaceUnderTarget,
                gameplayConfig.HealthBarWorldOffset,
                gameplayConfig.HideHealthBarWhenEmpty);
        }

        if (TryGetComponent(out Cannon cannon))
        {
            cannon.ApplySettings(
                gameplayConfig.CannonballPrefab,
                gameplayConfig.CannonFireSpeed,
                gameplayConfig.CannonArcHeightFactor,
                gameplayConfig.CannonDamage,
                gameplayConfig.CannonMaxHitDistance,
                gameplayConfig.CannonShootingInterval);
        }

        if (TryGetComponent(out PlayerAttack playerAttack))
        {
            playerAttack.ApplySettings(
                gameplayConfig.CannonDamage,
                gameplayConfig.CannonMaxHitDistance,
                gameplayConfig.CannonShootingInterval);
        }
    }

}
