using System.Collections;
using Pathfinding;
using Pathfinding.Graphs.Grid;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-10010)]
[DisallowMultipleComponent]
[RequireComponent(typeof(AstarPath))]
public sealed class RuntimeAstarGridBootstrap : MonoBehaviour
{
    private const string WaterLayerName = "Water";
    private const string PreferredWaterSurfaceName = "Water Surface";
    private const float DefaultWorldSpan = 512f;
    private const float DefaultWorldHeight = 64f;
    private const float MinimumNodeSize = 0.25f;

    private static RuntimeAstarGridBootstrap instance;

    [Header("Grid")]
    [SerializeField] private float nodeSize = 1f;
    [SerializeField] private float scanPadding = 8f;
    [SerializeField] private Vector3 graphRotation = new(0f, 45f, 0f);

    [Header("Bake")]
    [SerializeField] private LayerMask waterHeightMask;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float heightRayLength = 100f;
    [SerializeField] private bool useThickRaycast = true;
    [SerializeField] private float thickRaycastDiameter = 1f;
    [SerializeField] private ColliderType collisionShape = ColliderType.Sphere;
    [SerializeField] private float collisionDiameter = 0.85f;
    [SerializeField] private float collisionHeight = 2f;
    [SerializeField] private float collisionOffset = 0.25f;

    private Coroutine rebuildCoroutine;
    private AstarPath astarPath;
    private int waterLayer = -1;
    private readonly System.Collections.Generic.List<Renderer> waterSurfaceRenderers = new();

    public static bool TryGetSourceWorldBounds(out Bounds bounds)
    {
        RuntimeAstarGridBootstrap bootstrap = instance != null
            ? instance
            : FindFirstObjectByType<RuntimeAstarGridBootstrap>();

        if (bootstrap == null)
        {
            bounds = default;
            return false;
        }

        bootstrap.waterLayer = LayerMask.NameToLayer(WaterLayerName);
        return TryResolveBoundsFromTerrain(out bounds) || bootstrap.TryResolveBoundsFromRenderers(out bounds);
    }

    private void Reset()
    {
        ApplyDefaultLayerMasks();
        EnsureAstarPathReference();
    }

    private void Awake()
    {
        EnsureAstarPathReference();
        ApplyDefaultLayerMasksIfNeeded();
    }

