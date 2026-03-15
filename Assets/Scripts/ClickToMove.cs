using System.Collections;
using Pathfinding;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles server-authoritative click-to-move navigation over the runtime A* grid graph.
/// </summary>
[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(AILerp))]
public class ClickToMove : NetworkBehaviour, IClickable
{
    [SerializeField, Min(0.1f)] private float clickSnapDistance = 12f;

    private AILerp aiLerp;
    private PlayerDirectionSpriteController spriteController;
    private Coroutine waitForNavigationCoroutine;
    private Vector3 previousPosition;
    private Vector3 lastValidDirection;

    private void Awake()
    {
        EnsureComponents();
        aiLerp.enabled = false;
        aiLerp.enableRotation = false;
        previousPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (waitForNavigationCoroutine != null)
        {
            StopCoroutine(waitForNavigationCoroutine);
        }

        if (IsServer)
        {
            waitForNavigationCoroutine = StartCoroutine(EnableMovementWhenNavigationReady());
        }
        else
        {
            aiLerp.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (waitForNavigationCoroutine != null)
        {
            StopCoroutine(waitForNavigationCoroutine);
            waitForNavigationCoroutine = null;
        }

        base.OnNetworkDespawn();
    }

    public void OnClick(Vector3 position)
    {
        if (!IsOwner)
        {
            return;
        }

        if (TryGetComponent(out Player player) && player.IsDead)
        {
            return;
        }

        SubmitMoveRequestServerRpc(position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitMoveRequestServerRpc(Vector3 position)
    {
        if (TryGetComponent(out Player player) && player.IsDead)
        {
            return;
        }

        if (!AstarNavigationUtility.TryGetNearestWalkablePoint(position, clickSnapDistance, out Vector3 targetPoint))
        {
            return;
        }

        aiLerp.destination = targetPoint;
        aiLerp.isStopped = false;
        aiLerp.simulateMovement = true;
        aiLerp.SearchPath();
    }

    public void ApplyMovementSettings(float movementSpeed)
    {
        if (!EnsureComponents())
        {
            return;
        }

        aiLerp.speed = Mathf.Max(0f, movementSpeed);
    }

    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        if (!EnsureComponents())
        {
            transform.SetPositionAndRotation(position, rotation);
            previousPosition = position;
            return;
        }

        transform.SetPositionAndRotation(position, rotation);
        if (aiLerp != null)
        {
            aiLerp.destination = position;
            aiLerp.Teleport(position, clearPath: true);
        }

        previousPosition = position;
    }

    public void SetMovementEnabled(bool enabled)
    {
        if (!IsServer || !EnsureComponents())
        {
            return;
        }

        aiLerp.simulateMovement = enabled;
        aiLerp.isStopped = !enabled;

        if (!enabled)
        {
            aiLerp.destination = transform.position;
            aiLerp.SearchPath();
        }
    }

    private IEnumerator EnableMovementWhenNavigationReady()
    {
        while (IsSpawned && !AstarNavigationUtility.IsReady)
        {
            yield return null;
        }

        if (!IsSpawned || !EnsureComponents())
        {
            yield break;
        }

        aiLerp.enabled = true;
        aiLerp.simulateMovement = true;
        aiLerp.isStopped = false;
        waitForNavigationCoroutine = null;
    }

    private bool EnsureComponents()
    {
        aiLerp ??= GetComponent<AILerp>();
        spriteController ??= GetComponentInChildren<PlayerDirectionSpriteController>(true);
        return aiLerp != null;
    }

    private void Update()
    {
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;

        if (displacement.sqrMagnitude > 0.0001f)
        {
            lastValidDirection = displacement.normalized;

            if (IsServer)
            {
                transform.rotation = Quaternion.LookRotation(lastValidDirection, Vector3.up);
            }

            spriteController?.UpdateSprite(lastValidDirection);
        }
        else if (lastValidDirection != Vector3.zero)
        {
            spriteController?.UpdateSprite(lastValidDirection);
        }

        previousPosition = transform.position;
    }
}
