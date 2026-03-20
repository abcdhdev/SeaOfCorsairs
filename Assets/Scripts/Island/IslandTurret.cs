using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Cannon))]
[RequireComponent(typeof(PlayerAttack))]
public sealed class IslandTurret : NetworkBehaviour, IHealthSystem, ICombat, ITargetable
{
    private const string CannonballResourcePath = "Island/Cannonball";

    private static readonly HashSet<IslandTurret> ActiveTurretsInternal = new();

    [Header("Turret")]
    [SerializeField, Min(1)] private int maxHealth = 1000;
    [SerializeField, Min(0f)] private float placementY = 15f;
    [SerializeField, Min(0.01f)] private float fireSpeed = 100f;
    [SerializeField, Min(0f)] private float arcHeightFactor = 0.2f;
    [SerializeField, Min(0)] private int attackDamage = 20;
    [SerializeField, Min(0f)] private float attackRange = 175f;
    [SerializeField, Min(0.05f)] private float attackInterval = 2f;
    [SerializeField] private GameObject cannonballPrefab;

    private readonly NetworkVariable<int> networkHealth = new(
        1000,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString128Bytes> ownerEntityId = new(
        new FixedString128Bytes(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly List<ulong> aggressorIds = new();

    private Cannon cachedCannon;
    private PlayerAttack cachedAttack;
    private ulong retaliatingAgainstId;
    private Vector3 lastNotifiedPosition;
    private string persistentWorldObjectId = string.Empty;

    public static event Action RegistryChanged = delegate { };
    public event Action<float> OnHealthChanged = delegate { };

    public static IReadOnlyCollection<IslandTurret> ActiveTurrets => ActiveTurretsInternal;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => networkHealth.Value;
    public string OwnerEntityId => ownerEntityId.Value.ToString();
    public string PersistentWorldObjectId => persistentWorldObjectId;
    public bool HasPersistentWorldObjectId => !string.IsNullOrWhiteSpace(persistentWorldObjectId);
    public GameObject TargetGameObject => gameObject;
    public bool CanBeTargeted => IsSpawned && isActiveAndEnabled && CurrentHealth > 0;

    private Cannon Cannon
    {
        get
        {
            if (cachedCannon == null)
            {
                cachedCannon = GetComponent<Cannon>();
            }

            return cachedCannon;
        }
    }

    private PlayerAttack Attack
    {
        get
        {
            if (cachedAttack == null)
            {
                cachedAttack = GetComponent<PlayerAttack>();
            }

            return cachedAttack;
        }
    }

    private void Awake()
    {
        networkHealth.OnValueChanged += OnNetworkHealthChanged;
        ResolveCannonballPrefab();
        ConfigureWeapon();
    }

    public override void OnDestroy()
    {
        networkHealth.OnValueChanged -= OnNetworkHealthChanged;
        UnregisterFromRegistry();
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            networkHealth.Value = maxHealth;
            SnapToPlacementHeight();
        }

        ConfigureWeapon();
        lastNotifiedPosition = transform.position;
        CombatTargetingUtility.Register(this);
        RegisterWithRegistry();
    }

    public override void OnNetworkDespawn()
    {
        CombatTargetingUtility.Unregister(this);
        UnregisterFromRegistry();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if ((transform.position - lastNotifiedPosition).sqrMagnitude < 0.01f)
        {
            return;
        }

        lastNotifiedPosition = transform.position;
        IslandBuildManager.Instance?.MarkTerrainDirty();
    }

    public void InitializeOwnership(string ownerEntityIdValue, string worldObjectId)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"IslandTurret {name}: InitializeOwnership is server-only.");
            return;
        }

