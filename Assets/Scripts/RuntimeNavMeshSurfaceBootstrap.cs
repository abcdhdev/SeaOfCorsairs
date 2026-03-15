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

    private IEnumerator Start()
    {
        if (!Application.isPlaying)
        {
            yield break;
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            EnsureNavMeshBuilt();
            if (HasNavMeshData())
            {
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            $"RuntimeNavMeshSurfaceBootstrap: '{gameObject.name}' still has no NavMesh after runtime rebuild attempts.",
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

        if (!ShouldBuildNavMesh())
        {
            return;
        }

        navMeshSurface.BuildNavMesh();

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

    private bool ShouldBuildNavMesh()
    {
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

    private static bool HasNavMeshData()
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        return triangulation.indices != null && triangulation.indices.Length > 0;
    }
}
