using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

/// <summary>
/// Handles click-to-move navigation for the player.
/// Works with NavMeshAgent for pathfinding.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ClickToMove : NetworkBehaviour, IClickable
{
    private const string WalkableAreaName = "Walkable";

    private NavMeshAgent navMeshAgent;
    private int walkableAreaMask = NavMesh.AllAreas;
    private NavMeshPath validatedPath;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        navMeshAgent.enabled = false;
        validatedPath = new NavMeshPath();

        int walkableArea = NavMesh.GetAreaFromName(WalkableAreaName);
        if (walkableArea >= 0)
        {
            walkableAreaMask = 1 << walkableArea;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            navMeshAgent.enabled = true;
            navMeshAgent.updateRotation = false;
            navMeshAgent.autoBraking = false;
            navMeshAgent.stoppingDistance = 0.0f;
            navMeshAgent.areaMask = walkableAreaMask;
        }
        else
        {
            navMeshAgent.enabled = false;
        }
    }

    public void OnClick(Vector3 position)
    {
        if (!IsOwner) return;

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

        NavMeshHit hit;
        // Sample slightly larger radius to ensure clicks near edges work
        if (NavMesh.SamplePosition(position, out hit, 2.0f, walkableAreaMask) &&
            navMeshAgent.CalculatePath(hit.position, validatedPath) &&
            validatedPath.status == NavMeshPathStatus.PathComplete)
        {
            navMeshAgent.SetPath(validatedPath);
        }
    }

    private void Update()
    {
        // Movement logic runs ONLY on Server
        if (!IsServer || !navMeshAgent.enabled) return;

        // If we have a path and are moving
        if (navMeshAgent.hasPath)
        {
            // --- INSTANT ROTATION ---
            // steeringTarget is the next immediate corner/point the agent is heading to.
            Vector3 direction = (navMeshAgent.steeringTarget - transform.position).normalized;

            // Zero out Y so the character doesn't look at the ground/sky on slopes
            direction.y = 0;

            if (direction != Vector3.zero)
            {
                // Snap rotation instantly
                transform.rotation = Quaternion.LookRotation(direction);
            }

            // --- STOPPING LOGIC ---
            // Check if we are close enough to the destination
            if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance + 0.1f && !navMeshAgent.pathPending)
            {
                navMeshAgent.ResetPath(); // Hard stop
                navMeshAgent.velocity = Vector3.zero;
                return;
            }

            // --- INSTANT ACCELERATION ---
            // If the agent wants to move, apply max speed immediately
            // This bypasses the 'Acceleration' variable entirely.
            if (navMeshAgent.desiredVelocity.sqrMagnitude > 0.1f)
            {
                // Apply velocity directly for "Instant" start
                navMeshAgent.velocity = navMeshAgent.desiredVelocity.normalized * navMeshAgent.speed;
            }
        }
    }
}
