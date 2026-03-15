using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SeaWars.Utility;

/// <summary>
/// NPC component that handles health, combat, and AI behavior.
/// Uses NetworkVariables for automatic state synchronization.
/// </summary>
public class NPC : NetworkBehaviour, IHealthSystem, ICombat, ITargetable
{
    [Header("Config")]
    [SerializeField] private PrefabGameplayConfig gameplayConfig;
    [SerializeField] private NpcDefinition npcDefinition;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private bool useDefinitionVisualPrefab = true;

    [Header("Health Settings")]
    [SerializeField] private int m_maxHealth = 100;
    private const int DefaultAttackDamage = 20;
    private const float DefaultAttackInterval = 2f;
    private const float DefaultAttackRange = 150f;
    private GameObject spawnedVisual;
    private const int UnsetDefinitionIndex = -1;
    private Coroutine resolveDefinitionCoroutine;

    // NetworkVariable for automatic health synchronization across all clients
    private NetworkVariable<int> m_networkHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<int> m_networkDefinitionIndex = new NetworkVariable<int>(
        UnsetDefinitionIndex,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Repair Settings")]
    [SerializeField] public float repairRate = 2.0f;
    [SerializeField] public int repairAmount = 5;

    private Coroutine repairCoroutine;
    private Cannon npcCannon;
    private PlayerAttack playerAttack;
    private NPCMovement movement;
    private int spawnSlotId = -1;
    private Vector3 homePosition;
    private Quaternion homeRotation = Quaternion.identity;
    private bool hasHomeAnchor;
    private bool deathHandled;

    private readonly List<ulong> aggressorNetworkObjectIds = new();
    private NetworkVariable<bool> m_networkHasBeenBoarded = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool HasBeenBoarded => m_networkHasBeenBoarded.Value;
    
    public event Action<float> OnHealthChanged = delegate { };

    // Public properties
    public int MaxHealth => m_maxHealth;
    public int CurrentHealth => m_networkHealth.Value;
    public NpcReward Reward => npcDefinition != null ? npcDefinition.Reward : default;
    public string DisplayName => ResolveDisplayName();
    public GameObject TargetGameObject => gameObject;
    public bool CanBeTargeted => IsSpawned && isActiveAndEnabled && CurrentHealth > 0;
    public int SpawnSlotId => spawnSlotId;
    public Vector3 HomePosition => hasHomeAnchor ? homePosition : transform.position;
    public Quaternion HomeRotation => hasHomeAnchor ? homeRotation : transform.rotation;

    public static event Action<Player, NPC, NpcReward> RewardGranted = delegate { };

    public void BindSpawnSlot(int slotId, Vector3 slotHomePosition, Quaternion slotHomeRotation)
    {
        spawnSlotId = slotId;
        homePosition = slotHomePosition;
        homeRotation = slotHomeRotation;
        hasHomeAnchor = true;
        movement?.SetHomeAnchor(slotHomePosition, slotHomeRotation);
    }

    public void SetDefinition(NpcDefinition definition)
    {
        int definitionIndex = NPCSpawner.Instance != null
            ? NPCSpawner.Instance.GetDefinitionIndex(definition)
            : UnsetDefinitionIndex;
        SetDefinitionFromServer(definitionIndex, definition);
    }

    public void SetDefinitionFromServer(int definitionIndex, NpcDefinition definition)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"NPC {name}: Ignoring non-server SetDefinitionFromServer call.");
            return;
        }

        int previousMaxHealth = Mathf.Max(m_maxHealth, 1);
        int previousHealth = Mathf.Clamp(m_networkHealth.Value, 0, previousMaxHealth);

        npcDefinition = definition;
        m_networkDefinitionIndex.Value = definitionIndex;
        ApplyGameplayConfig();

        if (IsSpawned)
        {
            if (previousHealth <= 0)
            {
                m_networkHealth.Value = MaxHealth;
                return;
            }

            float preservedHealthRatio = previousMaxHealth > 0
                ? previousHealth / (float)previousMaxHealth
                : 1f;
            m_networkHealth.Value = Mathf.Clamp(
                Mathf.RoundToInt(MaxHealth * preservedHealthRatio),
                1,
                MaxHealth);
        }
    }

    public bool TryMarkBoarded()
    {
        if (m_networkHasBeenBoarded.Value)
        {
            return false;
        }

        if (!IsServer)
        {
            return false;
        }

        m_networkHasBeenBoarded.Value = true;
        return true;
    }

    private void Awake()
    {
        npcCannon = GetComponent<Cannon>();
        playerAttack = GetComponent<PlayerAttack>();
        movement = GetComponent<NPCMovement>();
        if (movement != null)
        {
            movement.SetHomeAnchor(transform.position, transform.rotation);
            movement.LeashExceeded += OnLeashExceeded;
        }
        EnsureWorldNameplate();
        ApplyGameplayConfig();
        
        // Subscribe to network health changes
        m_networkHealth.OnValueChanged += OnNetworkHealthChanged;
    }

    public override void OnDestroy()
    {
        CombatTargetingUtility.Unregister(this);

        m_networkHealth.OnValueChanged -= OnNetworkHealthChanged;
        m_networkDefinitionIndex.OnValueChanged -= OnNetworkDefinitionIndexChanged;
        if (movement != null)
        {
            movement.LeashExceeded -= OnLeashExceeded;
        }

        if (spawnedVisual != null)
        {
            Destroy(spawnedVisual);
            spawnedVisual = null;
        }

        if (resolveDefinitionCoroutine != null)
        {
            StopCoroutine(resolveDefinitionCoroutine);
            resolveDefinitionCoroutine = null;
        }

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
            OnDeath();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CombatTargetingUtility.Register(this);
        m_networkDefinitionIndex.OnValueChanged += OnNetworkDefinitionIndexChanged;

        // Clients resolve visual/combat definition from synchronized index.
        ApplyDefinitionFromNetworkIndex(m_networkDefinitionIndex.Value);

        // Initialize health on server
        if (IsServer)
        {
            m_networkHealth.Value = m_maxHealth;
            m_networkHasBeenBoarded.Value = false;
            deathHandled = false;
            if (hasHomeAnchor)
            {
                movement?.SetHomeAnchor(homePosition, homeRotation);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        CombatTargetingUtility.Unregister(this);
        m_networkDefinitionIndex.OnValueChanged -= OnNetworkDefinitionIndexChanged;
        base.OnNetworkDespawn();
    }

    private void OnNetworkDefinitionIndexChanged(int previousIndex, int currentIndex)
    {
        ApplyDefinitionFromNetworkIndex(currentIndex);
    }

    private void ApplyDefinitionFromNetworkIndex(int definitionIndex)
    {
        if (IsServer || definitionIndex < 0)
        {
            return;
        }

        NpcDefinition resolvedDefinition = NPCSpawner.Instance != null
            ? NPCSpawner.Instance.ResolveDefinitionByIndex(definitionIndex)
            : null;

        if (resolvedDefinition == null)
        {
            if (resolveDefinitionCoroutine == null)
            {
                resolveDefinitionCoroutine = StartCoroutine(ResolveDefinitionWhenSpawnerReady(definitionIndex));
            }

            return;
        }

        if (resolveDefinitionCoroutine != null)
        {
            StopCoroutine(resolveDefinitionCoroutine);
            resolveDefinitionCoroutine = null;
        }

        if (npcDefinition == resolvedDefinition)
        {
            return;
        }

        npcDefinition = resolvedDefinition;
        ApplyGameplayConfig();
    }

    private IEnumerator ResolveDefinitionWhenSpawnerReady(int definitionIndex)
    {
        while (NPCSpawner.Instance == null)
        {
            yield return null;
        }

        resolveDefinitionCoroutine = null;
        ApplyDefinitionFromNetworkIndex(definitionIndex);
    }

    #region Combat System

    public void EnterCombat(GameObject player)
    {
        if (!IsServer)
        {
            return;
        }

        EnterCombatLocal(player);
    }

    public void ExitCombat(GameObject player)
    {
        if (!IsServer)
        {
            return;
        }

        ExitCombatLocal(player);
    }

    private void EnterCombatLocal(GameObject player)
    {
        if (player == null)
        {
            Debug.LogWarning($"[Combat][NPC:{name}] EnterCombat ignored because aggressor was null.");
            return;
        }

        var aggressorNetObj = player.GetComponent<NetworkObject>();
        if (aggressorNetObj == null)
        {
            Debug.LogWarning($"[Combat][NPC:{name}] EnterCombat ignored because aggressor '{player.name}' has no NetworkObject.");
            return;
        }

        ulong aggressorNetworkObjectId = aggressorNetObj.NetworkObjectId;
        if (!aggressorNetworkObjectIds.Contains(aggressorNetworkObjectId))
        {
            aggressorNetworkObjectIds.Add(aggressorNetworkObjectId);
            Debug.Log($"[Combat][NPC:{name}] Registered aggressor '{player.name}' (netId: {aggressorNetworkObjectId}).");
            Retaliate();
            movement?.StopRoaming();
        }
    }

    private void ExitCombatLocal(GameObject player)
    {
        if (player == null)
        {
            return;
        }

        var playerNetObj = player.GetComponent<NetworkObject>();
        if (playerNetObj == null)
        {
            return;
        }

        ulong aggressorNetworkObjectId = playerNetObj.NetworkObjectId;
        if (aggressorNetworkObjectIds.Contains(aggressorNetworkObjectId))
        {
            aggressorNetworkObjectIds.Remove(aggressorNetworkObjectId);
            playerAttack?.StopAttack();

            if (aggressorNetworkObjectIds.Count == 0)
            {
                movement?.StartRoaming();
            }
        }
    }

    #endregion

    #region Health System

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
            Debug.LogWarning($"NPC {gameObject.name}: Ignoring non-server TakeDamage call.");
            return;
        }

        ApplyDamage(damage, damageSource);
    }

    private void ApplyDamage(int damage, GameObject damageSource)
    {
        if (damage <= 0) return;
        if (!IsServer) return;
        if (m_networkHealth.Value <= 0) return;

        if (damageSource != null && CombatTargetingUtility.TryGetTargetable(damageSource, out ITargetable damageSourceTargetable))
        {
            string damageSourceName = damageSourceTargetable.TargetGameObject != null
                ? damageSourceTargetable.TargetGameObject.name
                : damageSource.name;
            Debug.Log($"[Combat][NPC:{name}] Took {damage} damage from aggressor '{damageSourceName}'.");
            EnterCombatLocal(damageSource);
        }

        int newHealth = Mathf.Max(m_networkHealth.Value - damage, 0);
        m_networkHealth.Value = newHealth;

        if (newHealth <= 0)
        {
            HandleDeath(damageSource);
        }
    }

    private void Retaliate()
    {
        if (!IsServer) return;

        if (NetworkManager == null || NetworkManager.SpawnManager == null || aggressorNetworkObjectIds.Count == 0)
        {
            Debug.LogWarning($"[Combat][NPC:{name}] Retaliate aborted because no aggressors or spawn manager were available.");
            return;
        }

        for (int index = 0; index < aggressorNetworkObjectIds.Count; index++)
        {
            ulong aggressorNetworkObjectId = aggressorNetworkObjectIds[index];
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(aggressorNetworkObjectId, out NetworkObject aggressorNetworkObject))
            {
                Debug.LogWarning($"[Combat][NPC:{name}] Could not resolve aggressor netId {aggressorNetworkObjectId} for retaliation.");
                continue;
            }

            if (!CombatTargetingUtility.TryGetTargetable(aggressorNetworkObject.gameObject, out ITargetable targetable) ||
                !targetable.CanBeTargeted ||
                targetable.TargetGameObject == null)
            {
                Debug.LogWarning($"[Combat][NPC:{name}] Aggressor '{aggressorNetworkObject.name}' is not a live targetable combatant.");
                continue;
            }

            Debug.Log($"[Combat][NPC:{name}] Retaliating against '{targetable.TargetGameObject.name}' (netId: {aggressorNetworkObjectId}).");
            playerAttack?.StartAttack(targetable.TargetGameObject);
            return;
        }

        Debug.LogWarning($"[Combat][NPC:{name}] Retaliate found no valid live combat targets.");
    }

    private void HandleDeath(GameObject damageSource)
    {
        if (!IsServer) return;
        if (deathHandled) return;

        deathHandled = true;

        if (repairCoroutine != null)
        {
            StopCoroutine(repairCoroutine);
            repairCoroutine = null;
        }

        movement?.StopRoaming();
        ClearCombatState();

        AwardKillReward(damageSource);
        NotifyDeathClientRpc();

        if (NPCSpawner.Instance != null && spawnSlotId >= 0)
        {
            NPCSpawner.Instance.NotifyNpcDeath(this);
        }
        else
        {
            StartCoroutine(DespawnAfterDelay(0.1f));
        }
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    private void NotifyDeathClientRpc()
    {
        Debug.Log($"NPC {gameObject.name} has died!");
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }

    private void OnDeath()
    {
        // Client-side death effects
    }

    private void OnLeashExceeded()
    {
        if (!IsServer || deathHandled || CurrentHealth <= 0)
        {
            return;
        }

        if (repairCoroutine != null)
        {
            StopCoroutine(repairCoroutine);
            repairCoroutine = null;
        }

        ClearCombatState();
        m_networkHasBeenBoarded.Value = false;
        m_networkHealth.Value = MaxHealth;
        movement?.ReturnHome();
    }

    private void AwardKillReward(GameObject damageSource)
    {
        if (!IsServer || damageSource == null)
        {
            return;
        }

        if (!damageSource.TryGetComponent(out Player killer))
        {
            return;
        }

        NpcReward reward = Reward;
        if (reward.IsEmpty)
        {
            return;
        }

        killer.GrantReward(reward);
        RewardGranted?.Invoke(killer, this, reward);
    }

    #endregion

    #region Repair System

    public void StartRepairing()
    {
        if (!IsServer)
        {
            StartRepairingServerRpc();
            return;
        }

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

    private void EnsureWorldNameplate()
    {
        if (!TryGetComponent<WorldNameplateUI>(out _))
        {
            gameObject.AddComponent<WorldNameplateUI>();
        }
    }

    private string ResolveDisplayName()
    {
        if (npcDefinition != null && !string.IsNullOrWhiteSpace(npcDefinition.NpcName))
        {
            return npcDefinition.NpcName.Trim();
        }

        string rawName = gameObject.name;
        const string cloneSuffix = "(Clone)";
        if (rawName.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(rawName) ? "Unknown Target" : rawName;
    }

    private void ApplyDefinitionVisual()
    {
        if (spawnedVisual != null)
        {
            Destroy(spawnedVisual);
            spawnedVisual = null;
        }

        if (npcDefinition == null || npcDefinition.VisualPrefab == null)
        {
            return;
        }

        if (!useDefinitionVisualPrefab)
        {
            return;
        }

        GameObject visualPrefab = npcDefinition.VisualPrefab;
        if (visualPrefab.TryGetComponent(out NetworkObject _))
        {
            Debug.LogWarning($"NPC {name}: NpcDefinition visual prefab '{visualPrefab.name}' has a NetworkObject. Use a visual-only prefab/model.");
            return;
        }

        Transform targetRoot = visualRoot != null ? visualRoot : transform;
        spawnedVisual = Instantiate(visualPrefab, targetRoot, false);
        spawnedVisual.name = $"{visualPrefab.name}_Visual";
        ShadowCastingUtility.DisableShadowCastingInChildren(spawnedVisual.transform);
    }

    private void ApplyGameplayConfig()
    {
        int resolvedMaxHealth = m_maxHealth;
        int resolvedAttackDamage = DefaultAttackDamage;
        float resolvedAttackInterval = DefaultAttackInterval;
        float resolvedAttackRange = DefaultAttackRange;

        if (gameplayConfig != null)
        {
            resolvedMaxHealth = gameplayConfig.MaxHealth;
            resolvedAttackDamage = gameplayConfig.CannonDamage;
            resolvedAttackInterval = gameplayConfig.CannonShootingInterval;
            resolvedAttackRange = gameplayConfig.CannonMaxHitDistance;

            repairRate = gameplayConfig.RepairRate;
            repairAmount = gameplayConfig.RepairAmount;

            movement?.ApplyMovementSettings(gameplayConfig.NavMeshSpeed);

            if (movement != null)
            {
                movement.ApplyRoamingSettings(gameplayConfig.NpcRoamRadius, gameplayConfig.NpcRoamWaitTime);
                movement.ApplyLeashSettings(gameplayConfig.NpcLeashRadius, gameplayConfig.NpcHomeArrivalDistance);
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

            if (npcCannon != null)
            {
                npcCannon.ApplySettings(
                    gameplayConfig.CannonballPrefab,
                    gameplayConfig.CannonFireSpeed,
                    gameplayConfig.CannonArcHeightFactor,
                    resolvedAttackDamage,
                    resolvedAttackRange,
                    resolvedAttackInterval);
            }
        }

        if (npcDefinition != null)
        {
            resolvedMaxHealth = npcDefinition.Health;
            resolvedAttackDamage = npcDefinition.Damage;
            resolvedAttackInterval = npcDefinition.AttackIntervalSeconds;
        }

        m_maxHealth = resolvedMaxHealth;

        if (playerAttack != null)
        {
            playerAttack.ApplySettings(
                resolvedAttackDamage,
                resolvedAttackRange,
                resolvedAttackInterval);
        }

        if (npcCannon != null && gameplayConfig != null && npcDefinition != null)
        {
            npcCannon.ApplySettings(
                gameplayConfig.CannonballPrefab,
                gameplayConfig.CannonFireSpeed,
                gameplayConfig.CannonArcHeightFactor,
                resolvedAttackDamage,
                resolvedAttackRange,
                resolvedAttackInterval);
        }

        if (TryGetComponent(out WorldNameplateUI worldNameplateUi))
        {
            worldNameplateUi.SetDisplayNameOverride(DisplayName);
        }

        ApplyDefinitionVisual();
    }

    private void ClearCombatState()
    {
        playerAttack?.StopAttack();

        if (NetworkManager != null && NetworkManager.SpawnManager != null)
        {
            for (int i = aggressorNetworkObjectIds.Count - 1; i >= 0; i--)
            {
                ulong aggressorNetworkObjectId = aggressorNetworkObjectIds[i];
                if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(aggressorNetworkObjectId, out NetworkObject aggressorNetworkObject))
                {
                    continue;
                }

                if (!aggressorNetworkObject.TryGetComponent(out Player combatant))
                {
                    continue;
                }

                combatant.StopAttack();
                combatant.ExitCombat(gameObject);
            }
        }

        aggressorNetworkObjectIds.Clear();
    }

}
