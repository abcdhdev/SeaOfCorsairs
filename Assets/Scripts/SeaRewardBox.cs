using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class SeaRewardBox : NetworkBehaviour, ISeaEntity
{
    [SerializeField] private string boxName = "Box";
    [SerializeField] private NpcReward reward;

    private SeaRewardBoxSpawner ownerSpawner;
    private int spawnSlotId = -1;
    private bool collected;

    public SeaEntityType EntityType => SeaEntityType.Box;
    public GameObject EntityGameObject => gameObject;
    public string DisplayName => string.IsNullOrWhiteSpace(boxName) ? "Box" : boxName.Trim();
    public int SpawnSlotId => spawnSlotId;

    public void BindSpawnSlot(SeaRewardBoxSpawner spawner, int slotId)
    {
        ownerSpawner = spawner;
        spawnSlotId = slotId;
        collected = false;
    }

    private void Awake()
    {
        if (TryGetComponent(out Collider collider))
        {
            collider.isTrigger = true;
        }

        if (TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || collected || other == null)
        {
            return;
        }

        Player player = other.GetComponentInParent<Player>();
        if (player == null || !player.IsSpawned || player.IsDead)
        {
            return;
        }

        Collect(player);
    }

    private void Collect(Player collector)
    {
        if (collector == null || collected)
        {
            return;
        }

        collected = true;
        if (!reward.IsEmpty)
        {
            collector.GrantReward(reward);
        }

        ownerSpawner?.NotifyBoxCollected(this);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }
}
