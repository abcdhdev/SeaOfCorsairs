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
public partial class Player : NetworkBehaviour, ICombatEntity, IDamageSourceReceiver
{
    /// <summary>
    /// Static event fired when the LOCAL player spawns.
    /// Subscribe from Awake() - static events survive instance creation.
    /// </summary>
    public static event Action<Player> LocalPlayerSpawned;

    /// <summary>
    /// Reference to the current local player, or null if not spawned yet.
    /// </summary>
    public static Player LocalPlayer { get; private set; }

    [Header("Config")]
    [SerializeField] private PlayerGameplayConfig gameplayConfig;

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
    private NetworkVariable<Unity.Collections.FixedString64Bytes> m_playerName = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        "Player",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<FixedString32Bytes> m_networkGuildAbbreviation = new NetworkVariable<FixedString32Bytes>(
        new FixedString32Bytes(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<FixedString128Bytes> m_ownerEntityId = new NetworkVariable<FixedString128Bytes>(
        new FixedString128Bytes(),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_networkDiamonds = new NetworkVariable<int>(
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
    private NetworkVariable<FixedString512Bytes> m_ownedShipIdsCsv = new NetworkVariable<FixedString512Bytes>(
        new FixedString512Bytes(MarketShipCatalogRuntime.DefaultShipId),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<FixedString64Bytes> m_selectedShipId = new NetworkVariable<FixedString64Bytes>(
        new FixedString64Bytes(MarketShipCatalogRuntime.DefaultShipId),
        NetworkVariableReadPermission.Everyone,
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
    private NetworkVariable<int> m_networkHarpoonAmmoIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_networkActionItem = new NetworkVariable<int>(
        (int)PlayerActionItemType.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<float> OnHealthChanged = delegate { };
    public event Action<int, int, int> OnRewardWalletChanged = delegate { };
    public event Action<int, int, int> OnRewardGranted = delegate { };
    public event Action OnOwnedCannonsChanged = delegate { };
    public event Action OnOwnedShipsChanged = delegate { };
    public event Action<string, bool, string> OnCannonPurchaseResult = delegate { };
    public event Action<string, bool, string> OnShipPurchaseResult = delegate { };
    public event Action<string> OnSelectedShipChanged = delegate { };
    public event Action<PlayerActionItemType> OnActiveActionItemsChanged = delegate { };
    public event Action<string, bool> OnIslandActionFeedback = delegate { };
    public event Action<bool> OnDeathStateChanged = delegate { };

    // Public properties
    public int MaxHealth => m_maxHealth;
    public int CurrentHealth => m_networkHealth.Value;
    public bool IsDead => m_networkIsDead.Value || CurrentHealth <= 0;
    public bool IsDeadNetworkState => m_networkIsDead.Value;
    public int Diamonds => m_networkDiamonds.Value;
    public int Gold => m_networkGold.Value;
    public int Experience => m_networkExperience.Value;
    public string OwnerEntityId => m_ownerEntityId.Value.ToString();
    public string GuildAbbreviation => m_networkGuildAbbreviation.Value.ToString();
    public string OwnedCannonIdsCsv => m_ownedCannonIdsCsv.Value.ToString();
    public string OwnedShipIdsCsv => m_ownedShipIdsCsv.Value.ToString();
    public string SelectedShipId => NormalizeOwnedShipId(m_selectedShipId.Value.ToString());
    public PlayerActionItemType ActiveActionItems => NormalizeActionItemMask(m_networkActionItem.Value);
    public SeaEntityType EntityType => SeaEntityType.Player;
    public GameObject EntityGameObject => gameObject;
    public string DisplayName
    {
        get
        {
            string resolvedName = m_playerName.Value.ToString();
            return string.IsNullOrWhiteSpace(resolvedName)
                ? ResolveObjectDisplayName(gameObject, "Player")
                : resolvedName.Trim();
        }
    }
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
        m_playerName.OnValueChanged += OnPlayerNameChanged;
        m_networkGuildAbbreviation.OnValueChanged += OnGuildAbbreviationChanged;
        m_networkDiamonds.OnValueChanged += OnRewardWalletValueChanged;
        m_networkGold.OnValueChanged += OnRewardWalletValueChanged;
        m_networkExperience.OnValueChanged += OnRewardWalletValueChanged;
        m_ownedCannonIdsCsv.OnValueChanged += OnOwnedCannonIdsChanged;
        m_inventorySnapshot.OnValueChanged += OnInventorySnapshotChanged;
        m_shipCannonLoadoutsSnapshot.OnValueChanged += OnShipCannonLoadoutsSnapshotChanged;
        m_ownedShipIdsCsv.OnValueChanged += OnOwnedShipIdsChanged;
        m_selectedShipId.OnValueChanged += OnSelectedShipIdChanged;
        m_networkActionItem.OnValueChanged += OnActiveActionItemsChangedInternal;
        InitializeWorldMapSubscriptions();

        OnPlayerNameChanged(default, m_playerName.Value);
        OnGuildAbbreviationChanged(default, m_networkGuildAbbreviation.Value);
    }

    public override void OnDestroy()
    {
        CombatTargetingUtility.Unregister(this);

        m_networkHealth.OnValueChanged -= OnNetworkHealthChanged;
        m_playerName.OnValueChanged -= OnPlayerNameChanged;
        m_networkGuildAbbreviation.OnValueChanged -= OnGuildAbbreviationChanged;
        m_networkDiamonds.OnValueChanged -= OnRewardWalletValueChanged;
        m_networkGold.OnValueChanged -= OnRewardWalletValueChanged;
        m_networkExperience.OnValueChanged -= OnRewardWalletValueChanged;
        m_ownedCannonIdsCsv.OnValueChanged -= OnOwnedCannonIdsChanged;
        m_inventorySnapshot.OnValueChanged -= OnInventorySnapshotChanged;
        m_shipCannonLoadoutsSnapshot.OnValueChanged -= OnShipCannonLoadoutsSnapshotChanged;
        m_ownedShipIdsCsv.OnValueChanged -= OnOwnedShipIdsChanged;
        m_selectedShipId.OnValueChanged -= OnSelectedShipIdChanged;
        m_networkActionItem.OnValueChanged -= OnActiveActionItemsChangedInternal;
        DisposeWorldMapSubscriptions();
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
        if (delta < 0)
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

    public void ApplyPlayerName(string playerName)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPlayerName is server-only.");
            return;
        }

        string normalizedPlayerName = string.IsNullOrWhiteSpace(playerName)
            ? string.Empty
            : playerName.Trim();
        m_playerName.Value = new FixedString64Bytes(normalizedPlayerName);
    }

    private void OnGuildAbbreviationChanged(FixedString32Bytes previousValue, FixedString32Bytes newValue)
    {
        if (TryGetComponent<WorldNameplateUI>(out var ui))
        {
            ui.SetGuildPrefixOverride(newValue.ToString());
        }
    }

    private void OnRewardWalletValueChanged(int previousValue, int newValue)
    {
        OnRewardWalletChanged?.Invoke(Diamonds, Gold, Experience);
    }

    private void OnOwnedCannonIdsChanged(FixedString512Bytes previousValue, FixedString512Bytes newValue)
    {
        OnOwnedCannonsChanged?.Invoke();
    }

    private void OnOwnedShipIdsChanged(FixedString512Bytes previousValue, FixedString512Bytes newValue)
    {
        OnOwnedShipsChanged?.Invoke();
    }

    private void OnSelectedShipIdChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        RefreshCannonCombatRuntimeSettings();
        OnSelectedShipChanged?.Invoke(NormalizeOwnedShipId(newValue.ToString()));
    }

    private void OnActiveActionItemsChangedInternal(int previousValue, int newValue)
    {
        OnActiveActionItemsChanged?.Invoke(NormalizeActionItemMask(newValue));
    }

    public void ApplyPersistedWallet(int gold, int diamond, int? experience = null)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedWallet is server-only.");
            return;
        }

        m_networkGold.Value = Mathf.Max(0, gold);
        m_networkDiamonds.Value = Mathf.Max(0, diamond);
        if (experience.HasValue)
        {
            m_networkExperience.Value = Mathf.Max(0, experience.Value);
        }
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

        var inventoryItems = new List<PlayerInventoryItemState>(ownedCannonIds != null ? ownedCannonIds.Count : 0);
        if (ownedCannonIds != null)
        {
            for (int index = 0; index < ownedCannonIds.Count; index++)
            {
                string normalizedCannonId = NormalizeOwnedCannonId(ownedCannonIds[index]);
                if (string.IsNullOrWhiteSpace(normalizedCannonId))
                {
                    continue;
                }

                inventoryItems.Add(new PlayerInventoryItemState(normalizedCannonId, 1));
            }
        }

        ApplyPersistedInventory(inventoryItems);
    }

    public void ApplyPersistedOwnedShips(IReadOnlyList<string> ownedShipIds)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedOwnedShips is server-only.");
            return;
        }

        m_ownedShipIdsCsv.Value = new FixedString512Bytes(BuildOwnedShipsCsv(ownedShipIds));
        EnsureSelectedShipIsOwned();
        NormalizeAndApplyLoadoutsServer(
            PlayerInventoryState.ParseShipCannonLoadoutsSnapshot(ShipCannonLoadoutsSnapshot),
            PlayerInventoryState.ParseInventorySnapshot(InventorySnapshot));
    }

