using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class Monster : NetworkBehaviour, ICombatEntity, IDamageSourceReceiver
{
    [Header("Identity")]
    [SerializeField] private string monsterName = "Monster";

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 500;

    [Header("Reward")]
    [SerializeField] private NpcReward reward;

    [Header("World Nameplate")]
    [SerializeField] private bool showWorldNameplate = true;
    [SerializeField, Min(0f)] private float worldNameplateMaxRenderDistance = 300f;
    [SerializeField] private bool healthBarPlaceUnderTarget;
    [SerializeField] private Vector3 healthBarWorldOffset = new(0f, 0f, -5.9f);
    [SerializeField] private bool hideHealthBarWhenEmpty = true;

    private readonly NetworkVariable<int> networkHealth = new(
        500,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private MonsterSpawner ownerSpawner;
    private int spawnSlotId = -1;
    private bool deathHandled;

    public event Action<float> OnHealthChanged = delegate { };

    public SeaEntityType EntityType => SeaEntityType.Monster;
    public GameObject EntityGameObject => gameObject;
    public string DisplayName => string.IsNullOrWhiteSpace(monsterName) ? "Monster" : monsterName.Trim();
    public GameObject TargetGameObject => gameObject;
    public bool CanBeTargeted => IsSpawned && isActiveAndEnabled && CurrentHealth > 0;
    public int MaxHealth => Mathf.Max(1, maxHealth);
    public int CurrentHealth => networkHealth.Value;
    public NpcReward Reward => reward;
    public int SpawnSlotId => spawnSlotId;

    public void BindSpawnSlot(MonsterSpawner spawner, int slotId)
    {
        ownerSpawner = spawner;
        spawnSlotId = slotId;
        collectedReward = false;
    }

    private bool collectedReward;

    private void Awake()
    {
        EnsureWorldNameplate();
        ApplyNameplateSettings();
        networkHealth.OnValueChanged += OnNetworkHealthChanged;
    }

    public override void OnDestroy()
    {
        CombatTargetingUtility.Unregister(this);
        networkHealth.OnValueChanged -= OnNetworkHealthChanged;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CombatTargetingUtility.Register(this);

        if (IsServer)
        {
            deathHandled = false;
            collectedReward = false;
            networkHealth.Value = MaxHealth;
        }
    }

    public override void OnNetworkDespawn()
    {
        CombatTargetingUtility.Unregister(this);
        base.OnNetworkDespawn();
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, null);
    }

    public void TakeDamage(int damage, GameObject damageSource)
    {
        if (damage <= 0 || !IsServer || networkHealth.Value <= 0)
        {
            return;
        }

        int resolvedDamage = CombatActionItemUtility.ApplyIncomingDamageModifiers(
            gameObject,
            damage,
            damageSource,
            out DamageNumberEffectStyle effectStyle);

        if (resolvedDamage <= 0)
        {
            return;
        }

        int newHealth = Mathf.Max(networkHealth.Value - resolvedDamage, 0);
        networkHealth.Value = newHealth;
        ShowDamageNumberClientRpc(resolvedDamage, (int)effectStyle);

        if (newHealth <= 0)
        {
            HandleDeath(damageSource);
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
    }

    public void ExitCombat(GameObject aggressor)
    {
    }

    private void HandleDeath(GameObject damageSource)
    {
        if (!IsServer || deathHandled)
        {
            return;
        }

        deathHandled = true;
        AwardKillReward(damageSource);
        ownerSpawner?.NotifyMonsterDeath(this);

        if (ownerSpawner == null && NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }

    private void AwardKillReward(GameObject damageSource)
    {
        if (collectedReward || damageSource == null || Reward.IsEmpty)
        {
            return;
        }

        if (!damageSource.TryGetComponent(out Player killer))
        {
            return;
        }

        collectedReward = true;
        killer.GrantReward(Reward);
    }

    private void OnNetworkHealthChanged(int previousValue, int newValue)
    {
        int resolvedMaxHealth = Mathf.Max(MaxHealth, 1);
        OnHealthChanged?.Invoke(Mathf.Clamp01(newValue / (float)resolvedMaxHealth));

        int delta = previousValue - newValue;
        if (delta < 0)
        {
            DamageNumberService.Show(transform.position, -delta, true);
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void ShowDamageNumberClientRpc(int amount, int effectStyle)
    {
        DamageNumberService.Show(
            transform.position,
            amount,
            false,
            CombatActionItemUtility.NormalizeDamageNumberEffectStyle(effectStyle));
    }

    private void EnsureWorldNameplate()
    {
        if (!TryGetComponent<WorldNameplateUI>(out _))
        {
            gameObject.AddComponent<WorldNameplateUI>();
        }
    }

    private void ApplyNameplateSettings()
    {
        if (!TryGetComponent(out WorldNameplateUI worldNameplate))
        {
            return;
        }

        worldNameplate.ApplySettings(
            showWorldNameplate,
            worldNameplateMaxRenderDistance,
            healthBarPlaceUnderTarget,
            healthBarWorldOffset,
            hideHealthBarWhenEmpty);
        worldNameplate.SetDisplayNameOverride(DisplayName);
    }
}
