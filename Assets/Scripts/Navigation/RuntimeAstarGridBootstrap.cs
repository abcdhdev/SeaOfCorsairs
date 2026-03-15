using System.Collections;
using System.Collections.Generic;
using Pathfinding;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RuntimeAstarGridBootstrap : MonoBehaviour
{
    private const string RuntimeObjectName = "[AstarGridNavigation]";
    private const string PreferredNavMeshSurfaceName = "NavMesh Surface";
    private const string WaterLayerName = "Water";
    private const float DefaultWorldSpan = 512f;
    private const float DefaultNodeSize = 4f;
    private const float WaterProbeHeight = 100f;
    private const float WaterProbeDistance = 300f;
    private const float WaterSurfaceTolerance = 1.5f;
    private const float ScanPadding = 8f;

    private static RuntimeAstarGridBootstrap instance;

    private readonly RaycastHit[] waterProbeHits = new RaycastHit[8];
    private readonly List<Renderer> waterSurfaceRenderers = new();
    private Coroutine rebuildCoroutine;
    private AstarPath astarPath;
    private int waterLayer = -1;
    private float cachedWaterSurfaceY = float.NaN;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        instance = runtimeObject.AddComponent<RuntimeAstarGridBootstrap>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        QueueRebuild();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (rebuildCoroutine != null)
        {
            StopCoroutine(rebuildCoroutine);
        }

        rebuildCoroutine = StartCoroutine(RebuildGraphAfterSceneSettles());
    }

    private IEnumerator RebuildGraphAfterSceneSettles()
    {
        yield return null;

        waterLayer = LayerMask.NameToLayer(WaterLayerName);
        CacheWaterSurfaceData();

        Bounds worldBounds = ResolveWorldBounds();
        float graphHeight = ResolveGraphHeight(worldBounds);

        EnsureAstarPath();
        ConfigureGridGraph(worldBounds, graphHeight);
        astarPath.Scan();
        RestrictGridGraphToWater();
        rebuildCoroutine = null;
    }

    private void EnsureAstarPath()
    {
        if (astarPath == null)
        {
            astarPath = GetComponent<AstarPath>();
        }

        if (astarPath == null)
        {
            astarPath = gameObject.AddComponent<AstarPath>();
        }

        if (astarPath.data == null)
        {
            Debug.LogError("RuntimeAstarGridBootstrap: AstarPath was created without initialized graph data.");
            return;
        }

        if (astarPath.data.gridGraph == null)
        {
            GridGraph gridGraph = astarPath.data.AddGraph<GridGraph>();
            gridGraph.name = "Sea Grid Graph";
        }
    }

    private void ConfigureGridGraph(Bounds worldBounds, float graphHeight)
    {
        GridGraph gridGraph = astarPath.data.gridGraph;
        float nodeSize = DefaultNodeSize;
        float width = Mathf.Max(nodeSize, worldBounds.size.x + ScanPadding * 2f);
        float depth = Mathf.Max(nodeSize, worldBounds.size.z + ScanPadding * 2f);
        int gridWidth = Mathf.Max(1, Mathf.CeilToInt(width / nodeSize));
        int gridDepth = Mathf.Max(1, Mathf.CeilToInt(depth / nodeSize));

        gridGraph.rotation = Vector3.zero;
        gridGraph.center = new Vector3(worldBounds.center.x, graphHeight, worldBounds.center.z);
        gridGraph.collision.use2D = false;
        gridGraph.collision.collisionCheck = false;
        gridGraph.collision.heightCheck = false;
        gridGraph.SetDimensions(gridWidth, gridDepth, nodeSize);
    }

    private void RestrictGridGraphToWater()
    {
        GridGraph gridGraph = astarPath.data.gridGraph;
        if (gridGraph == null)
        {
            return;
        }

        astarPath.AddWorkItem(new AstarWorkItem(_ =>
        {
            gridGraph.GetNodes(node =>
            {
                if (node == null)
                {
                    return;
                }

                Vector3 worldPoint = (Vector3)node.position;
                node.Walkable = IsPointOnWaterSurface(worldPoint);
            });

            gridGraph.RecalculateAllConnections();
        }));

        astarPath.FlushWorkItems();
    }

    private Bounds ResolveWorldBounds()
    {
        if (TryResolveBoundsFromNavMeshSurface(out Bounds bounds) ||
            TryResolveBoundsFromTerrain(out bounds) ||
            TryResolveBoundsFromRenderers(out bounds))
        {
            return bounds;
        }

        return new Bounds(Vector3.zero, new Vector3(DefaultWorldSpan, 64f, DefaultWorldSpan));
    }

    private bool TryResolveBoundsFromNavMeshSurface(out Bounds bounds)
    {
        bounds = default;
        NavMeshSurface[] surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces == null || surfaces.Length == 0)
        {
            return false;
        }

        NavMeshSurface fallback = null;
        for (int i = 0; i < surfaces.Length; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null || !surface.isActiveAndEnabled || surface.collectObjects != CollectObjects.Volume)
            {
                continue;
            }

            fallback ??= surface;
            if (string.Equals(surface.gameObject.name, PreferredNavMeshSurfaceName, System.StringComparison.OrdinalIgnoreCase))
            {
                fallback = surface;
                break;
            }
        }

        if (fallback == null)
        {
            return false;
        }

        Bounds localBounds = new Bounds(fallback.center, fallback.size);
        Matrix4x4 localToWorld = Matrix4x4.TRS(fallback.transform.position, fallback.transform.rotation, Vector3.one);
        bounds = GetWorldBounds(localToWorld, localBounds);
        return bounds.size.x > 0.01f && bounds.size.z > 0.01f;
    }

    private static bool TryResolveBoundsFromTerrain(out Bounds bounds)
    {
        bounds = default;
        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null || !terrain.isActiveAndEnabled)
            {
                continue;
            }

            Bounds terrainBounds = new Bounds(
                terrain.transform.position + terrain.terrainData.size * 0.5f,
                terrain.terrainData.size);

            if (!found)
            {
                bounds = terrainBounds;
                found = true;
                continue;
            }

            bounds.Encapsulate(terrainBounds);
        }

        return found;
    }

    private static bool TryResolveBoundsFromRenderers(out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return found;
    }

    private void CacheWaterSurfaceData()
    {
        waterSurfaceRenderers.Clear();
        cachedWaterSurfaceY = float.NaN;

        if (waterLayer < 0)
        {
            return;
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        float largestSurfaceArea = -1f;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (renderer.gameObject.layer != waterLayer)
            {
                continue;
            }

            waterSurfaceRenderers.Add(renderer);

            Bounds bounds = renderer.bounds;
            float surfaceArea = bounds.size.x * bounds.size.z;
            if (surfaceArea > largestSurfaceArea)
            {
                largestSurfaceArea = surfaceArea;
                cachedWaterSurfaceY = bounds.center.y;
            }
        }
    }

    private float ResolveGraphHeight(Bounds worldBounds)
    {
        if (!float.IsNaN(cachedWaterSurfaceY))
        {
            return cachedWaterSurfaceY;
        }

        return worldBounds.center.y;
    }

    private bool IsPointOnWaterSurface(Vector3 point)
    {
        if (waterLayer < 0)
        {
            return true;
        }

        Vector3 origin = point + Vector3.up * WaterProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            waterProbeHits,
            WaterProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount > 0)
        {
            int nearestHitIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = waterProbeHits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (waterProbeHits[i].distance < nearestDistance)
                {
                    nearestDistance = waterProbeHits[i].distance;
                    nearestHitIndex = i;
                }
            }

            if (nearestHitIndex >= 0 &&
                waterProbeHits[nearestHitIndex].collider != null &&
                waterProbeHits[nearestHitIndex].collider.gameObject.layer == waterLayer)
            {
                return true;
            }
        }

        if (TryGetWaterSurfaceY(point, out float waterSurfaceY))
        {
            return Mathf.Abs(point.y - waterSurfaceY) <= WaterSurfaceTolerance;
        }

        return false;
    }

    private bool TryGetWaterSurfaceY(Vector3 point, out float waterSurfaceY)
    {
        waterSurfaceY = default;

        if (waterLayer < 0)
        {
            return false;
        }

        float closestVerticalDelta = float.MaxValue;
        bool found = false;
        for (int i = 0; i < waterSurfaceRenderers.Count; i++)
        {
            Renderer renderer = waterSurfaceRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (!IsPointWithinBounds(point, bounds))
            {
                continue;
            }

            float candidateY = bounds.center.y;
            float verticalDelta = Mathf.Abs(point.y - candidateY);
            if (!found || verticalDelta < closestVerticalDelta)
            {
                closestVerticalDelta = verticalDelta;
                waterSurfaceY = candidateY;
                found = true;
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

    private static bool IsPointWithinBounds(Vector3 point, Bounds bounds)
    {
        const float Padding = 0.5f;
        return point.x >= bounds.min.x - Padding &&
               point.x <= bounds.max.x + Padding &&
               point.z >= bounds.min.z - Padding &&
               point.z <= bounds.max.z + Padding;
    }

    private static Bounds GetWorldBounds(Matrix4x4 localToWorld, Bounds bounds)
    {
        Vector3 axisX = Abs(localToWorld.MultiplyVector(Vector3.right));
        Vector3 axisY = Abs(localToWorld.MultiplyVector(Vector3.up));
        Vector3 axisZ = Abs(localToWorld.MultiplyVector(Vector3.forward));
        Vector3 worldCenter = localToWorld.MultiplyPoint(bounds.center);
        Vector3 worldSize = axisX * bounds.size.x + axisY * bounds.size.y + axisZ * bounds.size.z;
        return new Bounds(worldCenter, worldSize);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