    public void ApplyPersistedSelectedShip(string shipId)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedSelectedShip is server-only.");
            return;
        }

        string normalizedShipId = NormalizeOwnedShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId) || !OwnsShip(normalizedShipId))
        {
            EnsureSelectedShipIsOwned();
            return;
        }

        if (string.Equals(SelectedShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        m_selectedShipId.Value = new FixedString64Bytes(normalizedShipId);
    }

    public void ApplyPersistedActiveActionItems(PlayerActionItemType actionItems)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedActiveActionItems is server-only.");
            return;
        }

        m_networkActionItem.Value = (int)NormalizeActionItemMask((int)actionItems);
        DisableUnavailableActionItemsServer();
    }

    public void ApplyGuildAbbreviation(string guildAbbreviation)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyGuildAbbreviation is server-only.");
            return;
        }

        m_networkGuildAbbreviation.Value = new FixedString32Bytes(NormalizeGuildAbbreviation(guildAbbreviation));
    }

    public void RequestGuildAbbreviationRefresh()
    {
        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        RequestGuildAbbreviationRefreshServerRpc();
    }

    public bool OwnsCannon(string cannonId)
    {
        return GetInventoryAmount(cannonId) > 0;
    }

    public string[] GetOwnedCannonIds()
    {
        IReadOnlyList<PlayerInventoryItemState> inventoryItems = GetInventoryItems();
        var ownedCannonIds = new List<string>(inventoryItems.Count);
        for (int index = 0; index < inventoryItems.Count; index++)
        {
            PlayerInventoryItemState item = inventoryItems[index];
            if (item.Amount <= 0 || !PlayerInventoryState.IsCannon(item.ItemId))
            {
                continue;
            }

            ownedCannonIds.Add(item.ItemId);
        }

        return ownedCannonIds.Count == 0 ? Array.Empty<string>() : ownedCannonIds.ToArray();
    }

    public bool OwnsShip(string shipId)
    {
        return ContainsOwnedShipId(m_ownedShipIdsCsv.Value.ToString(), shipId);
    }

    public string[] GetOwnedShipIds()
    {
        return ParseOwnedShipsCsv(m_ownedShipIdsCsv.Value.ToString());
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

    public bool TryToggleActionItem(PlayerActionItemType actionItem)
    {
        PlayerActionItemType normalizedActionItem = NormalizeActionItemType((int)actionItem);
        if (normalizedActionItem == PlayerActionItemType.None || !IsOwner)
        {
            return false;
        }

        if (!HasActionItem(normalizedActionItem) && GetActionItemAmount(normalizedActionItem) <= 0)
        {
            return false;
        }

        if (IsServer)
        {
            SetActionItemEnabled(normalizedActionItem, !HasActionItem(normalizedActionItem));
        }
        else
        {
            ToggleActionItemServerRpc((int)normalizedActionItem);
        }

        return true;
    }

    public bool HasActionItem(PlayerActionItemType actionItem)
    {
        PlayerActionItemType normalizedActionItem = NormalizeActionItemType((int)actionItem);
        if (normalizedActionItem == PlayerActionItemType.None)
        {
            return false;
        }

        return (ActiveActionItems & normalizedActionItem) == normalizedActionItem &&
               GetActionItemAmount(normalizedActionItem) > 0;
    }

    public bool RequestShipPurchase(string shipId)
    {
        if (!IsOwner)
        {
            return false;
        }

        string normalizedShipId = NormalizeOwnedShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId))
        {
            return false;
        }

        RequestShipPurchaseServerRpc(normalizedShipId);
        return true;
    }

    public bool RequestShipSelection(string shipId)
    {
        if (!IsOwner)
        {
            return false;
        }

        string normalizedShipId = NormalizeOwnedShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId))
        {
            return false;
        }

        RequestShipSelectionServerRpc(normalizedShipId);
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

    public void NotifyShipPurchaseResult(string shipId, bool success, string message)
    {
        if (!IsServer)
        {
            return;
        }

        PushShipPurchaseResultClientRpc(
            NormalizeOwnedShipId(shipId),
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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ToggleActionItemServerRpc(int actionItemValue)
    {
        PlayerActionItemType normalizedActionItem = NormalizeActionItemType(actionItemValue);
        if (normalizedActionItem == PlayerActionItemType.None)
        {
            return;
        }

        SetActionItemEnabled(normalizedActionItem, !HasActionItem(normalizedActionItem));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestShipPurchaseServerRpc(string shipId)
    {
        if (MultiplayerController.Instance == null)
        {
            NotifyShipPurchaseResult(shipId, false, "The multiplayer controller is not ready.");
            return;
        }

        MultiplayerController.Instance.RequestShipPurchase(this, NormalizeOwnedShipId(shipId));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestShipSelectionServerRpc(string shipId)
    {
        SetSelectedShipServer(shipId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestGuildAbbreviationRefreshServerRpc()
    {
        if (MultiplayerController.Instance == null)
        {
            return;
        }

        MultiplayerController.Instance.RequestGuildAbbreviationRefresh(this);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushCannonPurchaseResultClientRpc(string cannonId, bool success, string message)
    {
        OnCannonPurchaseResult?.Invoke(
            NormalizeOwnedCannonId(cannonId),
            success,
            string.IsNullOrWhiteSpace(message) ? (success ? "Purchase completed." : "Purchase failed.") : message);
    }

    private void SetActionItemEnabled(PlayerActionItemType actionItem, bool isEnabled)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: SetActionItemEnabled is server-only.");
            return;
        }

        PlayerActionItemType normalizedActionItem = NormalizeActionItemType((int)actionItem);
        PlayerActionItemType currentMask = ActiveActionItems;

        if (normalizedActionItem == PlayerActionItemType.None)
        {
            m_networkActionItem.Value = (int)PlayerActionItemType.None;
            return;
        }

        if (isEnabled && GetActionItemAmount(normalizedActionItem) <= 0)
        {
            return;
        }

        PlayerActionItemType updatedMask = isEnabled
            ? currentMask | normalizedActionItem
            : currentMask & ~normalizedActionItem;

        m_networkActionItem.Value = (int)NormalizeActionItemMask((int)updatedMask);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushShipPurchaseResultClientRpc(string shipId, bool success, string message)
    {
        OnShipPurchaseResult?.Invoke(
            NormalizeOwnedShipId(shipId),
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
            ui.SetDisplayNameOverride(m_playerName.Value.ToString());
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
        m_networkHarpoonAmmoIndex.OnValueChanged += OnHarpoonAmmoIndexChanged;
        ApplyInitialCannonAmmoSelection();
        ApplyInitialHarpoonSelection();

        // Initialize health on server
        if (IsServer)
        {
            m_networkHealth.Value = m_maxHealth;
            InitializeWorldMapOnNetworkSpawn();
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

        bool isOwner = IsOwner;
        if (isOwner)
        {
            LocalPlayer = this;
        }

        // Notify listeners once the local player reference is in place.
        if (isOwner)
        {
            Debug.Log($"Player: Local player spawned - {gameObject.name}");
            LocalPlayerSpawned?.Invoke(this);
        }

        OnRewardWalletChanged?.Invoke(Diamonds, Gold, Experience);
    }

    public override void OnNetworkDespawn()
    {
        HandleWorldMapNetworkDespawn();
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

        m_networkHarpoonAmmoIndex.OnValueChanged -= OnHarpoonAmmoIndexChanged;

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
        TakeDamage(damage, null);
    }

    public void TakeDamage(int damage, GameObject damageSource)
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

        ApplyDamage(damage, damageSource);
    }

    /// <summary>
    /// Server-only: Actually apply damage to the player.
    /// </summary>
    private void ApplyDamage(int damage, GameObject damageSource)
    {
        if (damage <= 0) return;
        if (!IsServer) return;
        if (m_networkHealth.Value <= 0) return;

        int resolvedDamage = CombatActionItemUtility.ApplyIncomingDamageModifiers(
            gameObject,
            damage,
            damageSource,
            out DamageNumberEffectStyle effectStyle);

        if (resolvedDamage <= 0)
        {
            return;
        }

        int newHealth = Mathf.Max(m_networkHealth.Value - resolvedDamage, 0);
        m_networkHealth.Value = newHealth;
        ShowDamageNumberClientRpc(resolvedDamage, (int)effectStyle);

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

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void ShowDamageNumberClientRpc(int amount, int effectStyle)
    {
        DamageNumberService.Show(
            transform.position,
            amount,
            false,
            NormalizeDamageNumberEffectStyle(effectStyle));
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
        OnDeathStateChanged?.Invoke(true);
        // Client-side death effects can be triggered here
        // e.g., play death animation, sound effects, etc.
    }

    private void OnRespawned()
    {
        OnDeathStateChanged?.Invoke(false);
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

    private bool TryGetSpawnTransform(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        return TryGetWorldMapSpawnTransform(out spawnPosition, out spawnRotation);
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

        AddToWallet(m_networkDiamonds, reward.Diamonds);
        AddToWallet(m_networkGold, reward.Gold);
        AddToWallet(m_networkExperience, reward.Experience);
        PushRewardGrantedClientRpc(reward.Diamonds, reward.Gold, reward.Experience);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushRewardGrantedClientRpc(int diamonds, int gold, int experience)
    {
        OnRewardGranted?.Invoke(
            Mathf.Max(0, diamonds),
            Mathf.Max(0, gold),
            Mathf.Max(0, experience));
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

    public IReadOnlyList<HarpoonAmmoDefinition> GetHarpoonAmmoOptions()
    {
        return gameplayConfig != null ? gameplayConfig.HarpoonAmmoTypes : Array.Empty<HarpoonAmmoDefinition>();
    }

    public int SelectedHarpoonAmmoIndex => m_networkHarpoonAmmoIndex.Value;

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

        // Ammo selection only swaps projectile visuals here; damage is resolved on the server.
        if (TryGetComponent(out WeaponFireController weaponFireController))
        {
            weaponFireController.ApplyProjectilePrefabOverride(ammo.ProjectilePrefab);
        }
        else
        {
            WeaponFireController childWeaponFireController = GetComponentInChildren<WeaponFireController>();
            if (childWeaponFireController != null)
            {
                childWeaponFireController.ApplyProjectilePrefabOverride(ammo.ProjectilePrefab);
            }
        }
    }

    private void ApplyInitialCannonAmmoSelection()
    {
        if (!TryResolveSelectedCannonAmmo(out int clampedIndex, out CannonAmmoDefinition selectedAmmo))
        {
            return;
        }

        selectedCannonAmmoIndex = clampedIndex;
        ApplyAmmoToCannon(selectedAmmo);
    }

    public bool TrySelectHarpoonAmmo(int ammoIndex)
    {
        if (!IsServer && !IsOwner)
        {
            return false;
        }

        if (!ApplyHarpoonSelection(ammoIndex))
        {
            Debug.LogWarning("Player: No valid harpoon ammo types configured on gameplayConfig.");
            return false;
        }

        if (!IsServer)
        {
            SelectHarpoonAmmoServerRpc(ammoIndex);
        }

        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SelectHarpoonAmmoServerRpc(int ammoIndex)
    {
        ApplyHarpoonSelection(ammoIndex);
    }

    private void ApplySelectedHarpoon(HarpoonAmmoDefinition ammo)
    {
        if (ammo == null)
        {
            return;
        }

        if (TryGetComponent(out PlayerAttack attack))
        {
            attack.ApplyHarpoonOverride(ammo.Damage);
        }

        if (TryGetComponent(out WeaponFireController weaponFireController))
        {
            weaponFireController.ApplyHarpoonVisualOverride(ammo.ProjectileColor);
        }
        else
        {
            WeaponFireController childWeaponFireController = GetComponentInChildren<WeaponFireController>();
            if (childWeaponFireController != null)
            {
                childWeaponFireController.ApplyHarpoonVisualOverride(ammo.ProjectileColor);
            }
        }
    }

    private void ApplyInitialHarpoonSelection()
    {
        ApplyHarpoonSelection(m_networkHarpoonAmmoIndex.Value);
    }

    private void OnHarpoonAmmoIndexChanged(int previousValue, int newValue)
    {
        ApplyHarpoonSelection(newValue);
    }

    private bool ApplyHarpoonSelection(int ammoIndex)
    {
        if (!TryResolveHarpoonAmmo(ammoIndex, out int resolvedIndex, out HarpoonAmmoDefinition selectedAmmo))
        {
            return false;
        }

        ApplySelectedHarpoon(selectedAmmo);

        if (IsServer && m_networkHarpoonAmmoIndex.Value != resolvedIndex)
        {
            m_networkHarpoonAmmoIndex.Value = resolvedIndex;
        }

        return true;
    }

    private bool TryResolveHarpoonAmmo(int ammoIndex, out int resolvedIndex, out HarpoonAmmoDefinition ammo)
    {
        resolvedIndex = -1;
        ammo = null;

        IReadOnlyList<HarpoonAmmoDefinition> options = GetHarpoonAmmoOptions();
        if (options == null || options.Count == 0)
        {
            return false;
        }

        int clampedIndex = Mathf.Clamp(ammoIndex, 0, options.Count - 1);
        ammo = options[clampedIndex];
        if (ammo == null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == null)
                {
                    continue;
                }

                clampedIndex = i;
                ammo = options[i];
                break;
            }
        }

        if (ammo == null)
        {
            return false;
        }

        resolvedIndex = clampedIndex;
        return true;
    }

    #endregion

    private static string NormalizeOwnedCannonId(string cannonId)
    {
        return string.IsNullOrWhiteSpace(cannonId)
            ? string.Empty
            : cannonId.Trim().ToLowerInvariant();
    }

    private static PlayerActionItemType NormalizeActionItemType(int actionItemValue)
    {
        return actionItemValue switch
        {
            (int)PlayerActionItemType.BlackGunpowder => PlayerActionItemType.BlackGunpowder,
            (int)PlayerActionItemType.AgwesArmorPlating => PlayerActionItemType.AgwesArmorPlating,
            _ => PlayerActionItemType.None
        };
    }

    private static PlayerActionItemType NormalizeActionItemMask(int actionItemMask)
    {
        int allowedMask = (int)(PlayerActionItemType.BlackGunpowder | PlayerActionItemType.AgwesArmorPlating);
        return (PlayerActionItemType)(actionItemMask & allowedMask);
    }

    private static DamageNumberEffectStyle NormalizeDamageNumberEffectStyle(int effectStyleValue)
    {
        return CombatActionItemUtility.NormalizeDamageNumberEffectStyle(effectStyleValue);
    }

    private static string NormalizeOwnedShipId(string shipId)
    {
        return MarketShipCatalogRuntime.NormalizeShipId(shipId);
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

    private static string BuildOwnedShipsCsv(IReadOnlyList<string> ownedShipIds)
    {
        var uniqueOwnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedOwnedIds = new List<string>(ownedShipIds != null ? ownedShipIds.Count + 1 : 1);

        string defaultShipId = NormalizeOwnedShipId(MarketShipCatalogRuntime.DefaultShipId);
        if (!string.IsNullOrWhiteSpace(defaultShipId) && uniqueOwnedIds.Add(defaultShipId))
        {
            normalizedOwnedIds.Add(defaultShipId);
        }

        if (ownedShipIds != null)
        {
            for (int index = 0; index < ownedShipIds.Count; index++)
            {
                string normalizedId = NormalizeOwnedShipId(ownedShipIds[index]);
                if (string.IsNullOrWhiteSpace(normalizedId) ||
                    !MarketShipCatalogRuntime.TryGetShip(normalizedId, out _) ||
                    !uniqueOwnedIds.Add(normalizedId))
                {
                    continue;
                }

                normalizedOwnedIds.Add(normalizedId);
            }
        }

        normalizedOwnedIds.Sort(CompareOwnedShipIds);
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

    private static string[] ParseOwnedShipsCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new[] { NormalizeOwnedShipId(MarketShipCatalogRuntime.DefaultShipId) };
        }

        string[] splitValues = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        var uniqueOwnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedOwnedIds = new List<string>(splitValues.Length + 1);

        string defaultShipId = NormalizeOwnedShipId(MarketShipCatalogRuntime.DefaultShipId);
        if (!string.IsNullOrWhiteSpace(defaultShipId) && uniqueOwnedIds.Add(defaultShipId))
        {
            normalizedOwnedIds.Add(defaultShipId);
        }

        for (int index = 0; index < splitValues.Length; index++)
        {
            string normalizedId = NormalizeOwnedShipId(splitValues[index]);
            if (string.IsNullOrWhiteSpace(normalizedId) ||
                !MarketShipCatalogRuntime.TryGetShip(normalizedId, out _) ||
                !uniqueOwnedIds.Add(normalizedId))
            {
                continue;
            }

            normalizedOwnedIds.Add(normalizedId);
        }

        normalizedOwnedIds.Sort(CompareOwnedShipIds);
        return normalizedOwnedIds.ToArray();
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

    private static bool ContainsOwnedShipId(string csv, string shipId)
    {
        string normalizedId = NormalizeOwnedShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        string[] ownedIds = ParseOwnedShipsCsv(csv);
        for (int index = 0; index < ownedIds.Length; index++)
        {
            if (string.Equals(ownedIds[index], normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareOwnedShipIds(string left, string right)
    {
        bool hasLeft = MarketShipCatalogRuntime.TryGetShip(left, out MarketShipData leftShip) && leftShip != null;
        bool hasRight = MarketShipCatalogRuntime.TryGetShip(right, out MarketShipData rightShip) && rightShip != null;

        if (hasLeft && hasRight)
        {
            int sortOrderComparison = leftShip.SortOrder.CompareTo(rightShip.SortOrder);
            if (sortOrderComparison != 0)
            {
                return sortOrderComparison;
            }
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureSelectedShipIsOwned()
    {
        if (!IsServer)
        {
            return;
        }

        string[] ownedShipIds = ParseOwnedShipsCsv(m_ownedShipIdsCsv.Value.ToString());
        if (ownedShipIds.Length == 0)
        {
            ownedShipIds = new[] { NormalizeOwnedShipId(MarketShipCatalogRuntime.DefaultShipId) };
            m_ownedShipIdsCsv.Value = new FixedString512Bytes(BuildOwnedShipsCsv(ownedShipIds));
        }

        string normalizedSelectedShipId = NormalizeOwnedShipId(m_selectedShipId.Value.ToString());
        if (string.IsNullOrWhiteSpace(normalizedSelectedShipId) ||
            !ContainsOwnedShipId(m_ownedShipIdsCsv.Value.ToString(), normalizedSelectedShipId))
        {
            m_selectedShipId.Value = new FixedString64Bytes(ownedShipIds[0]);
        }
    }

    private void SetSelectedShipServer(string shipId)
    {
        if (!IsServer)
        {
            return;
        }

        string normalizedShipId = NormalizeOwnedShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId) || !OwnsShip(normalizedShipId))
        {
            return;
        }

        if (string.Equals(SelectedShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        m_selectedShipId.Value = new FixedString64Bytes(normalizedShipId);
    }

    private static string NormalizeGuildAbbreviation(string guildAbbreviation)
    {
        if (string.IsNullOrWhiteSpace(guildAbbreviation))
        {
            return string.Empty;
        }

        string normalized = UiTextSanitizer.SanitizeForLabel(guildAbbreviation.Trim().ToUpperInvariant(), collapseWhitespace: true);
        if (normalized.Length != 3)
        {
            return string.Empty;
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            if (!char.IsLetter(normalized[index]))
            {
                return string.Empty;
            }
        }

        return normalized;
    }

    private static string ResolveObjectDisplayName(GameObject targetObject, string fallbackName)
    {
        if (targetObject == null)
        {
            return fallbackName;
        }

        string rawName = targetObject.name;
        const string cloneSuffix = "(Clone)";
        if (rawName.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(rawName) ? fallbackName : rawName;
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

        RefreshCannonCombatRuntimeSettings();
    }

    private void RefreshCannonCombatRuntimeSettings()
    {
        if (gameplayConfig == null)
        {
            return;
        }

        float resolvedRange = ResolveCurrentShipCannonMaxRange();
        float resolvedInterval = ResolveCurrentShipCannonSalvoInterval();
        float resolvedProjectileSpeed = ResolveCurrentShipCannonProjectileSpeed();

        if (TryGetComponent(out WeaponFireController weaponFireController))
        {
            weaponFireController.ApplySettings(
                null,
                resolvedProjectileSpeed,
                gameplayConfig.CannonArcHeightFactor);
        }

        if (TryGetComponent(out PlayerAttack playerAttack))
        {
            playerAttack.ApplySettings(0, resolvedRange, resolvedInterval);
        }
    }

}
