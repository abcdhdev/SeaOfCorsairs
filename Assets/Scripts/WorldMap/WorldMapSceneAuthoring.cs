using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class WorldMapSceneAuthoring : MonoBehaviour
{
    [SerializeField] private string mapId = string.Empty;

    [Header("Playable Bounds")]
    [SerializeField] private Vector3 playableBoundsCenter = Vector3.zero;
    [SerializeField] private Vector3 playableBoundsSize = new(512f, 20f, 512f);

    [Header("Travel Settings")]
    [SerializeField, Min(1f)] private float edgePromptThreshold = 30f;
    [SerializeField, Min(1f)] private float arrivalInset = 40f;
    [SerializeField] private Vector2 orthogonalClampNormalized = new(0.15f, 0.85f);
    [SerializeField] private WorldMapTravelZone northTravelZone = new();
    [SerializeField] private WorldMapTravelZone eastTravelZone = new();
    [SerializeField] private WorldMapTravelZone southTravelZone = new();
    [SerializeField] private WorldMapTravelZone westTravelZone = new();

    [Header("Arrival Anchors")]
    [SerializeField] private Transform northArrivalAnchor;
    [SerializeField] private Transform eastArrivalAnchor;
    [SerializeField] private Transform southArrivalAnchor;
    [SerializeField] private Transform westArrivalAnchor;
    [SerializeField] private Transform respawnAnchor;

    [Header("Map Overrides")]
    [SerializeField] private Texture2D minimapTextureOverride;

    [Header("Local Spawners")]
    [SerializeField] private NPCSpawner[] npcSpawners = Array.Empty<NPCSpawner>();
    [SerializeField] private MonsterSpawner[] monsterSpawners = Array.Empty<MonsterSpawner>();
    [SerializeField] private SeaRewardBoxSpawner[] rewardBoxSpawners = Array.Empty<SeaRewardBoxSpawner>();

    public string MapId => WorldMapCatalog.NormalizeMapId(mapId);
    public Texture2D MinimapTextureOverride => minimapTextureOverride;
    public float EdgePromptThreshold => Mathf.Max(1f, edgePromptThreshold);
    public float ArrivalInset => Mathf.Max(1f, arrivalInset);
    public Vector2 OrthogonalClampNormalized => new(
        Mathf.Clamp01(Mathf.Min(orthogonalClampNormalized.x, orthogonalClampNormalized.y)),
        Mathf.Clamp01(Mathf.Max(orthogonalClampNormalized.x, orthogonalClampNormalized.y)));
    public IReadOnlyList<NPCSpawner> NpcSpawners => npcSpawners;
    public IReadOnlyList<MonsterSpawner> MonsterSpawners => monsterSpawners;
    public IReadOnlyList<SeaRewardBoxSpawner> RewardBoxSpawners => rewardBoxSpawners;

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            WorldMapManager.Instance?.RegisterScene(this);
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            WorldMapManager.Instance?.RegisterScene(this);
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            WorldMapManager.Instance?.UnregisterScene(this);
        }
    }

    public Bounds GetPlayableBoundsWorld()
    {
        Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Vector3 worldCenter = matrix.MultiplyPoint3x4(playableBoundsCenter);
        Vector3 worldAxisX = Abs(matrix.MultiplyVector(Vector3.right * playableBoundsSize.x));
        Vector3 worldAxisY = Abs(matrix.MultiplyVector(Vector3.up * playableBoundsSize.y));
        Vector3 worldAxisZ = Abs(matrix.MultiplyVector(Vector3.forward * playableBoundsSize.z));
        Vector3 worldSize = worldAxisX + worldAxisY + worldAxisZ;
        return new Bounds(worldCenter, worldSize);
    }

    public bool TryGetTravelZone(MapTransitionDirection direction, out WorldMapTravelZone zone)
    {
        switch (direction)
        {
            case MapTransitionDirection.North:
                zone = northTravelZone;
                return zone != null;
            case MapTransitionDirection.East:
                zone = eastTravelZone;
                return zone != null;
            case MapTransitionDirection.South:
                zone = southTravelZone;
                return zone != null;
            case MapTransitionDirection.West:
                zone = westTravelZone;
                return zone != null;
            default:
                zone = null;
                return false;
        }
    }

    public bool IsWithinTravelZone(MapTransitionDirection direction, Vector3 worldPosition)
    {
        return TryGetTravelZone(direction, out WorldMapTravelZone zone) &&
               zone != null &&
               zone.Contains(transform, worldPosition);
    }

    public bool TryGetPromptDirection(Vector3 worldPosition, out MapTransitionDirection direction)
    {
        direction = default;
        bool found = false;
        float bestDistanceSqr = float.MaxValue;
        MapTransitionDirection[] directions =
        {
            MapTransitionDirection.North,
            MapTransitionDirection.East,
            MapTransitionDirection.South,
            MapTransitionDirection.West
        };

        for (int index = 0; index < directions.Length; index++)
        {
            MapTransitionDirection candidateDirection = directions[index];
            if (!TryGetTravelZone(candidateDirection, out WorldMapTravelZone zone) ||
                zone == null ||
                !zone.Contains(transform, worldPosition))
            {
                continue;
            }

            float candidateDistanceSqr = (zone.GetWorldCenter(transform) - worldPosition).sqrMagnitude;
            if (!found || candidateDistanceSqr < bestDistanceSqr)
            {
                found = true;
                bestDistanceSqr = candidateDistanceSqr;
                direction = candidateDirection;
            }
        }

        return found;
    }

    public bool TryResolveRespawnTransform(out Vector3 position, out Quaternion rotation)
    {
        if (respawnAnchor != null)
        {
            position = respawnAnchor.position;
            rotation = respawnAnchor.rotation;
            return true;
        }

        position = transform.TransformPoint(playableBoundsCenter);
        rotation = transform.rotation;
        return true;
    }

    public bool TryResolveTravelDestination(MapTransitionDirection incomingDirection, float normalizedOrthogonal, out Vector3 position, out Quaternion rotation)
    {
        if (TryGetArrivalAnchor(incomingDirection, out Transform arrivalAnchor))
        {
            position = arrivalAnchor.position;
            rotation = arrivalAnchor.rotation;
            return true;
        }

        Bounds playableBounds = GetPlayableBoundsWorld();
        Vector2 clampRange = OrthogonalClampNormalized;
        float clampedOrthogonal = Mathf.Clamp(normalizedOrthogonal, clampRange.x, clampRange.y);
        float resolvedInset = Mathf.Min(ArrivalInset, Mathf.Max(1f, Mathf.Min(playableBounds.extents.x, playableBounds.extents.z) - 1f));
        float y = respawnAnchor != null ? respawnAnchor.position.y : playableBounds.center.y;

        switch (incomingDirection)
        {
            case MapTransitionDirection.North:
                position = new Vector3(
                    Mathf.Lerp(playableBounds.min.x, playableBounds.max.x, clampedOrthogonal),
                    y,
                    playableBounds.max.z - resolvedInset);
                rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                return true;
            case MapTransitionDirection.East:
                position = new Vector3(
                    playableBounds.max.x - resolvedInset,
                    y,
                    Mathf.Lerp(playableBounds.min.z, playableBounds.max.z, clampedOrthogonal));
                rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                return true;
            case MapTransitionDirection.South:
                position = new Vector3(
                    Mathf.Lerp(playableBounds.min.x, playableBounds.max.x, clampedOrthogonal),
                    y,
                    playableBounds.min.z + resolvedInset);
                rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                return true;
            case MapTransitionDirection.West:
                position = new Vector3(
                    playableBounds.min.x + resolvedInset,
                    y,
                    Mathf.Lerp(playableBounds.min.z, playableBounds.max.z, clampedOrthogonal));
                rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                return true;
            default:
                position = respawnAnchor != null ? respawnAnchor.position : transform.position;
                rotation = respawnAnchor != null ? respawnAnchor.rotation : transform.rotation;
                return false;
        }
    }

    public float GetNormalizedOrthogonalPosition(MapTransitionDirection direction, Vector3 worldPosition)
    {
        Bounds bounds = GetPlayableBoundsWorld();
        return direction == MapTransitionDirection.North || direction == MapTransitionDirection.South
            ? Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x)
            : Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPosition.z);
    }

    public List<string> ValidateAuthoring(WorldMapCatalog catalog = null)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(MapId))
        {
            issues.Add($"Scene '{gameObject.scene.name}' is missing a map ID on '{name}'.");
        }

        if (respawnAnchor == null)
        {
            issues.Add($"Map '{MapId}' is missing a respawn anchor.");
        }

        if (northArrivalAnchor == null)
        {
            issues.Add($"Map '{MapId}' is missing a north arrival anchor.");
        }

        if (eastArrivalAnchor == null)
        {
            issues.Add($"Map '{MapId}' is missing an east arrival anchor.");
        }

        if (southArrivalAnchor == null)
        {
            issues.Add($"Map '{MapId}' is missing a south arrival anchor.");
        }

        if (westArrivalAnchor == null)
        {
            issues.Add($"Map '{MapId}' is missing a west arrival anchor.");
        }

        if (catalog != null)
        {
            bool matchedLoadedScene = false;
            IReadOnlyList<WorldMapDefinition> definitions = catalog.Maps;
            for (int index = 0; index < definitions.Count; index++)
            {
                WorldMapDefinition definition = definitions[index];
                if (definition == null || definition.Scene == null || !definition.Scene.HasScenePath)
                {
                    continue;
                }

                if (!string.Equals(definition.Scene.ScenePath, gameObject.scene.path, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matchedLoadedScene = true;
                if (!string.Equals(definition.MapId, MapId, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Scene '{gameObject.scene.name}' has root map ID '{MapId}' but catalog entry '{definition.MapId}' points to this scene.");
                }
            }

            if (!matchedLoadedScene && !string.IsNullOrWhiteSpace(gameObject.scene.path))
            {
                issues.Add($"Scene '{gameObject.scene.name}' has a world map root but no matching scene entry in the catalog.");
            }
        }

        return issues;
    }

    private bool TryGetArrivalAnchor(MapTransitionDirection incomingDirection, out Transform anchor)
    {
        switch (incomingDirection)
        {
            case MapTransitionDirection.North:
                anchor = northArrivalAnchor;
                return anchor != null;
            case MapTransitionDirection.East:
                anchor = eastArrivalAnchor;
                return anchor != null;
            case MapTransitionDirection.South:
                anchor = southArrivalAnchor;
                return anchor != null;
            case MapTransitionDirection.West:
                anchor = westArrivalAnchor;
                return anchor != null;
            default:
                anchor = null;
                return false;
        }
    }

    private void OnValidate()
    {
        RefreshEditorState();
    }

#if UNITY_EDITOR
    public void RefreshEditorState()
    {
        mapId = WorldMapCatalog.NormalizeMapId(mapId);
        playableBoundsSize = new Vector3(
            Mathf.Max(1f, playableBoundsSize.x),
            Mathf.Max(1f, playableBoundsSize.y),
            Mathf.Max(1f, playableBoundsSize.z));
        edgePromptThreshold = Mathf.Max(1f, edgePromptThreshold);
        arrivalInset = Mathf.Max(1f, arrivalInset);
        orthogonalClampNormalized = new Vector2(
            Mathf.Clamp01(orthogonalClampNormalized.x),
            Mathf.Clamp01(orthogonalClampNormalized.y));

        if (string.IsNullOrWhiteSpace(mapId))
        {
            mapId = WorldMapCatalog.NormalizeMapId(gameObject.scene.name.Replace("Map_", string.Empty));
        }

        northArrivalAnchor = ResolveNamedChildAnchor(northArrivalAnchor, "NorthArrivalAnchor");
        eastArrivalAnchor = ResolveNamedChildAnchor(eastArrivalAnchor, "EastArrivalAnchor");
        southArrivalAnchor = ResolveNamedChildAnchor(southArrivalAnchor, "SouthArrivalAnchor");
        westArrivalAnchor = ResolveNamedChildAnchor(westArrivalAnchor, "WestArrivalAnchor");
        respawnAnchor = ResolveNamedChildAnchor(respawnAnchor, "RespawnAnchor");

        northTravelZone ??= new WorldMapTravelZone();
        eastTravelZone ??= new WorldMapTravelZone();
        southTravelZone ??= new WorldMapTravelZone();
        westTravelZone ??= new WorldMapTravelZone();

        AutoPopulateZoneDefaults();
        AutoPopulateSpawnerReferences();
    }
#else
    private void RefreshEditorState()
    {
        mapId = WorldMapCatalog.NormalizeMapId(mapId);
        playableBoundsSize = new Vector3(
            Mathf.Max(1f, playableBoundsSize.x),
            Mathf.Max(1f, playableBoundsSize.y),
            Mathf.Max(1f, playableBoundsSize.z));
        edgePromptThreshold = Mathf.Max(1f, edgePromptThreshold);
        arrivalInset = Mathf.Max(1f, arrivalInset);
        orthogonalClampNormalized = new Vector2(
            Mathf.Clamp01(orthogonalClampNormalized.x),
            Mathf.Clamp01(orthogonalClampNormalized.y));

        northTravelZone ??= new WorldMapTravelZone();
        eastTravelZone ??= new WorldMapTravelZone();
        southTravelZone ??= new WorldMapTravelZone();
        westTravelZone ??= new WorldMapTravelZone();

        AutoPopulateZoneDefaults();
        AutoPopulateSpawnerReferences();
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.56f, 0.89f, 0.98f, 0.85f);
        Gizmos.DrawWireCube(playableBoundsCenter, playableBoundsSize);
        Gizmos.color = new Color(0.56f, 0.89f, 0.98f, 0.08f);
        Gizmos.DrawCube(playableBoundsCenter, playableBoundsSize);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;

        northTravelZone?.DrawGizmos(transform, new Color(0.42f, 0.95f, 0.52f, 0.7f));
        eastTravelZone?.DrawGizmos(transform, new Color(0.98f, 0.85f, 0.26f, 0.7f));
        southTravelZone?.DrawGizmos(transform, new Color(0.98f, 0.56f, 0.26f, 0.7f));
        westTravelZone?.DrawGizmos(transform, new Color(0.58f, 0.65f, 0.98f, 0.7f));

        DrawAnchorGizmo(northArrivalAnchor, Color.green);
        DrawAnchorGizmo(eastArrivalAnchor, Color.yellow);
        DrawAnchorGizmo(southArrivalAnchor, new Color(1f, 0.4f, 0.2f));
        DrawAnchorGizmo(westArrivalAnchor, new Color(0.4f, 0.7f, 1f));
        DrawAnchorGizmo(respawnAnchor, Color.white);

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(MapId))
        {
            Handles.Label(transform.position + Vector3.up * 8f, MapId);
        }
#endif
    }

    private void AutoPopulateZoneDefaults()
    {
        Vector3 size = playableBoundsSize;
        Vector3 center = playableBoundsCenter;
        float threshold = Mathf.Min(EdgePromptThreshold, Mathf.Max(1f, Mathf.Min(size.x, size.z) * 0.5f));

        if (!northTravelZone.IsConfigured)
        {
            northTravelZone.Size = new Vector3(size.x, size.y, threshold);
            northTravelZone.Center = center + new Vector3(0f, 0f, size.z * 0.5f - threshold * 0.5f);
        }

        if (!eastTravelZone.IsConfigured)
        {
            eastTravelZone.Size = new Vector3(threshold, size.y, size.z);
            eastTravelZone.Center = center + new Vector3(size.x * 0.5f - threshold * 0.5f, 0f, 0f);
        }

        if (!southTravelZone.IsConfigured)
        {
            southTravelZone.Size = new Vector3(size.x, size.y, threshold);
            southTravelZone.Center = center - new Vector3(0f, 0f, size.z * 0.5f - threshold * 0.5f);
        }

        if (!westTravelZone.IsConfigured)
        {
            westTravelZone.Size = new Vector3(threshold, size.y, size.z);
            westTravelZone.Center = center - new Vector3(size.x * 0.5f - threshold * 0.5f, 0f, 0f);
        }
    }

    private void AutoPopulateSpawnerReferences()
    {
        if (npcSpawners == null || npcSpawners.Length == 0)
        {
            npcSpawners = GetComponentsInChildren<NPCSpawner>(true);
        }

        if (monsterSpawners == null || monsterSpawners.Length == 0)
        {
            monsterSpawners = GetComponentsInChildren<MonsterSpawner>(true);
        }

        if (rewardBoxSpawners == null || rewardBoxSpawners.Length == 0)
        {
            rewardBoxSpawners = GetComponentsInChildren<SeaRewardBoxSpawner>(true);
        }
    }

    private static void DrawAnchorGizmo(Transform anchor, Color color)
    {
        if (anchor == null)
        {
            return;
        }

        Color previousColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawSphere(anchor.position, 3f);
        Gizmos.DrawLine(anchor.position, anchor.position + anchor.forward * 10f);
        Gizmos.color = previousColor;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

#if UNITY_EDITOR
    private Transform ResolveNamedChildAnchor(Transform currentAnchor, string childName)
    {
        if (currentAnchor != null)
        {
            return currentAnchor;
        }

        Transform directChild = transform.Find(childName);
        if (directChild != null)
        {
            return directChild;
        }

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < childTransforms.Length; index++)
        {
            Transform candidate = childTransforms[index];
            if (candidate != null && string.Equals(candidate.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
#endif
}
