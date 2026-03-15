using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCMovement : NetworkBehaviour
{
    private const string WalkableAreaName = "Walkable";
    private const string WaterLayerName = "Water";
    private const float WaterSurfaceProbeHeight = 100f;
    private const float WaterSurfaceProbeDistance = 300f;
    private const int MaxRoamSampleAttempts = 10;

    [SerializeField] private float roamRadius = 10f;
    [SerializeField] private float waitTime = 3f;
    [SerializeField, Min(0.1f)] private float roamNavMeshSampleDistance = 2f;
    [SerializeField, Min(0f)] private float leashRadius = 300f;
    [SerializeField, Min(0f)] private float homeArrivalDistance = 8f;
    
    private NavMeshAgent navMeshAgent;
    private bool isRoaming = true;
    private bool isReturningHome;
    private int walkableAreaMask;
    private int waterLayer = -1;
    private readonly RaycastHit[] waterProbeHits = new RaycastHit[8];
    private Vector3 homePosition;
    private Quaternion homeRotation = Quaternion.identity;
    private bool hasHomeAnchor;

    public event Action LeashExceeded = delegate { };

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        // Disable by default to avoid issues on clients before network spawn
        navMeshAgent.enabled = false;
        homePosition = transform.position;
        homeRotation = transform.rotation;
        hasHomeAnchor = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        int walkableArea = NavMesh.GetAreaFromName(WalkableAreaName);
        walkableAreaMask = walkableArea >= 0 ? 1 << walkableArea : NavMesh.AllAreas;
        if (walkableArea < 0)
        {
            Debug.LogWarning($"NPCMovement: NavMesh area '{WalkableAreaName}' was not found. Falling back to all NavMesh areas.");
        }

        waterLayer = LayerMask.NameToLayer(WaterLayerName);
        if (waterLayer < 0)
        {
            Debug.LogWarning($"NPCMovement: Layer '{WaterLayerName}' was not found. Water surface validation will be skipped.");
        }
        
        // Only the server controls NPC movement
        if (IsServer)
        {
            navMeshAgent.enabled = true;
            navMeshAgent.areaMask = walkableAreaMask; // Restrict agent to water only
            if (!hasHomeAnchor)
            {
                SetHomeAnchor(transform.position, transform.rotation);
            }

            StartCoroutine(RoamRoutine());
        }
        else
        {
            navMeshAgent.enabled = false;
        }
    }

    private IEnumerator RoamRoutine()
    {
        while (true)
        {
            if (IsServer && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                if (!isReturningHome && ShouldTriggerLeashReset())
                {
                    isReturningHome = true;
                    isRoaming = false;
                    LeashExceeded?.Invoke();
                }

                if (isReturningHome)
                {
                    EnsureReturnHomePath();
                    if (HasReachedHome())
                    {
                        navMeshAgent.ResetPath();
                        transform.rotation = homeRotation;
                        isReturningHome = false;
                        isRoaming = true;
                    }
                }
                else if (isRoaming && !navMeshAgent.pathPending)
                {
                    // If we reached the destination or don't have one
                    if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
                    {
                        if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude == 0f)
                        {
                            yield return new WaitForSeconds(waitTime);
                            if (!isReturningHome)
                            {
                                MoveToRandomPoint();
                            }
                        }
                    }
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
    }

    private void MoveToRandomPoint()
    {
        Vector3 point = PickRandomPoint();
        navMeshAgent.SetDestination(point);
    }

    private Vector3 PickRandomPoint()
    {
        Vector3 center = hasHomeAnchor ? homePosition : transform.position;
        float sampleDistance = Mathf.Max(0.1f, roamNavMeshSampleDistance);
        for (int attempt = 0; attempt < MaxRoamSampleAttempts; attempt++)
        {
            Vector2 planarOffset = UnityEngine.Random.insideUnitCircle * roamRadius;
            Vector3 randomDirection = center + new Vector3(planarOffset.x, 0f, planarOffset.y);
            randomDirection.y = center.y;

            NavMeshHit hit;
            // Only sample from walkable areas (water), not the island
            if (NavMesh.SamplePosition(randomDirection, out hit, sampleDistance, walkableAreaMask) &&
                IsPointOnWaterSurface(hit.position))
            {
                return hit.position;
            }
        }
        
        return center;
    }

    private bool IsPointOnWaterSurface(Vector3 point)
    {
        if (waterLayer < 0)
        {
            return true;
        }

        Vector3 origin = point + Vector3.up * WaterSurfaceProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            waterProbeHits,
            WaterSurfaceProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
        {
            return false;
        }

        int nearestHitIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            if (waterProbeHits[i].collider == null)
            {
                continue;
            }

            float distance = waterProbeHits[i].distance;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestHitIndex = i;
            }
        }

        if (nearestHitIndex < 0)
        {
            return false;
        }

        return waterProbeHits[nearestHitIndex].collider.gameObject.layer == waterLayer;
    }

    public void StopRoaming()
    {
        isRoaming = false;
        isReturningHome = false;
        if (IsServer && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.ResetPath(); // Stop moving
        }
    }

    public void StartRoaming()
    {
        if (isReturningHome)
        {
            return;
        }

        isRoaming = true;
    }

    public void ApplyRoamingSettings(float newRoamRadius, float newWaitTime)
    {
        roamRadius = Mathf.Max(0f, newRoamRadius);
        waitTime = Mathf.Max(0f, newWaitTime);
    }

    public void ApplyLeashSettings(float newLeashRadius, float newHomeArrivalDistance)
    {
        leashRadius = Mathf.Max(0f, newLeashRadius);
        homeArrivalDistance = Mathf.Max(0f, newHomeArrivalDistance);
    }

    public void SetHomeAnchor(Vector3 newHomePosition, Quaternion newHomeRotation)
    {
        homePosition = newHomePosition;
        homeRotation = newHomeRotation;
        hasHomeAnchor = true;
    }

    public void ReturnHome()
    {
        if (!hasHomeAnchor)
        {
            return;
        }

        isRoaming = false;
        isReturningHome = true;
        EnsureReturnHomePath();
    }

    public void SnapToHome()
    {
        if (!hasHomeAnchor)
        {
            return;
        }

        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.Warp(homePosition);
        }
        else
        {
            transform.position = homePosition;
        }

        transform.rotation = homeRotation;
    }

    private bool ShouldTriggerLeashReset()
    {
        if (!hasHomeAnchor || leashRadius <= 0f)
        {
            return false;
        }

        float sqrDistanceFromHome = (transform.position - homePosition).sqrMagnitude;
        return sqrDistanceFromHome > leashRadius * leashRadius;
    }

    private void EnsureReturnHomePath()
    {
        if (!hasHomeAnchor)
        {
            return;
        }

        if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);
            return;
        }

        if (!navMeshAgent.hasPath || (navMeshAgent.destination - homePosition).sqrMagnitude > 0.01f)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.SetDestination(homePosition);
        }
    }

    private bool HasReachedHome()
    {
        if (!hasHomeAnchor)
        {
            return true;
        }

        float requiredDistance = Mathf.Max(navMeshAgent != null ? navMeshAgent.stoppingDistance : 0f, homeArrivalDistance);
        if (requiredDistance <= 0f)
        {
            requiredDistance = 0.5f;
        }

        return (transform.position - homePosition).sqrMagnitude <= requiredDistance * requiredDistance;
    }
}
