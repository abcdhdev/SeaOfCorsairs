using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative combat controller.
/// In networked play, clients only request Start/Stop attack and server drives
/// cadence, validation, damage, and fire synchronization RPCs.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerAttack : NetworkBehaviour
{
    private struct PendingImpact
    {
        public ulong TargetNetworkObjectId;
        public float ImpactAtTime;
        public int DamageAmount;
        public bool UsedBlackGunpowder;
    }

    private WeaponFireController _fireController;
    private ulong serverTargetNetworkObjectId;

    [SerializeField, HideInInspector] private float maxHitDistance = 150f;
    [SerializeField, HideInInspector] private float shootingInterval = 2f;
    [SerializeField, HideInInspector] private int damage = 20;
    [SerializeField, HideInInspector] private int baseDamage = 20;
    [SerializeField, HideInInspector] private int ammoBonusDamage;
    [SerializeField, HideInInspector] private int harpoonDamage = 25;
    [SerializeField, HideInInspector] private LayerMask hitOcclusionMask = ~0;

    private WeaponFireController FireController
    {
        get
        {
            if (_fireController == null)
            {
                _fireController = GetComponent<WeaponFireController>();
            }

            return _fireController;
        }
    }

    public void ApplySettings(int newBaseDamage, float newMaxHitDistance, float newShootingInterval)
    {
        baseDamage = Mathf.Max(0, newBaseDamage);
        RecalculateDamage();
        maxHitDistance = Mathf.Max(0f, newMaxHitDistance);
        shootingInterval = Mathf.Max(0.05f, newShootingInterval);
    }

    public void ApplyAmmoOverride(int newAmmoBonusDamage)
    {
        ammoBonusDamage = Mathf.Max(0, newAmmoBonusDamage);
        RecalculateDamage();
    }

    public void ApplyHarpoonOverride(int newHarpoonDamage)
    {
        harpoonDamage = Mathf.Max(0, newHarpoonDamage);
    }



    private Coroutine _serverAttackRoutine;
    private GameObject _syncedTarget;
    private readonly List<PendingImpact> _pendingImpacts = new List<PendingImpact>(8);

    public event Action<GameObject> OnShootingTargetChanged = delegate { };
    public bool IsAttacking => _syncedTarget != null;
    public GameObject CurrentTarget => _syncedTarget;

    // ------------------------------------------------------------------
    //  Public API
    // ------------------------------------------------------------------

    public void StartAttack(GameObject target)
    {
        if (target == null) return;

        if (IsServer)
        {
            if (TryGetNetworkObjectId(target, out ulong targetId))
            {
                ServerStartAttack(targetId, target);
            }

            return;
        }

        if (!IsOwner)
        {
            return;
        }

        if (TryGetNetworkObjectId(target, out ulong requestedTargetId))
        {
            StartAttackServerRpc(requestedTargetId);
        }
    }

    public void StopAttack()
    {
        if (IsServer)
        {
            ServerExitCombat();
            return;
        }

        if (IsOwner)
        {
            StopAttackServerRpc();
        }
    }

    public void RequestDamage(GameObject target)
    {
        if (target == null) return;

        // Networked sessions are fully server-driven.
        if (!IsServer) return;

        if (!TryGetNetworkObjectId(target, out ulong targetNetworkObjectId)) return;
        if (!TryValidateHitTarget(target, true, targetNetworkObjectId, out string failureReason))
        {
            Debug.LogWarning($"[Combat][Attack:{name}] Damage request rejected for target '{target.name}': {failureReason}");
            return;
        }

        if (!TryResolveDamageAmountForTarget(target, usedBlackGunpowder: false, out int resolvedDamage))
        {
            return;
        }

        if (resolvedDamage <= 0)
        {
            return;
        }

        ApplyDamageToTarget(target, resolvedDamage, gameObject);
    }

    private void Update()
    {
        if (!IsServer || _pendingImpacts.Count == 0)
        {
            return;
        }

        float now = Time.time;
        for (int i = _pendingImpacts.Count - 1; i >= 0; i--)
        {
            PendingImpact impact = _pendingImpacts[i];
            if (impact.ImpactAtTime > now)
            {
                continue;
            }

            _pendingImpacts.RemoveAt(i);

            if (!TryResolveNetworkObject(impact.TargetNetworkObjectId, out NetworkObject targetNetObj))
            {
                continue;
            }

            CombatActionItemUtility.MarkNextIncomingDamageEffect(gameObject, impact.UsedBlackGunpowder);
            ApplyDamageToTarget(targetNetObj.gameObject, impact.DamageAmount, gameObject);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_serverAttackRoutine != null)
        {
            StopCoroutine(_serverAttackRoutine);
            _serverAttackRoutine = null;
        }

        if (IsServer && serverTargetNetworkObjectId != 0 && TryResolveNetworkObject(serverTargetNetworkObjectId, out NetworkObject currentTarget))
        {
            currentTarget.GetComponent<ICombat>()?.ExitCombat(gameObject);
        }

        serverTargetNetworkObjectId = 0;
        _pendingImpacts.Clear();
        SetSyncedTarget(null);
        base.OnNetworkDespawn();
    }

    private void SetSyncedTarget(GameObject target)
    {
        if (_syncedTarget == target)
        {
            return;
        }

        _syncedTarget = target;
        OnShootingTargetChanged?.Invoke(_syncedTarget);
    }

    private void RecalculateDamage()
    {
        damage = Mathf.Max(0, baseDamage + ammoBonusDamage);
    }

    private bool TryResolveDamageAmountForTarget(GameObject target, bool usedBlackGunpowder, out int resolvedDamage)
    {
        resolvedDamage = 0;
        if (!CombatTargetingUtility.TryGetSeaEntity(target, out ISeaEntity seaEntity))
        {
            return false;
        }

        int baseResolvedDamage;
        if (seaEntity.EntityType == SeaEntityType.Monster)
        {
            baseResolvedDamage = Mathf.Max(0, harpoonDamage);
            if (baseResolvedDamage <= 0)
            {
                return false;
            }
        }
        else if (TryGetComponent(out Player attackingPlayer))
        {
            if (!attackingPlayer.TryResolveCurrentShipCannonSalvoDamage(target, out baseResolvedDamage))
            {
                return false;
            }
        }
        else
        {
            baseResolvedDamage = Mathf.Max(0, damage);
            if (baseResolvedDamage <= 0)
            {
                return false;
            }
        }

        resolvedDamage = CombatActionItemUtility.ApplyOutgoingDamageModifiers(baseResolvedDamage, usedBlackGunpowder);
        return true;
    }

    // ------------------------------------------------------------------
    //  RPCs
    // ------------------------------------------------------------------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StartAttackServerRpc(ulong targetNetworkObjectId)
    {
        if (!TryResolveNetworkObject(targetNetworkObjectId, out NetworkObject targetNetObj)) return;

        ServerStartAttack(targetNetworkObjectId, targetNetObj.gameObject);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StopAttackServerRpc()
    {
        ServerExitCombat();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void SyncAttackStateClientRpc(ulong targetNetworkObjectId, bool isAttacking)
    {
        if (!isAttacking || targetNetworkObjectId == 0)
        {
            SetSyncedTarget(null);
            return;
        }

        if (TryResolveNetworkObject(targetNetworkObjectId, out NetworkObject targetNetObj))
        {
            SetSyncedTarget(targetNetObj.gameObject);
        }
        else
        {
            SetSyncedTarget(null);
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void BroadcastFireClientRpc(ulong targetNetworkObjectId, bool useHarpoonVisual)
    {
        if (TryResolveNetworkObject(targetNetworkObjectId, out NetworkObject targetNetObj))
        {
            FireController?.PlayReplicatedFire(targetNetObj.gameObject, useHarpoonVisual);
        }
    }

    // ------------------------------------------------------------------
    //  Server-only combat state
    // ------------------------------------------------------------------

    private void ServerStartAttack(ulong targetNetworkObjectId, GameObject target)
    {
        if (!IsServer) return;
        if (!TryValidateHitTarget(target, false, targetNetworkObjectId, out string failureReason))
        {
            Debug.LogWarning($"[Combat][Attack:{name}] Could not start attack on '{target?.name}': {failureReason}");
            ServerExitCombat();
            return;
        }

        if (TryGetComponent(out Player attackingPlayer) &&
            !attackingPlayer.TryValidateAttackResourcesForTargetServer(target, out _, out string resourceFailureReason))
        {
            Debug.LogWarning($"[Combat][Attack:{name}] Could not start attack on '{target?.name}': {resourceFailureReason}");
            ServerExitCombat();
            return;
        }

        if (serverTargetNetworkObjectId != targetNetworkObjectId)
        {
            if (serverTargetNetworkObjectId != 0 && TryResolveNetworkObject(serverTargetNetworkObjectId, out NetworkObject prevTarget))
            {
                prevTarget.GetComponent<ICombat>()?.ExitCombat(gameObject);
            }

            serverTargetNetworkObjectId = targetNetworkObjectId;
            Debug.Log($"[Combat][Attack:{name}] Starting attack on '{target.name}' (targetNetId: {targetNetworkObjectId}).");
            target.GetComponent<ICombat>()?.EnterCombat(gameObject);
            SyncAttackStateClientRpc(targetNetworkObjectId, true);
        }

        if (_serverAttackRoutine == null)
        {
            _serverAttackRoutine = StartCoroutine(ServerAttackLoop());
        }
    }

    private void ServerExitCombat()
    {
        if (!IsServer) return;

        if (_serverAttackRoutine != null)
        {
            StopCoroutine(_serverAttackRoutine);
            _serverAttackRoutine = null;
        }

        if (serverTargetNetworkObjectId != 0 && TryResolveNetworkObject(serverTargetNetworkObjectId, out NetworkObject currentTarget))
        {
            currentTarget.GetComponent<ICombat>()?.ExitCombat(gameObject);
        }

        serverTargetNetworkObjectId = 0;
        SyncAttackStateClientRpc(0, false);
    }

    private IEnumerator ServerAttackLoop()
    {
        while (serverTargetNetworkObjectId != 0)
        {
            ulong targetNetworkObjectId = serverTargetNetworkObjectId;
            if (!TryResolveNetworkObject(targetNetworkObjectId, out NetworkObject targetNetObj))
            {
                ServerExitCombat();
                yield break;
            }

            GameObject target = targetNetObj.gameObject;
            if (!TryValidateHitTarget(target, true, targetNetworkObjectId, out string failureReason))
            {
                Debug.LogWarning($"[Combat][Attack:{name}] Attack loop stopping for '{target.name}': {failureReason}");
                ServerExitCombat();
                yield break;
            }

            bool useHarpoonVisual = ShouldUseHarpoonVisual(target);
            bool usedBlackGunpowder = false;
            if (TryGetComponent(out Player attackingPlayer))
            {
                if (!attackingPlayer.TryConsumeAttackResourcesForTarget(target, out useHarpoonVisual, out usedBlackGunpowder, out string consumeFailureReason))
                {
                    Debug.LogWarning($"[Combat][Attack:{name}] Attack loop stopping for '{target.name}': {consumeFailureReason}");
                    ServerExitCombat();
                    yield break;
                }
            }

            BroadcastFireClientRpc(targetNetworkObjectId, useHarpoonVisual);

            if (!TryResolveDamageAmountForTarget(target, usedBlackGunpowder, out int resolvedDamage))
            {
                Debug.LogWarning($"[Combat][Attack:{name}] Attack loop stopping for '{target.name}': no valid damage profile was available.");
                ServerExitCombat();
                yield break;
            }

            if (resolvedDamage > 0)
            {
                QueueImpact(targetNetworkObjectId, resolvedDamage, GetProjectileTravelDelay(target), usedBlackGunpowder);
            }

            float remainingInterval = Mathf.Max(0.05f, shootingInterval);
            while (remainingInterval > 0f && serverTargetNetworkObjectId != 0)
            {
                float step = Mathf.Min(0.1f, remainingInterval);
                yield return new WaitForSeconds(step);
                remainingInterval -= step;

                if (serverTargetNetworkObjectId == 0)
                {
                    break;
                }

                if (!TryResolveNetworkObject(serverTargetNetworkObjectId, out NetworkObject revalidatedTarget))
                {
                    ServerExitCombat();
                    yield break;
                }

                if (!TryValidateHitTarget(revalidatedTarget.gameObject, true, serverTargetNetworkObjectId, out string revalidationFailureReason))
                {
                    Debug.LogWarning($"[Combat][Attack:{name}] Revalidation failed for '{revalidatedTarget.name}': {revalidationFailureReason}");
                    ServerExitCombat();
                    yield break;
                }
            }
        }

        _serverAttackRoutine = null;
    }

    private void QueueImpact(ulong targetNetworkObjectId, int damageAmount, float delaySeconds, bool usedBlackGunpowder)
    {
        if (!IsServer || targetNetworkObjectId == 0 || damageAmount <= 0)
        {
            return;
        }

        _pendingImpacts.Add(new PendingImpact
        {
            TargetNetworkObjectId = targetNetworkObjectId,
            ImpactAtTime = Time.time + Mathf.Max(0f, delaySeconds),
            DamageAmount = damageAmount,
            UsedBlackGunpowder = usedBlackGunpowder
        });
    }

    private float GetProjectileTravelDelay(GameObject target)
    {
        if (target == null)
        {
            return 0f;
        }

        WeaponFireController fireController = FireController;
        if (fireController == null)
        {
            return 0f;
        }

        float speed = fireController.FireSpeed;
        if (speed <= 0.01f)
        {
            return 0f;
        }

        float distance = Vector3.Distance(transform.position, target.transform.position);
        return distance / speed;
    }

    private bool ShouldUseHarpoonVisual(GameObject target)
    {
        return target != null &&
               harpoonDamage > 0 &&
               CombatTargetingUtility.TryGetSeaEntity(target, out ISeaEntity seaEntity) &&
               seaEntity.EntityType == SeaEntityType.Monster;
    }

    // ------------------------------------------------------------------
    //  Validation
    // ------------------------------------------------------------------

    private bool TryValidateHitTarget(GameObject target, bool requireServerTargetMatch, ulong targetNetworkObjectId, out string failureReason)
    {
        failureReason = string.Empty;
        if (target == null)
        {
            failureReason = "target was null";
            return false;
        }

        if (target == gameObject)
        {
            failureReason = "target was self";
            return false;
        }

        if (!IsAttackerAlive())
        {
            failureReason = "attacker is dead";
            return false;
        }

        if (!CombatTargetingUtility.TryGetCombatEntity(target, out ICombatEntity combatEntity))
        {
            failureReason = "target was not a combat entity";
            return false;
        }

        if (!CanAttackTargetType(combatEntity.EntityType))
        {
            failureReason = $"target type '{combatEntity.EntityType}' is not valid for this weapon";
            return false;
        }

        if (!combatEntity.CanBeTargeted)
        {
            failureReason = "target cannot be targeted";
            return false;
        }

        if (requireServerTargetMatch)
        {
            if (serverTargetNetworkObjectId == 0 || serverTargetNetworkObjectId != targetNetworkObjectId)
            {
                failureReason = "target did not match current server target";
                return false;
            }
        }

        if (!IsTargetWithinRange(target))
        {
            failureReason = "target was out of range";
            return false;
        }

        if (!HasFogOfWarVisibility(target, out string fogOfWarFailureReason))
        {
            failureReason = fogOfWarFailureReason;
            return false;
        }

        if (!HasLineOfSight(target, out string lineOfSightFailureReason))
        {
            failureReason = lineOfSightFailureReason;
            return false;
        }

        if (!IsTargetAlive(target))
        {
            failureReason = "target is dead";
            return false;
        }

        return true;
    }

    private bool CanAttackTargetType(SeaEntityType entityType)
    {
        return entityType switch
        {
            SeaEntityType.Player => true,
            SeaEntityType.Npc => true,
            SeaEntityType.Turret => true,
            SeaEntityType.Monster => harpoonDamage > 0,
            _ => false
        };
    }

    private bool HasFogOfWarVisibility(GameObject target, out string failureReason)
    {
        failureReason = string.Empty;
        if (target == null)
        {
            failureReason = "target was hidden by fog of war";
            return false;
        }

        if (TryGetComponent(out Player attackingPlayer))
        {
            ulong viewerClientId = attackingPlayer.OwnerClientId;

            if (target.TryGetComponent(out Player targetPlayer))
            {
                if (!FogOfWarNetworkVisibilityController.ShouldPlayerBeVisibleToClient(targetPlayer, viewerClientId))
                {
                    failureReason = "target was hidden by fog of war";
                    return false;
                }

                return true;
            }

            if (target.TryGetComponent(out NPC targetNpc))
            {
                if (!FogOfWarNetworkVisibilityController.ShouldNpcBeVisibleToClient(targetNpc, viewerClientId))
                {
                    failureReason = "target was hidden by fog of war";
                    return false;
                }

                return true;
            }
        }
        else if (TryGetComponent(out NPC attackingNpc) && target.TryGetComponent(out Player defendingPlayer))
        {
            if (!FogOfWarNetworkVisibilityController.ShouldNpcBeVisibleToClient(attackingNpc, defendingPlayer.OwnerClientId))
            {
                failureReason = "attacker was hidden by fog of war";
                return false;
            }
        }

        return true;
    }

    private bool IsAttackerAlive()
    {
        if (CombatTargetingUtility.TryGetCombatEntity(gameObject, out ICombatEntity combatEntity))
        {
            return combatEntity.CurrentHealth > 0;
        }

        return true;
    }

    private bool IsTargetWithinRange(GameObject target)
    {
        if (target == null) return false;
        if (maxHitDistance <= 0f) return true;

        float sqrDistance = (target.transform.position - transform.position).sqrMagnitude;
        return sqrDistance <= maxHitDistance * maxHitDistance;
    }

    private bool HasLineOfSight(GameObject target, out string failureReason)
    {
        failureReason = string.Empty;
        if (target == null)
        {
            failureReason = "line of sight target was null";
            return false;
        }

        Vector3 origin = transform.position + Vector3.up;
        Vector3 targetPoint = CombatTargetingUtility.GetAimPoint(target);
        Vector3 delta = targetPoint - origin;
        float distance = delta.magnitude;
        if (distance <= 0.001f) return true;

        Vector3 direction = delta / distance;
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, hitOcclusionMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return true;
        }

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        int waterLayer = LayerMask.NameToLayer("Water");
        Transform attackerTransform = transform;
        Transform targetTransform = target.transform;

        for (int index = 0; index < hits.Length; index++)
        {
            RaycastHit hit = hits[index];
            Transform hitTransform = hit.collider != null ? hit.collider.transform : null;
            if (hitTransform == null)
            {
                continue;
            }

            if (hitTransform == attackerTransform || hitTransform.IsChildOf(attackerTransform))
            {
                continue;
            }

            if (waterLayer >= 0 && hit.collider.gameObject.layer == waterLayer)
            {
                continue;
            }

            if (hitTransform == targetTransform || hitTransform.IsChildOf(targetTransform))
            {
                return true;
            }

            failureReason = $"line of sight was blocked by '{hit.collider.name}'";
            return false;
        }

        return true;
    }
    private static bool IsTargetAlive(GameObject target)
    {
        return target != null &&
               CombatTargetingUtility.TryGetCombatEntity(target, out ICombatEntity combatEntity) &&
               combatEntity.CurrentHealth > 0;
    }

    // ------------------------------------------------------------------
    //  Network helpers
    // ------------------------------------------------------------------

    private bool TryGetNetworkObjectId(GameObject target, out ulong networkObjectId)
    {
        networkObjectId = 0;
        if (target == null) return false;
        if (!target.TryGetComponent(out NetworkObject netObj)) return false;
        if (!netObj.IsSpawned) return false;

        networkObjectId = netObj.NetworkObjectId;
        return true;
    }

    private bool TryResolveNetworkObject(ulong networkObjectId, out NetworkObject networkObject)
    {
        networkObject = null;
        if (NetworkManager == null || NetworkManager.SpawnManager == null) return false;

        return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out networkObject);
    }

    // ------------------------------------------------------------------
    //  Damage application
    // ------------------------------------------------------------------

    private static void ApplyDamageToTarget(GameObject target, int damageAmount, GameObject damageSource)
    {
        if (target == null || damageAmount <= 0) return;

        if (target.TryGetComponent(out IDamageSourceReceiver damageReceiver))
        {
            damageReceiver.TakeDamage(damageAmount, damageSource);
            return;
        }

        if (target.TryGetComponent(out IHealthSystem healthSystem))
        {
            healthSystem.TakeDamage(damageAmount);
        }
    }
}
