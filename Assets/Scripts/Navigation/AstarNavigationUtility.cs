using Pathfinding;
using UnityEngine;

public static class AstarNavigationUtility
{
    public static bool IsReady
    {
        get
        {
            AstarPath activePath = AstarPath.active;
            return activePath != null &&
                   activePath.data != null &&
                   activePath.data.gridGraph != null &&
                   !activePath.isScanning;
        }
    }

    public static GridGraph GetGridGraph()
    {
        return AstarPath.active != null && AstarPath.active.data != null
            ? AstarPath.active.data.gridGraph
            : null;
    }

    public static bool TryGetGridGraphBounds(out Bounds bounds)
    {
        GridGraph gridGraph = GetGridGraph();
        if (gridGraph == null)
        {
            bounds = default;
            return false;
        }

        float width = Mathf.Max(1, gridGraph.width) * Mathf.Max(0.01f, gridGraph.nodeSize);
        float depth = Mathf.Max(1, gridGraph.depth) * Mathf.Max(0.01f, gridGraph.nodeSize);
        bounds = new Bounds(gridGraph.center, new Vector3(width, 1f, depth));
        return bounds.size.x > 0.01f && bounds.size.z > 0.01f;
    }

    public static bool TryGetNearestWalkablePoint(Vector3 worldPoint, float maxDistance, out Vector3 walkablePoint)
    {
        walkablePoint = default;

        GridGraph gridGraph = GetGridGraph();
        AstarPath activePath = AstarPath.active;
        if (gridGraph == null || activePath == null)
        {
            return false;
        }

        NearestNodeConstraint constraint = NearestNodeConstraint.Walkable;
        constraint.graphMask = GraphMask.FromGraph(gridGraph);
        if (maxDistance > 0f)
        {
            constraint.maxDistance = maxDistance;
        }

        NNInfo nearest = activePath.GetNearest(worldPoint, constraint);
        if (nearest.node == null || !nearest.node.Walkable)
        {
            return false;
        }

        walkablePoint = (Vector3)nearest.node.position;
        return true;
    }

    public static bool TryGetRandomWalkablePoint(
        Vector3 center,
        float radius,
        int maxAttempts,
        float maxSnapDistance,
        out Vector3 walkablePoint)
    {
        walkablePoint = default;

        int attempts = Mathf.Max(1, maxAttempts);
        float clampedRadius = Mathf.Max(0f, radius);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 offset = clampedRadius > 0f
                ? Random.insideUnitCircle * clampedRadius
                : Vector2.zero;

            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (TryGetNearestWalkablePoint(candidate, maxSnapDistance, out walkablePoint))
            {
                return true;
            }
        }

        return TryGetNearestWalkablePoint(center, maxSnapDistance, out walkablePoint);
    }
}