    private void OnValidate()
    {
        nodeSize = Mathf.Max(MinimumNodeSize, nodeSize);
        scanPadding = Mathf.Max(0f, scanPadding);
        heightRayLength = Mathf.Max(0f, heightRayLength);
        thickRaycastDiameter = Mathf.Max(0f, thickRaycastDiameter);
        collisionDiameter = Mathf.Max(0f, collisionDiameter);
        collisionHeight = Mathf.Max(0f, collisionHeight);
        collisionOffset = Mathf.Max(0f, collisionOffset);

        EnsureAstarPathReference();
        ApplyDefaultLayerMasksIfNeeded();
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                "RuntimeAstarGridBootstrap: Multiple scene bootstrap instances found. Disabling the duplicate.",
                this);
            enabled = false;
            return;
        }

        instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        QueueRebuild();
    }

    private void OnDisable()
    {
        if (instance == this)
        {
            instance = null;
        }

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
        RestrictGridGraphToWaterSurface();
        rebuildCoroutine = null;
    }

    private void EnsureAstarPathReference()
    {
        if (astarPath == null)
        {
            astarPath = GetComponent<AstarPath>();
        }

        if (astarPath != null)
        {
            astarPath.scanOnStartup = false;
        }
    }

    private void ApplyDefaultLayerMasksIfNeeded()
    {
        if (waterHeightMask.value == 0 || obstacleMask.value == 0)
        {
            ApplyDefaultLayerMasks();
        }
    }

    private void ApplyDefaultLayerMasks()
    {
        waterHeightMask = LayerMask.GetMask(WaterLayerName);
        obstacleMask = ~LayerMask.GetMask(WaterLayerName, "boat", "NPC", "UI", "Ignore Raycast");
    }

    private void EnsureAstarPath()
    {
        EnsureAstarPathReference();
        if (astarPath == null)
        {
            astarPath = gameObject.AddComponent<AstarPath>();
            astarPath.scanOnStartup = false;
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
        float clampedNodeSize = Mathf.Max(MinimumNodeSize, nodeSize);
        Quaternion rotation = Quaternion.Euler(graphRotation);
        Vector2 graphSize = CalculateGraphSize(worldBounds, rotation, scanPadding);
        int gridWidth = Mathf.Max(1, Mathf.CeilToInt(graphSize.x / clampedNodeSize));
        int gridDepth = Mathf.Max(1, Mathf.CeilToInt(graphSize.y / clampedNodeSize));

        gridGraph.rotation = graphRotation;
        gridGraph.center = new Vector3(worldBounds.center.x, graphHeight, worldBounds.center.z);
        gridGraph.neighbours = NumNeighbours.Four;
        gridGraph.cutCorners = false;
        gridGraph.uniformEdgeCosts = true;
        gridGraph.SetDimensions(gridWidth, gridDepth, clampedNodeSize);

        gridGraph.collision.use2D = false;
        gridGraph.collision.heightCheck = false;
        gridGraph.collision.collisionCheck = true;
        gridGraph.collision.unwalkableWhenNoGround = true;
        gridGraph.collision.type = collisionShape;
        gridGraph.collision.fromHeight = heightRayLength;
        gridGraph.collision.thickRaycast = useThickRaycast;
        gridGraph.collision.thickRaycastDiameter = Mathf.Max(0f, thickRaycastDiameter);
        gridGraph.collision.heightMask = waterHeightMask.value != 0 ? waterHeightMask : Physics.DefaultRaycastLayers;
        gridGraph.collision.mask = obstacleMask;
        gridGraph.collision.diameter = Mathf.Max(0f, collisionDiameter);
        gridGraph.collision.height = Mathf.Max(0f, collisionHeight);
        gridGraph.collision.collisionOffset = Mathf.Max(0f, collisionOffset);
    }

    private void RestrictGridGraphToWaterSurface()
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
                node.Walkable = node.Walkable && IsPointWithinWaterSurface(worldPoint);
            });

            gridGraph.RecalculateAllConnections();
        }));

        astarPath.FlushWorkItems();
    }

    private static Vector2 CalculateGraphSize(Bounds worldBounds, Quaternion rotation, float padding)
    {
        Quaternion inverseRotation = Quaternion.Inverse(rotation);
        Vector3 center = worldBounds.center;

        Vector3[] corners =
        {
            new(worldBounds.min.x, center.y, worldBounds.min.z),
            new(worldBounds.min.x, center.y, worldBounds.max.z),
            new(worldBounds.max.x, center.y, worldBounds.min.z),
            new(worldBounds.max.x, center.y, worldBounds.max.z)
        };

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 localCorner = inverseRotation * (corners[i] - center);
            minX = Mathf.Min(minX, localCorner.x);
            maxX = Mathf.Max(maxX, localCorner.x);
            minZ = Mathf.Min(minZ, localCorner.z);
            maxZ = Mathf.Max(maxZ, localCorner.z);
        }

        float width = Mathf.Max(MinimumNodeSize, (maxX - minX) + padding * 2f);
        float depth = Mathf.Max(MinimumNodeSize, (maxZ - minZ) + padding * 2f);
        return new Vector2(width, depth);
    }

    private Bounds ResolveWorldBounds()
    {
        if (TryResolveBoundsFromTerrain(out Bounds bounds) ||
            TryResolveBoundsFromRenderers(out bounds))
        {
            return bounds;
        }

        return new Bounds(Vector3.zero, new Vector3(DefaultWorldSpan, DefaultWorldHeight, DefaultWorldSpan));
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

    private bool TryResolveBoundsFromRenderers(out Bounds bounds)
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

            if (waterLayer >= 0 && renderer.gameObject.layer == waterLayer)
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

    private float ResolveGraphHeight(Bounds worldBounds)
    {
        if (TryResolveWaterSurfaceHeight(out float waterSurfaceHeight))
        {
            return waterSurfaceHeight;
        }

        return worldBounds.center.y;
    }

    private void CacheWaterSurfaceData()
    {
        waterSurfaceRenderers.Clear();
        if (waterLayer < 0)
        {
            return;
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (renderer.gameObject.layer == waterLayer)
            {
                waterSurfaceRenderers.Add(renderer);
            }
        }
    }

    private bool TryResolveWaterSurfaceHeight(out float waterSurfaceHeight)
    {
        waterSurfaceHeight = default;
        if (waterLayer < 0)
        {
            return false;
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        Renderer preferredRenderer = null;
        Renderer fallbackRenderer = null;
        float preferredArea = -1f;
        float fallbackArea = -1f;

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

            float area = renderer.bounds.size.x * renderer.bounds.size.z;
            if (area <= 0f)
            {
                continue;
            }

            if (area > fallbackArea)
            {
                fallbackArea = area;
                fallbackRenderer = renderer;
            }

            if (string.Equals(renderer.gameObject.name, PreferredWaterSurfaceName, System.StringComparison.OrdinalIgnoreCase) &&
                area > preferredArea)
            {
                preferredArea = area;
                preferredRenderer = renderer;
            }
        }

        Renderer selectedRenderer = preferredRenderer != null ? preferredRenderer : fallbackRenderer;
        if (selectedRenderer == null)
        {
            return false;
        }

        waterSurfaceHeight = selectedRenderer.bounds.center.y;
        return true;
    }

    private bool IsPointWithinWaterSurface(Vector3 point)
    {
        if (waterSurfaceRenderers.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < waterSurfaceRenderers.Count; i++)
        {
            Renderer renderer = waterSurfaceRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (point.x >= bounds.min.x &&
                point.x <= bounds.max.x &&
                point.z >= bounds.min.z &&
                point.z <= bounds.max.z)
            {
                return true;
            }
        }

        return false;
    }
}