        string normalizedOwnerEntityId = string.IsNullOrWhiteSpace(ownerEntityIdValue)
            ? string.Empty
            : ownerEntityIdValue.Trim();
        ownerEntityId.Value = new FixedString128Bytes(normalizedOwnerEntityId);
        persistentWorldObjectId = string.IsNullOrWhiteSpace(worldObjectId)
            ? string.Empty
            : worldObjectId.Trim();
    }

    public bool IsOwnedByOwnerEntity(string ownerEntityIdValue)
    {
        return !string.IsNullOrWhiteSpace(OwnerEntityId) &&
               string.Equals(OwnerEntityId, ownerEntityIdValue?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetPlacementPosition(Vector3 position)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"IslandTurret {name}: SetPlacementPosition is server-only.");
            return;
        }

        transform.position = new Vector3(position.x, placementY, position.z);
        lastNotifiedPosition = transform.position;
        IslandBuildManager.Instance?.MarkTerrainDirty();
    }

    public PersistentTurretState CapturePersistentState()
    {
        return PersistentTurretState.FromPosition(transform.position);
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        if (!IsServer)
        {
            Debug.LogWarning($"IslandTurret {name}: Ignoring non-server TakeDamage call.");
            return;
        }

        if (networkHealth.Value <= 0)
        {
            return;
        }

        networkHealth.Value = Mathf.Max(0, networkHealth.Value - damage);
        if (networkHealth.Value <= 0)
        {
            HandleDestroyed();
        }
    }

    public void StartRepairing()
    {
    }

    public void StopRepairing()
    {
    }

    public void EnterCombat(GameObject aggressor)
    {
        if (!IsServer || aggressor == null || CurrentHealth <= 0)
        {
            return;
        }

        if (!TryGetAggressorId(aggressor, out ulong aggressorId))
        {
            return;
        }

        if (!aggressorIds.Contains(aggressorId))
        {
            aggressorIds.Add(aggressorId);
        }

        if (retaliatingAgainstId == aggressorId)
        {
            return;
        }

        retaliatingAgainstId = aggressorId;
        Attack?.StartAttack(aggressor);
    }

    public void ExitCombat(GameObject aggressor)
    {
        if (!IsServer || aggressor == null)
        {
            return;
        }

        if (!TryGetAggressorId(aggressor, out ulong aggressorId))
        {
            return;
        }

        aggressorIds.Remove(aggressorId);
        if (retaliatingAgainstId != aggressorId)
        {
            return;
        }

        retaliatingAgainstId = 0;
        Attack?.StopAttack();

        for (int index = aggressorIds.Count - 1; index >= 0; index--)
        {
            if (!TryResolveAggressor(aggressorIds[index], out GameObject nextAggressor))
            {
                aggressorIds.RemoveAt(index);
                continue;
            }

            retaliatingAgainstId = aggressorIds[index];
            Attack?.StartAttack(nextAggressor);
            return;
        }
    }

    public static int CountOwnedByOwnerEntity(string ownerEntityIdValue)
    {
        int count = 0;
        foreach (IslandTurret turret in ActiveTurretsInternal)
        {
            if (turret != null && turret.IsSpawned && turret.IsOwnedByOwnerEntity(ownerEntityIdValue))
            {
                count++;
            }
        }

        return count;
    }

    public static bool TryResolveOwnedTurret(ulong networkObjectId, string ownerEntityIdValue, out IslandTurret turret)
    {
        turret = null;
        foreach (IslandTurret candidate in ActiveTurretsInternal)
        {
            if (candidate == null || !candidate.IsSpawned || candidate.NetworkObject == null)
            {
                continue;
            }

            if (candidate.NetworkObject.NetworkObjectId != networkObjectId)
            {
                continue;
            }

            if (!candidate.IsOwnedByOwnerEntity(ownerEntityIdValue))
            {
                return false;
            }

            turret = candidate;
            return true;
        }

        return false;
    }

    private void ResolveCannonballPrefab()
    {
        if (cannonballPrefab == null)
        {
            cannonballPrefab = Resources.Load<GameObject>(CannonballResourcePath);
        }
    }

    private void ConfigureWeapon()
    {
        ResolveCannonballPrefab();
        Cannon?.ApplySettings(
            cannonballPrefab,
            fireSpeed,
            arcHeightFactor,
            attackDamage,
            attackRange,
            attackInterval);
        Attack?.ApplySettings(attackDamage, attackRange, attackInterval);
    }

    private void OnNetworkHealthChanged(int previousValue, int newValue)
    {
        int resolvedMaxHealth = Mathf.Max(maxHealth, 1);
        OnHealthChanged?.Invoke(Mathf.Clamp01(newValue / (float)resolvedMaxHealth));

        int delta = previousValue - newValue;
        if (delta > 0)
        {
            DamageNumberService.Show(transform.position, delta, false);
        }
        else if (delta < 0)
        {
            DamageNumberService.Show(transform.position, -delta, true);
        }
    }

    private void HandleDestroyed()
    {
        aggressorIds.Clear();
        retaliatingAgainstId = 0;
        Attack?.StopAttack();
        IslandBuildManager.Instance?.NotifyTurretDestroyed(this);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void SnapToPlacementHeight()
    {
        Vector3 position = transform.position;
        position.y = placementY;
        transform.position = position;
    }

    private bool TryGetAggressorId(GameObject aggressor, out ulong aggressorId)
    {
        aggressorId = 0;
        NetworkObject aggressorNetworkObject = aggressor.GetComponent<NetworkObject>();
        if (aggressorNetworkObject == null || !aggressorNetworkObject.IsSpawned)
        {
            return false;
        }

        aggressorId = aggressorNetworkObject.NetworkObjectId;
        return true;
    }

    private bool TryResolveAggressor(ulong aggressorId, out GameObject aggressor)
    {
        aggressor = null;
        if (NetworkManager == null || NetworkManager.SpawnManager == null)
        {
            return false;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(aggressorId, out NetworkObject aggressorNetworkObject))
        {
            return false;
        }

        aggressor = aggressorNetworkObject.gameObject;
        return aggressor != null;
    }

    private void RegisterWithRegistry()
    {
        if (ActiveTurretsInternal.Add(this))
        {
            RegistryChanged();
            IslandBuildManager.Instance?.MarkTerrainDirty();
        }
    }

    private void UnregisterFromRegistry()
    {
        if (ActiveTurretsInternal.Remove(this))
        {
            RegistryChanged();
            IslandBuildManager.Instance?.MarkTerrainDirty();
        }
    }
}
