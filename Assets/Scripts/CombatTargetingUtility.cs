using System.Collections.Generic;
using UnityEngine;

public static class CombatTargetingUtility
{
    private static readonly HashSet<ITargetable> RegisteredTargetables = new();
    private static readonly List<ITargetable> TargetableSnapshot = new(32);

    public static void Register(ITargetable targetable)
    {
        if (targetable == null)
        {
            return;
        }

        RegisteredTargetables.Add(targetable);
    }

    public static void Unregister(ITargetable targetable)
    {
        if (targetable == null)
        {
            return;
        }

        RegisteredTargetables.Remove(targetable);
    }

    public static bool TryFindTargetAlongRay(
        Ray ray,
        GameObject requester,
        float maxDistance,
        float minimumSelectionRadius,
        out GameObject target)
    {
        target = null;
        float bestDistance = maxDistance;

        TargetableSnapshot.Clear();
        foreach (ITargetable registeredTargetable in RegisteredTargetables)
        {
            TargetableSnapshot.Add(registeredTargetable);
        }

        for (int index = 0; index < TargetableSnapshot.Count; index++)
        {
            ITargetable targetable = TargetableSnapshot[index];
            if (targetable == null || !targetable.CanBeTargeted)
            {
                continue;
            }

            GameObject candidate = targetable.TargetGameObject;
            if (candidate == null || candidate == requester)
            {
                continue;
            }

            if (!TryProjectTargetAlongRay(ray, candidate, bestDistance, minimumSelectionRadius, out float candidateDistance))
            {
                continue;
            }

            bestDistance = candidateDistance;
            target = candidate;
        }

        TargetableSnapshot.Clear();
        return target != null;
    }

    public static bool IsTargetableCollider(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return false;
        }

        MonoBehaviour[] parentBehaviours = hitCollider.GetComponentsInParent<MonoBehaviour>(true);
        for (int index = 0; index < parentBehaviours.Length; index++)
        {
            if (parentBehaviours[index] is ITargetable targetable && targetable.CanBeTargeted)
            {
                return targetable.TargetGameObject != null;
            }
        }

        return false;
    }

    public static bool TryGetTargetable(GameObject gameObject, out ITargetable targetable)
    {
        targetable = null;
        if (gameObject == null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = gameObject.GetComponents<MonoBehaviour>();
        for (int index = 0; index < behaviours.Length; index++)
        {
            if (behaviours[index] is ITargetable resolvedTargetable)
            {
                targetable = resolvedTargetable;
                return true;
            }
        }

        return false;
    }

    public static Vector3 GetAimPoint(GameObject target)
    {
        if (TryGetTargetBounds(target, out Bounds targetBounds))
        {
            return targetBounds.center;
        }

        return target != null ? target.transform.position : Vector3.zero;
    }

    public static float GetSelectionRadius(GameObject target, float minimumSelectionRadius)
    {
        if (TryGetTargetBounds(target, out Bounds targetBounds))
        {
            float targetFootprintRadius = Mathf.Max(
                targetBounds.extents.x,
                targetBounds.extents.y,
                targetBounds.extents.z);

            return Mathf.Max(minimumSelectionRadius, targetFootprintRadius);
        }

        return Mathf.Max(minimumSelectionRadius, 1f);
    }

    public static bool TryGetTargetBounds(GameObject target, out Bounds bounds)
    {
        bounds = default;
        if (target == null)
        {
            return false;
        }

        bool hasBounds = false;
        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        for (int index = 0; index < colliders.Length; index++)
        {
            Collider collider = colliders[index];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static bool TryProjectTargetAlongRay(
        Ray ray,
        GameObject target,
        float maxDistance,
        float minimumSelectionRadius,
        out float distanceAlongRay)
    {
        distanceAlongRay = 0f;
        if (target == null || maxDistance <= 0f)
        {
            return false;
        }

        if (!TryGetTargetBounds(target, out Bounds targetBounds))
        {
            return false;
        }

        // Use one strict rule for every target: the click ray must intersect the target's
        // rendered/collider bounds (with only a small padding for playability).
        Bounds expandedBounds = targetBounds;
        float selectionPadding = Mathf.Max(0.35f, minimumSelectionRadius * 0.2f);
        expandedBounds.Expand(selectionPadding * 2f);

        if (!expandedBounds.IntersectRay(ray, out float hitDistance) || hitDistance > maxDistance)
        {
            return false;
        }

        distanceAlongRay = hitDistance;
        return true;
    }
}
