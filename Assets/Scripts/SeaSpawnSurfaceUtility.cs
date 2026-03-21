using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class SeaSpawnSurfaceUtility
{
    private const string WalkableAreaName = "Walkable";
    private const string WaterLayerName = "Water";
    private const float WaterSurfaceProbeHeight = 100f;
    private const float WaterSurfaceProbeDistance = 300f;
    private const float WaterSurfaceYTolerance = 1.5f;
    private const float WaterBoundsPadding = 0.5f;

    private static readonly RaycastHit[] WaterProbeHits = new RaycastHit[8];
    private static readonly List<Renderer> WaterSurfaceRenderers = new();

    private static bool waterSurfaceDataCached;
    private static int cachedWaterLayer = int.MinValue;
    private static float cachedWaterSurfaceY = float.NaN;

    public static int ResolveWalkableAreaMask()
    {
        int walkableArea = NavMesh.GetAreaFromName(WalkableAreaName);
        return walkableArea >= 0 ? 1 << walkableArea : NavMesh.AllAreas;
    }

    public static int ResolveWaterLayer()
    {
        return LayerMask.NameToLayer(WaterLayerName);
    }

    public static bool TryGetRandomWaterNavMeshPosition(
        Vector3 center,
        float radius,
        float navMeshSampleDistance,
        int walkableAreaMask,
        int maxAttempts,
        out Vector3 spawnPosition)
    {
        float sampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
        int attempts = Mathf.Max(1, maxAttempts);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 planarOffset = Random.insideUnitCircle * Mathf.Max(0f, radius);
            Vector3 samplePoint = center + new Vector3(planarOffset.x, 0f, planarOffset.y);

            if (TryGetWaterSurfaceY(samplePoint, out float waterSurfaceY))
            {
                samplePoint.y = waterSurfaceY;
            }
            else
            {
                samplePoint.y = center.y;
            }

            if (NavMesh.SamplePosition(samplePoint, out NavMeshHit hit, sampleDistance, walkableAreaMask) &&
                IsPointOnWaterSurface(hit.position))
            {
                spawnPosition = hit.position;
                return true;
            }
        }

        spawnPosition = default;
        return false;
    }

    public static bool TrySampleWaterNavMeshPosition(
        Vector3 desiredPosition,
        float navMeshSampleDistance,
        int walkableAreaMask,
        out Vector3 sampledPosition)
    {
        Vector3 sampleOrigin = desiredPosition;
        float sampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);

        if (TryGetWaterSurfaceY(desiredPosition, out float waterSurfaceY))
        {
            sampleOrigin.y = waterSurfaceY;
            sampleDistance = Mathf.Max(sampleDistance, Mathf.Abs(desiredPosition.y - waterSurfaceY) + 0.5f);
        }

        if (NavMesh.SamplePosition(sampleOrigin, out NavMeshHit hit, sampleDistance, walkableAreaMask) &&
            IsPointOnWaterSurface(hit.position))
        {
            sampledPosition = hit.position;
            return true;
        }

        sampledPosition = default;
        return false;
    }

    public static void ApplyWaterlineOffset(GameObject target, Vector3 navMeshSpawnPosition, float additionalOffset)
    {
        if (target == null)
        {
            return;
        }

        float waterSurfaceY = navMeshSpawnPosition.y;
        if (TryGetWaterSurfaceY(navMeshSpawnPosition, out float resolvedWaterSurfaceY))
        {
            waterSurfaceY = resolvedWaterSurfaceY;
        }

        if (target.TryGetComponent(out NavMeshAgent navMeshAgent))
        {
            float navMeshToWaterDelta = waterSurfaceY - navMeshSpawnPosition.y;
            float desiredBaseOffset = navMeshAgent.baseOffset + navMeshToWaterDelta + additionalOffset;
            navMeshAgent.baseOffset = Mathf.Max(navMeshAgent.baseOffset, desiredBaseOffset);

            Vector3 correctedPosition = navMeshSpawnPosition;
            correctedPosition.y = navMeshSpawnPosition.y + navMeshAgent.baseOffset;
            target.transform.position = correctedPosition;
            return;
        }

        Vector3 position = navMeshSpawnPosition;
        position.y = waterSurfaceY + additionalOffset;
        target.transform.position = position;
    }

    private static bool TryGetWaterSurfaceY(Vector3 point, out float waterSurfaceY)
    {
        waterSurfaceY = default;
        int waterLayer = ResolveWaterLayer();
        if (waterLayer < 0)
        {
            return false;
        }

        EnsureWaterSurfaceCache(waterLayer);

        float closestVerticalDelta = float.MaxValue;
        bool found = false;

        for (int index = 0; index < WaterSurfaceRenderers.Count; index++)
        {
            Renderer renderer = WaterSurfaceRenderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (!IsPointWithinWaterBounds(point, bounds))
            {
                continue;
            }

            float candidateY = bounds.center.y;
            float verticalDelta = Mathf.Abs(point.y - candidateY);
            if (!found || verticalDelta < closestVerticalDelta)
            {
                found = true;
                closestVerticalDelta = verticalDelta;
                waterSurfaceY = candidateY;
            }
        }

        if (found)
        {
            return true;
        }

        if (!float.IsNaN(cachedWaterSurfaceY))
        {
            waterSurfaceY = cachedWaterSurfaceY;
            return true;
        }

        return false;
    }

    private static void EnsureWaterSurfaceCache(int waterLayer)
    {
        if (waterSurfaceDataCached && cachedWaterLayer == waterLayer)
        {
            return;
        }

        waterSurfaceDataCached = true;
        cachedWaterLayer = waterLayer;
        cachedWaterSurfaceY = float.NaN;
        WaterSurfaceRenderers.Clear();

        Renderer[] sceneRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        float largestSurfaceArea = -1f;

        for (int index = 0; index < sceneRenderers.Length; index++)
        {
            Renderer renderer = sceneRenderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (renderer.gameObject.layer != waterLayer)
            {
                continue;
            }

            WaterSurfaceRenderers.Add(renderer);

            Bounds bounds = renderer.bounds;
            float surfaceArea = bounds.size.x * bounds.size.z;
            if (surfaceArea > largestSurfaceArea)
            {
                largestSurfaceArea = surfaceArea;
                cachedWaterSurfaceY = bounds.center.y;
            }
        }
    }

    private static bool IsPointOnWaterSurface(Vector3 point)
    {
        int waterLayer = ResolveWaterLayer();
        if (waterLayer < 0)
        {
            return true;
        }

        Vector3 origin = point + Vector3.up * WaterSurfaceProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            WaterProbeHits,
            WaterSurfaceProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount > 0)
        {
            int nearestHitIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int index = 0; index < hitCount; index++)
            {
                if (WaterProbeHits[index].collider == null)
                {
                    continue;
                }

                if (WaterProbeHits[index].distance < nearestDistance)
                {
                    nearestDistance = WaterProbeHits[index].distance;
                    nearestHitIndex = index;
                }
            }

            if (nearestHitIndex >= 0 &&
                WaterProbeHits[nearestHitIndex].collider != null &&
                WaterProbeHits[nearestHitIndex].collider.gameObject.layer == waterLayer)
            {
                return true;
            }
        }

        return TryGetWaterSurfaceY(point, out float waterSurfaceY) &&
               Mathf.Abs(point.y - waterSurfaceY) <= WaterSurfaceYTolerance;
    }

    private static bool IsPointWithinWaterBounds(Vector3 point, Bounds bounds)
    {
        return point.x >= bounds.min.x - WaterBoundsPadding &&
               point.x <= bounds.max.x + WaterBoundsPadding &&
               point.z >= bounds.min.z - WaterBoundsPadding &&
               point.z <= bounds.max.z + WaterBoundsPadding;
    }
}
