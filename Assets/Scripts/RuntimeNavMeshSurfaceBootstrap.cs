using Unity.AI.Navigation;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class RuntimeNavMeshSurfaceBootstrap : MonoBehaviour
{
    [SerializeField] private NavMeshSurface navMeshSurface;
    [SerializeField] private bool rebuildWhenTriangulationIsEmpty = true;
    [SerializeField] private bool logWhenBuilt = true;
    [SerializeField] private bool fitToLoadedWorldMapScenes = true;
    [SerializeField, Min(0f)] private float worldMapBoundsPadding = 0f;

    private bool loadedWorldMapBoundsApplied;

    private IEnumerator Start()
    {
        if (!Application.isPlaying)
        {
            yield break;
        }

        for (int attempt = 0; attempt < 600; attempt++)
        {
            EnsureNavMeshBuilt();
            if (HasNavMeshData() && (!fitToLoadedWorldMapScenes || (loadedWorldMapBoundsApplied && HasRegisteredAllCatalogScenes())))
            {
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            $"RuntimeNavMeshSurfaceBootstrap: '{gameObject.name}' did not finish runtime NavMesh setup after rebuild attempts.",
            this);
    }

    private void EnsureNavMeshBuilt()
    {
        if (navMeshSurface == null)
        {
            navMeshSurface = GetComponent<NavMeshSurface>();
        }

        if (navMeshSurface == null || !navMeshSurface.isActiveAndEnabled)
        {
            return;
        }

        bool surfaceBoundsChanged = FitSurfaceToLoadedWorldMaps();

        if (!ShouldBuildNavMesh(surfaceBoundsChanged))
        {
            return;
        }

        if (fitToLoadedWorldMapScenes)
        {
            WorldMapRuntimeVisibilityController.SetAllMapContentVisibleNow();
        }

        navMeshSurface.BuildNavMesh();

        if (fitToLoadedWorldMapScenes)
        {
            WorldMapRuntimeVisibilityController.RefreshVisibilityNowStatic();
        }

        if (!logWhenBuilt)
        {
            return;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        int triangleCount = triangulation.indices != null ? triangulation.indices.Length / 3 : 0;
        Debug.Log(
            $"RuntimeNavMeshSurfaceBootstrap: Built NavMesh on '{navMeshSurface.gameObject.name}' ({triangleCount} triangles).",
            navMeshSurface);
    }

    private bool FitSurfaceToLoadedWorldMaps()
    {
        if (!fitToLoadedWorldMapScenes || navMeshSurface.collectObjects != CollectObjects.Volume)
        {
            return false;
        }

        WorldMapSceneAuthoring[] mapScenes = FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        if (mapScenes == null || mapScenes.Length == 0)
        {
            return false;
        }

        bool foundBounds = false;
        Bounds combinedBounds = default;
        for (int index = 0; index < mapScenes.Length; index++)
        {
            WorldMapSceneAuthoring mapScene = mapScenes[index];
            if (mapScene == null || !mapScene.isActiveAndEnabled)
            {
                continue;
            }

            Bounds mapBounds = mapScene.GetPlayableBoundsWorld();
            if (!foundBounds)
            {
                combinedBounds = mapBounds;
                foundBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(mapBounds);
            }
        }

        if (!foundBounds)
        {
            return false;
        }

        loadedWorldMapBoundsApplied = true;

        Vector3 currentCenter = navMeshSurface.center;
        Vector3 currentSize = navMeshSurface.size;
        float padding = Mathf.Max(0f, worldMapBoundsPadding);
        Vector3 worldCenter = combinedBounds.center;
        Vector3 localCenter = transform.InverseTransformPoint(
            new Vector3(worldCenter.x, transform.TransformPoint(currentCenter).y, worldCenter.z));
        Vector3 targetCenter = new(localCenter.x, currentCenter.y, localCenter.z);
        Vector3 targetSize = new(
            Mathf.Max(currentSize.x, combinedBounds.size.x + padding * 2f),
            currentSize.y,
            Mathf.Max(currentSize.z, combinedBounds.size.z + padding * 2f));

        bool changed = !Approximately(currentCenter, targetCenter) || !Approximately(currentSize, targetSize);
        if (!changed)
        {
            return false;
        }

        navMeshSurface.center = targetCenter;
        navMeshSurface.size = targetSize;

        if (TryGetComponent(out BoxCollider boxCollider))
        {
            boxCollider.center = targetCenter;
            boxCollider.size = targetSize;
        }

        return true;
    }

    private bool ShouldBuildNavMesh(bool surfaceBoundsChanged)
    {
        if (surfaceBoundsChanged)
        {
            return true;
        }

        if (navMeshSurface.navMeshData == null)
        {
            return true;
        }

        if (!rebuildWhenTriangulationIsEmpty)
        {
            return false;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        return triangulation.indices == null || triangulation.indices.Length == 0;
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Approximately(a.x, b.x) &&
               Mathf.Approximately(a.y, b.y) &&
               Mathf.Approximately(a.z, b.z);
    }

    private static bool HasNavMeshData()
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        return triangulation.indices != null && triangulation.indices.Length > 0;
    }

    private static bool HasRegisteredAllCatalogScenes()
    {
        WorldMapManager manager = WorldMapManager.Instance;
        WorldMapCatalog catalog = manager != null ? manager.Catalog : null;
        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            return FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None).Length > 0;
        }

        int expectedSceneCount = 0;
        for (int index = 0; index < catalog.Maps.Count; index++)
        {
            WorldMapDefinition definition = catalog.Maps[index];
            if (definition != null)
            {
                expectedSceneCount++;
            }
        }

        if (expectedSceneCount == 0)
        {
            return false;
        }

        WorldMapSceneAuthoring[] loadedSceneRoots = FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        return loadedSceneRoots != null && loadedSceneRoots.Length >= expectedSceneCount;
    }
}
