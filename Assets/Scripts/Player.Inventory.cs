using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public partial class Player
{
    private const float DefaultPlayerCannonProjectileSpeed = 20f;

    private struct CurrentShipCannonCombatProfile
    {
        public int EquippedCannonCount;
        public float AverageReloadTimeSeconds;
        public float AverageRange;
        public float AverageHitProbability;
        public float AverageCriticalHitProbability;
        public float AverageCriticalHitDamage;
        public float AverageBonusDamageFlat;
        public float AverageBonusDamagePercentage;

        public bool HasCannons => EquippedCannonCount > 0;
    }

    private NetworkVariable<FixedString4096Bytes> m_inventorySnapshot = new NetworkVariable<FixedString4096Bytes>(
        new FixedString4096Bytes(),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<FixedString4096Bytes> m_shipCannonLoadoutsSnapshot = new NetworkVariable<FixedString4096Bytes>(
        new FixedString4096Bytes(),
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server
    );

    public event Action OnInventoryChanged = delegate { };
    public event Action OnShipCannonLoadoutsChanged = delegate { };
    public event Action<string, bool, string> OnInventoryItemPurchaseResult = delegate { };

    public string InventorySnapshot => m_inventorySnapshot.Value.ToString();
    public string ShipCannonLoadoutsSnapshot => m_shipCannonLoadoutsSnapshot.Value.ToString();

    public IReadOnlyList<PlayerInventoryItemState> GetInventoryItems()
    {
        return PlayerInventoryState.ParseInventorySnapshot(InventorySnapshot);
    }

    public IReadOnlyList<ShipCannonLoadoutState> GetShipCannonLoadouts()
    {
        return PlayerInventoryState.ParseShipCannonLoadoutsSnapshot(ShipCannonLoadoutsSnapshot);
    }

    public int GetInventoryAmount(string itemId)
    {
        string normalizedItemId = PlayerInventoryState.NormalizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalizedItemId))
        {
            return 0;
        }

        IReadOnlyList<PlayerInventoryItemState> inventoryItems = GetInventoryItems();
        for (int index = 0; index < inventoryItems.Count; index++)
        {
            if (string.Equals(inventoryItems[index].ItemId, normalizedItemId, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Max(0, inventoryItems[index].Amount);
            }
        }

        return 0;
    }

    public int GetActionItemAmount(PlayerActionItemType actionItemType)
    {
        return GetInventoryAmount(PlayerInventoryState.GetActionItemInventoryId(actionItemType));
    }

    public int GetSelectedCannonAmmoAmount()
    {
        return GetInventoryAmount(GetSelectedCannonAmmoId());
    }

    public int GetSelectedHarpoonAmmoAmount()
    {
        return GetInventoryAmount(GetSelectedHarpoonAmmoId());
    }

    public int GetShipCannonCapacity(string shipId)
    {
        string normalizedShipId = NormalizeOwnedShipId(shipId);
        if (!MarketShipCatalogRuntime.TryGetShip(normalizedShipId, out MarketShipData ship) || ship == null)
        {
            return 0;
        }

        return ship.CannonCapacity;
    }

    public int GetCurrentShipCannonCapacity()
    {
        return GetShipCannonCapacity(SelectedShipId);
    }

    public int GetCurrentShipEquippedCannonTotal()
    {
        return GetShipEquippedCannonTotal(SelectedShipId);
    }

    public int GetShipEquippedCannonTotal(string shipId)
    {
        string normalizedShipId = NormalizeOwnedShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId))
        {
            return 0;
        }

        IReadOnlyList<ShipCannonLoadoutState> loadouts = GetShipCannonLoadouts();
        for (int shipIndex = 0; shipIndex < loadouts.Count; shipIndex++)
        {
            ShipCannonLoadoutState loadout = loadouts[shipIndex];
            if (!string.Equals(loadout.ShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int total = 0;
            IReadOnlyList<PlayerInventoryItemState> cannonStacks = loadout.CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
            for (int entryIndex = 0; entryIndex < cannonStacks.Count; entryIndex++)
            {
                total += Mathf.Max(0, cannonStacks[entryIndex].Amount);
            }

            return total;
        }

        return 0;
    }

    public int GetCurrentShipEquippedCannonAmount(string cannonId)
    {
        return GetShipEquippedCannonAmount(SelectedShipId, cannonId);
    }

    public int GetShipEquippedCannonAmount(string shipId, string cannonId)
    {
        string normalizedShipId = NormalizeOwnedShipId(shipId);
        string normalizedCannonId = NormalizeOwnedCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedShipId) || string.IsNullOrWhiteSpace(normalizedCannonId))
        {
            return 0;
        }

        IReadOnlyList<ShipCannonLoadoutState> loadouts = GetShipCannonLoadouts();
        for (int shipIndex = 0; shipIndex < loadouts.Count; shipIndex++)
        {
            ShipCannonLoadoutState loadout = loadouts[shipIndex];
            if (!string.Equals(loadout.ShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<PlayerInventoryItemState> cannonStacks = loadout.CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
            for (int entryIndex = 0; entryIndex < cannonStacks.Count; entryIndex++)
            {
                PlayerInventoryItemState stack = cannonStacks[entryIndex];
                if (string.Equals(stack.ItemId, normalizedCannonId, StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Max(0, stack.Amount);
                }
            }

            return 0;
        }

        return 0;
    }

    public int GetAvailableWarehouseCannonAmount(string cannonId)
    {
        string normalizedCannonId = NormalizeOwnedCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedCannonId))
        {
            return 0;
        }

        int totalOwned = GetInventoryAmount(normalizedCannonId);
        int totalEquipped = GetTotalEquippedCannonAmount(normalizedCannonId);
        return Mathf.Max(0, totalOwned - totalEquipped);
    }

    public int GetActionItemBadgeAmount()
    {
        int blackGunpowderAmount = GetActionItemAmount(PlayerActionItemType.BlackGunpowder);
        int agwesArmorAmount = GetActionItemAmount(PlayerActionItemType.AgwesArmorPlating);
        return Mathf.Max(0, blackGunpowderAmount) + Mathf.Max(0, agwesArmorAmount);
    }

    public void ApplyPersistedInventory(IReadOnlyList<PlayerInventoryItemState> inventoryItems)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedInventory is server-only.");
            return;
        }

        List<PlayerInventoryItemState> normalizedInventory = PlayerInventoryState.NormalizeInventory(inventoryItems);
        SetInventorySnapshotServer(normalizedInventory);
        NormalizeAndApplyLoadoutsServer(PlayerInventoryState.ParseShipCannonLoadoutsSnapshot(ShipCannonLoadoutsSnapshot), normalizedInventory);
        DisableUnavailableActionItemsServer();
    }

    public void ApplyPersistedShipCannonLoadouts(IReadOnlyList<ShipCannonLoadoutState> loadouts)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"Player {gameObject.name}: ApplyPersistedShipCannonLoadouts is server-only.");
            return;
        }

        List<PlayerInventoryItemState> normalizedInventory = PlayerInventoryState.ParseInventorySnapshot(InventorySnapshot);
        NormalizeAndApplyLoadoutsServer(loadouts, normalizedInventory);
    }

    public bool RequestInventoryItemPurchase(string itemId)
    {
        if (!IsOwner)
        {
            return false;
        }

        string normalizedItemId = PlayerInventoryState.NormalizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalizedItemId) || PlayerInventoryState.GetItemKind(normalizedItemId) == PlayerInventoryItemKind.Unknown)
        {
            return false;
        }

        RequestInventoryItemPurchaseServerRpc(normalizedItemId);
        return true;
    }

    public bool RequestEquipCannonToSelectedShip(string cannonId)
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

        RequestEquipCannonToSelectedShipServerRpc(normalizedCannonId);
        return true;
    }

    public bool RequestUnequipCannonFromSelectedShip(string cannonId)
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

        RequestUnequipCannonFromSelectedShipServerRpc(normalizedCannonId);
        return true;
    }

    public void NotifyInventoryItemPurchaseResult(string itemId, bool success, string message)
    {
        if (!IsServer)
        {
            return;
        }

        PushInventoryItemPurchaseResultClientRpc(
            PlayerInventoryState.NormalizeItemId(itemId),
            success,
            message ?? string.Empty);
    }

    private void OnInventorySnapshotChanged(FixedString4096Bytes previousValue, FixedString4096Bytes newValue)
    {
        SyncOwnedCannonIdsFromInventory(PlayerInventoryState.ParseInventorySnapshot(newValue.ToString()));
        OnInventoryChanged?.Invoke();
    }

    private void OnShipCannonLoadoutsSnapshotChanged(FixedString4096Bytes previousValue, FixedString4096Bytes newValue)
    {
        RefreshCannonCombatRuntimeSettings();
        OnShipCannonLoadoutsChanged?.Invoke();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestInventoryItemPurchaseServerRpc(string itemId)
    {
        if (MultiplayerController.Instance == null)
        {
            NotifyInventoryItemPurchaseResult(itemId, false, "The multiplayer controller is not ready.");
            return;
        }

        MultiplayerController.Instance.RequestInventoryItemPurchase(this, PlayerInventoryState.NormalizeItemId(itemId));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestEquipCannonToSelectedShipServerRpc(string cannonId)
    {
        TryUpdateSelectedShipCannonLoadoutServer(cannonId, 1);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestUnequipCannonFromSelectedShipServerRpc(string cannonId)
    {
        TryUpdateSelectedShipCannonLoadoutServer(cannonId, -1);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushInventoryItemPurchaseResultClientRpc(string itemId, bool success, string message)
    {
        OnInventoryItemPurchaseResult?.Invoke(
            PlayerInventoryState.NormalizeItemId(itemId),
            success,
            string.IsNullOrWhiteSpace(message) ? (success ? "Purchase completed." : "Purchase failed.") : message);
    }

    private bool TryUpdateSelectedShipCannonLoadoutServer(string cannonId, int amountDelta)
    {
        if (!IsServer)
        {
            return false;
        }

        string selectedShipId = SelectedShipId;
        string normalizedShipId = NormalizeOwnedShipId(selectedShipId);
        string normalizedCannonId = NormalizeOwnedCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedShipId) ||
            string.IsNullOrWhiteSpace(normalizedCannonId) ||
            !OwnsShip(normalizedShipId) ||
            !PlayerInventoryState.IsCannon(normalizedCannonId) ||
            amountDelta == 0)
        {
            return false;
        }

        int currentShipAmount = GetShipEquippedCannonAmount(normalizedShipId, normalizedCannonId);
        int currentShipTotal = GetShipEquippedCannonTotal(normalizedShipId);
        int capacity = GetShipCannonCapacity(normalizedShipId);
        if (amountDelta > 0)
        {
            if (capacity <= 0 || currentShipTotal >= capacity || GetAvailableWarehouseCannonAmount(normalizedCannonId) < amountDelta)
            {
                return false;
            }
        }
        else
        {
            if (currentShipAmount < -amountDelta)
            {
                return false;
            }
        }

        List<ShipCannonLoadoutState> loadouts = PlayerInventoryState.ParseShipCannonLoadoutsSnapshot(ShipCannonLoadoutsSnapshot);
        var updatedLoadouts = new List<ShipCannonLoadoutState>(loadouts.Count + 1);
        bool updatedShip = false;
        for (int shipIndex = 0; shipIndex < loadouts.Count; shipIndex++)
        {
            ShipCannonLoadoutState loadout = loadouts[shipIndex];
            if (!string.Equals(loadout.ShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
            {
                updatedLoadouts.Add(loadout);
                continue;
            }

            updatedShip = true;
            updatedLoadouts.Add(UpdateShipLoadoutAmounts(loadout, normalizedCannonId, amountDelta));
        }

        if (!updatedShip)
        {
            updatedLoadouts.Add(UpdateShipLoadoutAmounts(new ShipCannonLoadoutState(normalizedShipId, Array.Empty<PlayerInventoryItemState>()), normalizedCannonId, amountDelta));
        }

        NormalizeAndApplyLoadoutsServer(updatedLoadouts, PlayerInventoryState.ParseInventorySnapshot(InventorySnapshot));
        return true;
    }

    internal bool TryValidateAttackResourcesForTargetServer(GameObject target, out bool useHarpoonVisual, out string failureReason)
    {
        useHarpoonVisual = false;
        failureReason = string.Empty;

        if (!IsServer)
        {
            failureReason = "attack resources can only be validated on the server";
            return false;
        }

        if (!CombatTargetingUtility.TryGetSeaEntity(target, out ISeaEntity seaEntity))
        {
            failureReason = "target was not a sea entity";
            return false;
        }

        if (seaEntity.EntityType == SeaEntityType.Monster)
        {
            string selectedHarpoonId = GetSelectedHarpoonAmmoId();
            if (string.IsNullOrWhiteSpace(selectedHarpoonId) || !PlayerInventoryState.IsHarpoon(selectedHarpoonId))
            {
                failureReason = "no harpoon ammo is selected";
                return false;
            }

            if (GetInventoryAmount(selectedHarpoonId) < 1)
            {
                failureReason = $"not enough {selectedHarpoonId} harpoons";
                return false;
            }

            useHarpoonVisual = true;
            return true;
        }

        int equippedCannons = GetCurrentShipEquippedCannonTotal();
        if (equippedCannons <= 0)
        {
            failureReason = "no cannons are equipped on the selected ship";
            return false;
        }

        if (ResolveCurrentShipCannonMaxRange() <= 0f)
        {
            failureReason = "equipped cannons do not have valid combat stats";
            return false;
        }

        string selectedAmmoId = GetSelectedCannonAmmoId();
        if (string.IsNullOrWhiteSpace(selectedAmmoId) || !PlayerInventoryState.IsCannonAmmo(selectedAmmoId))
        {
            failureReason = "no cannon ammo is selected";
            return false;
        }

        if (GetInventoryAmount(selectedAmmoId) < equippedCannons)
        {
            failureReason = $"not enough {selectedAmmoId} cannonballs for a {equippedCannons}-cannon salvo";
            return false;
        }

        return true;
    }

    internal bool TryConsumeAttackResourcesForTarget(GameObject target, out bool useHarpoonVisual, out bool usedBlackGunpowder, out string failureReason)
    {
        useHarpoonVisual = false;
        usedBlackGunpowder = false;
        failureReason = string.Empty;

        if (!IsServer)
        {
            failureReason = "attack resources can only be consumed on the server";
            return false;
        }

        if (!TryValidateAttackResourcesForTargetServer(target, out useHarpoonVisual, out failureReason))
        {
            return false;
        }

        if (useHarpoonVisual)
        {
            string selectedHarpoonId = GetSelectedHarpoonAmmoId();
            if (!TryConsumeInventoryItemServer(selectedHarpoonId, 1))
            {
                failureReason = $"not enough {selectedHarpoonId} harpoons";
                return false;
            }

            usedBlackGunpowder = TryConsumeActionItemChargeServer(PlayerActionItemType.BlackGunpowder);
            return true;
        }

        int equippedCannons = GetCurrentShipEquippedCannonTotal();
        string selectedAmmoId = GetSelectedCannonAmmoId();
        if (!TryConsumeInventoryItemServer(selectedAmmoId, equippedCannons))
        {
            failureReason = $"not enough {selectedAmmoId} cannonballs for a {equippedCannons}-cannon salvo";
            return false;
        }

        usedBlackGunpowder = TryConsumeActionItemChargeServer(PlayerActionItemType.BlackGunpowder);
        return true;
    }

    internal bool TryConsumeIncomingDefenseResources()
    {
        return TryConsumeActionItemChargeServer(PlayerActionItemType.AgwesArmorPlating);
    }

    private bool TryConsumeActionItemChargeServer(PlayerActionItemType actionItemType)
    {
        if (!IsServer || !HasActionItem(actionItemType))
        {
            return false;
        }

        string inventoryItemId = PlayerInventoryState.GetActionItemInventoryId(actionItemType);
        if (string.IsNullOrWhiteSpace(inventoryItemId) || !TryConsumeInventoryItemServer(inventoryItemId, 1))
        {
            SetActionItemEnabled(actionItemType, false);
            return false;
        }

        if (GetInventoryAmount(inventoryItemId) <= 0)
        {
            SetActionItemEnabled(actionItemType, false);
        }

        return true;
    }

    private bool TryConsumeInventoryItemServer(string itemId, int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return false;
        }

        string normalizedItemId = PlayerInventoryState.NormalizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalizedItemId) || GetInventoryAmount(normalizedItemId) < amount)
        {
            return false;
        }

        List<PlayerInventoryItemState> inventoryItems = PlayerInventoryState.ParseInventorySnapshot(InventorySnapshot);
        var updatedInventory = new List<PlayerInventoryItemState>(inventoryItems.Count);
        bool consumed = false;
        for (int index = 0; index < inventoryItems.Count; index++)
        {
            PlayerInventoryItemState stack = inventoryItems[index];
            if (!string.Equals(stack.ItemId, normalizedItemId, StringComparison.OrdinalIgnoreCase))
            {
                updatedInventory.Add(stack);
                continue;
            }

            int remainingAmount = Mathf.Max(0, stack.Amount - amount);
            if (remainingAmount > 0)
            {
                updatedInventory.Add(new PlayerInventoryItemState(stack.ItemId, remainingAmount));
            }

            consumed = true;
        }

        if (!consumed)
        {
            return false;
        }

        SetInventorySnapshotServer(updatedInventory);
        DisableUnavailableActionItemsServer();
        return true;
    }

    private void SetInventorySnapshotServer(IReadOnlyList<PlayerInventoryItemState> inventoryItems)
    {
        List<PlayerInventoryItemState> normalizedInventory = PlayerInventoryState.NormalizeInventory(inventoryItems);
        m_inventorySnapshot.Value = new FixedString4096Bytes(PlayerInventoryState.BuildInventorySnapshot(normalizedInventory));
        SyncOwnedCannonIdsFromInventory(normalizedInventory);
    }

    private void NormalizeAndApplyLoadoutsServer(IEnumerable<ShipCannonLoadoutState> loadouts, IReadOnlyList<PlayerInventoryItemState> normalizedInventory)
    {
        List<ShipCannonLoadoutState> normalizedLoadouts = NormalizeShipCannonLoadoutsForPlayer(loadouts, normalizedInventory);
        m_shipCannonLoadoutsSnapshot.Value = new FixedString4096Bytes(PlayerInventoryState.BuildShipCannonLoadoutsSnapshot(normalizedLoadouts));
    }

    private void SyncOwnedCannonIdsFromInventory(IReadOnlyList<PlayerInventoryItemState> inventoryItems)
    {
        if (!IsServer)
        {
            return;
        }

        var ownedCannonIds = new List<string>();
        if (inventoryItems != null)
        {
            for (int index = 0; index < inventoryItems.Count; index++)
            {
                PlayerInventoryItemState item = inventoryItems[index];
                if (item.Amount <= 0 || !PlayerInventoryState.IsCannon(item.ItemId))
                {
                    continue;
                }

                ownedCannonIds.Add(item.ItemId);
            }
        }

        m_ownedCannonIdsCsv.Value = new FixedString512Bytes(BuildOwnedCannonsCsv(ownedCannonIds));
    }

    private List<ShipCannonLoadoutState> NormalizeShipCannonLoadoutsForPlayer(IEnumerable<ShipCannonLoadoutState> loadouts, IReadOnlyList<PlayerInventoryItemState> normalizedInventory)
    {
        List<ShipCannonLoadoutState> normalizedRequestedLoadouts = PlayerInventoryState.NormalizeShipCannonLoadouts(loadouts);
        string[] ownedShipIds = GetOwnedShipIds();
        var ownedShipSet = new HashSet<string>(ownedShipIds, StringComparer.OrdinalIgnoreCase);
        var remainingCannonAmounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (normalizedInventory != null)
        {
            for (int index = 0; index < normalizedInventory.Count; index++)
            {
                PlayerInventoryItemState item = normalizedInventory[index];
                if (!PlayerInventoryState.IsCannon(item.ItemId) || item.Amount <= 0)
                {
                    continue;
                }

                remainingCannonAmounts[item.ItemId] = item.Amount;
            }
        }

        var loadoutsByShipId = new Dictionary<string, ShipCannonLoadoutState>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < normalizedRequestedLoadouts.Count; index++)
        {
            ShipCannonLoadoutState loadout = normalizedRequestedLoadouts[index];
            if (loadout == null || !ownedShipSet.Contains(loadout.ShipId))
            {
                continue;
            }

            loadoutsByShipId[loadout.ShipId] = loadout;
        }

        var resolvedLoadouts = new List<ShipCannonLoadoutState>(loadoutsByShipId.Count);
        for (int shipIndex = 0; shipIndex < ownedShipIds.Length; shipIndex++)
        {
            string shipId = ownedShipIds[shipIndex];
            if (!loadoutsByShipId.TryGetValue(shipId, out ShipCannonLoadoutState requestedLoadout) || requestedLoadout == null)
            {
                continue;
            }

            int remainingCapacity = Mathf.Max(0, GetShipCannonCapacity(shipId));
            if (remainingCapacity <= 0)
            {
                continue;
            }

            var resolvedStacks = new List<PlayerInventoryItemState>();
            IReadOnlyList<PlayerInventoryItemState> cannonStacks = requestedLoadout.CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
            for (int entryIndex = 0; entryIndex < cannonStacks.Count; entryIndex++)
            {
                PlayerInventoryItemState stack = cannonStacks[entryIndex];
                if (remainingCapacity <= 0 || !PlayerInventoryState.IsCannon(stack.ItemId) || stack.Amount <= 0)
                {
                    continue;
                }

                remainingCannonAmounts.TryGetValue(stack.ItemId, out int availableAmount);
                if (availableAmount <= 0)
                {
                    continue;
                }

                int resolvedAmount = Mathf.Min(stack.Amount, Mathf.Min(availableAmount, remainingCapacity));
                if (resolvedAmount <= 0)
                {
                    continue;
                }

                resolvedStacks.Add(new PlayerInventoryItemState(stack.ItemId, resolvedAmount));
                remainingCannonAmounts[stack.ItemId] = availableAmount - resolvedAmount;
                remainingCapacity -= resolvedAmount;
            }

            if (resolvedStacks.Count > 0)
            {
                resolvedLoadouts.Add(new ShipCannonLoadoutState(shipId, resolvedStacks));
            }
        }

        return resolvedLoadouts;
    }

    private void DisableUnavailableActionItemsServer()
    {
        if (!IsServer)
        {
            return;
        }

        if (GetActionItemAmount(PlayerActionItemType.BlackGunpowder) <= 0 && (ActiveActionItems & PlayerActionItemType.BlackGunpowder) != 0)
        {
            SetActionItemEnabled(PlayerActionItemType.BlackGunpowder, false);
        }

        if (GetActionItemAmount(PlayerActionItemType.AgwesArmorPlating) <= 0 && (ActiveActionItems & PlayerActionItemType.AgwesArmorPlating) != 0)
        {
            SetActionItemEnabled(PlayerActionItemType.AgwesArmorPlating, false);
        }
    }

    private int GetTotalEquippedCannonAmount(string cannonId)
    {
        string normalizedCannonId = NormalizeOwnedCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedCannonId))
        {
            return 0;
        }

        int totalEquipped = 0;
        IReadOnlyList<ShipCannonLoadoutState> loadouts = GetShipCannonLoadouts();
        for (int shipIndex = 0; shipIndex < loadouts.Count; shipIndex++)
        {
            IReadOnlyList<PlayerInventoryItemState> cannonStacks = loadouts[shipIndex].CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
            for (int entryIndex = 0; entryIndex < cannonStacks.Count; entryIndex++)
            {
                PlayerInventoryItemState stack = cannonStacks[entryIndex];
                if (string.Equals(stack.ItemId, normalizedCannonId, StringComparison.OrdinalIgnoreCase))
                {
                    totalEquipped += Mathf.Max(0, stack.Amount);
                }
            }
        }

        return totalEquipped;
    }

    internal float ResolveCurrentShipCannonMaxRange()
    {
        return TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile)
            ? combatProfile.AverageRange
            : 0f;
    }

    internal float ResolveCurrentShipCannonSalvoInterval()
    {
        return TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile) &&
               combatProfile.AverageReloadTimeSeconds > 0f
            ? combatProfile.AverageReloadTimeSeconds
            : 0.05f;
    }

    public float GetCurrentShipAverageCannonReloadTime()
    {
        return TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile)
            ? combatProfile.AverageReloadTimeSeconds
            : 0f;
    }

    public float GetCurrentShipAverageCannonRange()
    {
        return TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile)
            ? combatProfile.AverageRange
            : 0f;
    }

    public float GetCurrentShipAverageCannonHitProbability()
    {
        return TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile)
            ? combatProfile.AverageHitProbability
            : 0f;
    }

    internal float ResolveCurrentShipCannonProjectileSpeed()
    {
        return Mathf.Max(DefaultPlayerCannonProjectileSpeed, ResolveCurrentShipCannonMaxRange());
    }

    internal bool TryResolveCurrentShipCannonSalvoDamage(GameObject target, out int damageAmount)
    {
        damageAmount = 0;

        if (!TryResolveSelectedCannonAmmo(out CannonAmmoDefinition selectedAmmo) || selectedAmmo == null)
        {
            return false;
        }

        if (!TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile) ||
            !combatProfile.HasCannons)
        {
            return false;
        }

        float targetDistance = target != null ? Vector3.Distance(transform.position, target.transform.position) : 0f;
        if (target != null && combatProfile.AverageRange > 0f && targetDistance > combatProfile.AverageRange)
        {
            return true;
        }

        long totalDamage = 0L;
        for (int shotIndex = 0; shotIndex < combatProfile.EquippedCannonCount; shotIndex++)
        {
            totalDamage += ResolveCannonShotDamage(combatProfile, selectedAmmo.Damage);
            if (totalDamage >= int.MaxValue)
            {
                totalDamage = int.MaxValue;
                break;
            }
        }

        damageAmount = totalDamage >= int.MaxValue ? int.MaxValue : (int)totalDamage;
        return true;
    }

    private static ShipCannonLoadoutState UpdateShipLoadoutAmounts(ShipCannonLoadoutState sourceLoadout, string cannonId, int amountDelta)
    {
        IReadOnlyList<PlayerInventoryItemState> sourceStacks = sourceLoadout?.CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
        var updatedStacks = new List<PlayerInventoryItemState>(sourceStacks.Count + 1);
        bool matched = false;
        for (int index = 0; index < sourceStacks.Count; index++)
        {
            PlayerInventoryItemState stack = sourceStacks[index];
            if (!string.Equals(stack.ItemId, cannonId, StringComparison.OrdinalIgnoreCase))
            {
                updatedStacks.Add(stack);
                continue;
            }

            matched = true;
            int updatedAmount = Mathf.Max(0, stack.Amount + amountDelta);
            if (updatedAmount > 0)
            {
                updatedStacks.Add(new PlayerInventoryItemState(stack.ItemId, updatedAmount));
            }
        }

        if (!matched && amountDelta > 0)
        {
            updatedStacks.Add(new PlayerInventoryItemState(cannonId, amountDelta));
        }

        return new ShipCannonLoadoutState(sourceLoadout?.ShipId, updatedStacks);
    }

    private bool TryResolveCurrentShipCannonCombatProfile(out CurrentShipCannonCombatProfile combatProfile)
    {
        combatProfile = default;
        float reloadTotal = 0f;
        float rangeTotal = 0f;
        float hitProbabilityTotal = 0f;
        float criticalHitProbabilityTotal = 0f;
        float criticalHitDamageTotal = 0f;
        float bonusDamageFlatTotal = 0f;
        float bonusDamagePercentageTotal = 0f;
        int totalCannons = 0;

        VisitCurrentShipEquippedCannons((cannon, amount) =>
        {
            if (amount <= 0)
            {
                return;
            }

            reloadTotal += cannon.ReloadTimeSeconds * amount;
            rangeTotal += cannon.CannonRange * amount;
            hitProbabilityTotal += cannon.HitProbability * amount;
            criticalHitProbabilityTotal += cannon.CriticalHitProbability * amount;
            criticalHitDamageTotal += cannon.CriticalHitDamage * amount;
            bonusDamageFlatTotal += cannon.BonusDamageFlat * amount;
            bonusDamagePercentageTotal += cannon.BonusDamagePercentage * amount;
            totalCannons += amount;
        });

        if (totalCannons <= 0)
        {
            return false;
        }

        float divisor = totalCannons;
        combatProfile = new CurrentShipCannonCombatProfile
        {
            EquippedCannonCount = totalCannons,
            AverageReloadTimeSeconds = reloadTotal / divisor,
            AverageRange = rangeTotal / divisor,
            AverageHitProbability = hitProbabilityTotal / divisor,
            AverageCriticalHitProbability = criticalHitProbabilityTotal / divisor,
            AverageCriticalHitDamage = criticalHitDamageTotal / divisor,
            AverageBonusDamageFlat = bonusDamageFlatTotal / divisor,
            AverageBonusDamagePercentage = bonusDamagePercentageTotal / divisor
        };

        return true;
    }

    private void VisitCurrentShipEquippedCannons(Action<MarketCannonData, int> visitor)
    {
        if (visitor == null)
        {
            return;
        }

        string normalizedShipId = NormalizeOwnedShipId(SelectedShipId);
        if (string.IsNullOrWhiteSpace(normalizedShipId))
        {
            return;
        }

        IReadOnlyList<ShipCannonLoadoutState> loadouts = GetShipCannonLoadouts();
        for (int shipIndex = 0; shipIndex < loadouts.Count; shipIndex++)
        {
            ShipCannonLoadoutState loadout = loadouts[shipIndex];
            if (!string.Equals(loadout.ShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<PlayerInventoryItemState> cannonStacks = loadout.CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
            for (int entryIndex = 0; entryIndex < cannonStacks.Count; entryIndex++)
            {
                PlayerInventoryItemState stack = cannonStacks[entryIndex];
                int amount = Mathf.Max(0, stack.Amount);
                if (amount <= 0 || !MarketCannonCatalogRuntime.TryGetCannon(stack.ItemId, out MarketCannonData cannon) || cannon == null)
                {
                    continue;
                }

                visitor(cannon, amount);
            }

            return;
        }
    }

    private static int ResolveCannonShotDamage(CurrentShipCannonCombatProfile combatProfile, int baseAmmoDamage)
    {
        if (!combatProfile.HasCannons || !RollPercentageChance(combatProfile.AverageHitProbability))
        {
            return 0;
        }

        float shotDamage = Mathf.Max(0f, baseAmmoDamage + combatProfile.AverageBonusDamageFlat);
        if (shotDamage <= 0f)
        {
            return 0;
        }

        if (combatProfile.AverageBonusDamagePercentage > 0f)
        {
            shotDamage *= 1f + (combatProfile.AverageBonusDamagePercentage / 100f);
        }

        if (combatProfile.AverageCriticalHitProbability > 0f &&
            RollPercentageChance(combatProfile.AverageCriticalHitProbability))
        {
            shotDamage *= 1f + (combatProfile.AverageCriticalHitDamage / 100f);
        }

        return Mathf.Max(1, Mathf.RoundToInt(shotDamage));
    }

    private static bool RollPercentageChance(float chancePercent)
    {
        if (chancePercent <= 0f)
        {
            return false;
        }

        if (chancePercent >= 100f)
        {
            return true;
        }

        return UnityEngine.Random.value * 100f < chancePercent;
    }

    private bool TryResolveSelectedCannonAmmo(out CannonAmmoDefinition ammo)
    {
        return TryResolveSelectedCannonAmmo(out _, out ammo);
    }

    private bool TryResolveSelectedCannonAmmo(out int resolvedIndex, out CannonAmmoDefinition ammo)
    {
        resolvedIndex = -1;
        ammo = null;

        IReadOnlyList<CannonAmmoDefinition> ammoOptions = GetCannonAmmoOptions();
        if (ammoOptions == null || ammoOptions.Count == 0)
        {
            return false;
        }

        int clampedIndex = Mathf.Clamp(selectedCannonAmmoIndex, 0, ammoOptions.Count - 1);
        ammo = ammoOptions[clampedIndex];
        if (ammo == null)
        {
            for (int index = 0; index < ammoOptions.Count; index++)
            {
                if (ammoOptions[index] == null)
                {
                    continue;
                }

                clampedIndex = index;
                ammo = ammoOptions[index];
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

    private string GetSelectedCannonAmmoId()
    {
        return TryResolveSelectedCannonAmmo(out CannonAmmoDefinition ammo)
            ? PlayerInventoryState.NormalizeItemId(ammo.Id)
            : string.Empty;
    }

    private string GetSelectedHarpoonAmmoId()
    {
        IReadOnlyList<HarpoonAmmoDefinition> harpoonOptions = GetHarpoonAmmoOptions();
        if (harpoonOptions == null || harpoonOptions.Count == 0)
        {
            return string.Empty;
        }

        int clampedIndex = Mathf.Clamp(SelectedHarpoonAmmoIndex, 0, harpoonOptions.Count - 1);
        HarpoonAmmoDefinition ammo = harpoonOptions[clampedIndex];
        if (ammo == null)
        {
            for (int index = 0; index < harpoonOptions.Count; index++)
            {
                if (harpoonOptions[index] != null)
                {
                    ammo = harpoonOptions[index];
                    break;
                }
            }
        }

        return ammo != null ? PlayerInventoryState.NormalizeItemId(ammo.Id) : string.Empty;
    }
}
