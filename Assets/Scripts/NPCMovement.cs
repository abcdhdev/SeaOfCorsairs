using System;
using System.Collections;
using Pathfinding;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(AILerp))]
public class NPCMovement : NetworkBehaviour
{
    private const int MaxRoamSampleAttempts = 10;

    [SerializeField] private float roamRadius = 10f;
    [SerializeField] private float waitTime = 3f;
    [SerializeField, Min(0.1f)] private float graphSnapDistance = 12f;
    [SerializeField, Min(0f)] private float leashRadius = 300f;
    [SerializeField, Min(0f)] private float homeArrivalDistance = 8f;

    private AILerp aiLerp;
    private PlayerDirectionSpriteController spriteController;
    private Coroutine roamCoroutine;
    private Coroutine waitForNavigationCoroutine;
    private bool isRoaming = true;
    private bool isReturningHome;
    private Vector3 homePosition;
    private Quaternion homeRotation = Quaternion.identity;
    private bool hasHomeAnchor;
    private Vector3 previousPosition;
    private Vector3 lastValidDirection;

    public event Action LeashExceeded = delegate { };

    private void Awake()
    {
        EnsureComponents();
        aiLerp.enabled = false;
        aiLerp.enableRotation = false;
        homePosition = transform.position;
        homeRotation = transform.rotation;
        hasHomeAnchor = true;
        previousPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (waitForNavigationCoroutine != null)
        {
            StopCoroutine(waitForNavigationCoroutine);
        }

        if (roamCoroutine != null)
        {
            StopCoroutine(roamCoroutine);
            roamCoroutine = null;
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

        if (roamCoroutine != null)
        {
            StopCoroutine(roamCoroutine);
            roamCoroutine = null;
        }

        base.OnNetworkDespawn();
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
        roamCoroutine = StartCoroutine(RoamRoutine());
        MoveToRandomPoint();
        waitForNavigationCoroutine = null;
    }

    private IEnumerator RoamRoutine()
    {
        while (IsServer && IsSpawned)
        {
            if (aiLerp.enabled)
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
                        StopMovement();
                        transform.rotation = homeRotation;
                        isReturningHome = false;
                        isRoaming = true;
                    }
                }
                else if (isRoaming && !aiLerp.pathPending && (aiLerp.reachedEndOfPath || !aiLerp.hasPath))
                {
                    yield return new WaitForSeconds(waitTime);
                    if (!isReturningHome)
                    {
                        MoveToRandomPoint();
                    }
                }
            }

            yield return new WaitForSeconds(0.25f);
        }

        roamCoroutine = null;
    }

    private void MoveToRandomPoint()
    {
        Vector3 center = hasHomeAnchor ? homePosition : transform.position;
        if (!AstarNavigationUtility.TryGetRandomWalkablePoint(
                center,
                roamRadius,
                MaxRoamSampleAttempts,
                graphSnapDistance,
                out Vector3 point))
        {
            point = center;
        }

        aiLerp.destination = point;
        aiLerp.isStopped = false;
        aiLerp.simulateMovement = true;
        aiLerp.SearchPath();
    }

    public void StopRoaming()
    {
        isRoaming = false;
        isReturningHome = false;
        StopMovement();
    }

    public void StartRoaming()
    {
        if (isReturningHome)
        {
            return;
        }

        isRoaming = true;
    }

    public void ApplyMovementSettings(float movementSpeed)
    {
        if (!EnsureComponents())
        {
            return;
        }

        aiLerp.speed = Mathf.Max(0f, movementSpeed);
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

        EnsureComponents();
        transform.SetPositionAndRotation(homePosition, homeRotation);
        if (aiLerp != null)
        {
            aiLerp.destination = homePosition;
            aiLerp.Teleport(homePosition, clearPath: true);
        }

        previousPosition = homePosition;
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

        aiLerp.destination = homePosition;
        aiLerp.isStopped = false;
        aiLerp.simulateMovement = true;
        aiLerp.SearchPath();
    }

    private bool HasReachedHome()
    {
        if (!hasHomeAnchor)
        {
            return true;
        }

        float requiredDistance = homeArrivalDistance <= 0f ? 0.5f : homeArrivalDistance;
        return (transform.position - homePosition).sqrMagnitude <= requiredDistance * requiredDistance;
    }

    private void StopMovement()
    {
        if (!IsServer || !EnsureComponents())
        {
            return;
        }

        aiLerp.simulateMovement = false;
        aiLerp.isStopped = true;
        aiLerp.destination = transform.position;
        aiLerp.SearchPath();
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

    private bool EnsureComponents()
    {
        aiLerp ??= GetComponent<AILerp>();
        spriteController ??= GetComponentInChildren<PlayerDirectionSpriteController>(true);
        return aiLerp != null;
    }
}
