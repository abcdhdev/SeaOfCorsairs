using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WorldMapCatalog", menuName = "Sea Wars/World Map/Catalog")]
public sealed class WorldMapCatalog : ScriptableObject
{
    public const int DefaultColumnCount = 4;
    public const int DefaultRowCount = 7;

    [SerializeField] private List<WorldMapDefinition> maps = new();
    [SerializeField] private string startingMapId = "1-1";

    private readonly Dictionary<string, WorldMapDefinition> mapIdLookup = new(StringComparer.OrdinalIgnoreCase);
    private bool lookupsDirty = true;

    public IReadOnlyList<WorldMapDefinition> Maps => maps;
    public string StartingMapId => NormalizeMapId(startingMapId);

    public void GenerateDefaultGrid()
    {
        maps ??= new List<WorldMapDefinition>(DefaultColumnCount * DefaultRowCount);
        maps.Clear();

        for (int row = 0; row < DefaultRowCount; row++)
        {
            for (int column = 0; column < DefaultColumnCount; column++)
            {
                string mapId = BuildDefaultMapId(row, column);
                maps.Add(new WorldMapDefinition
                {
                    MapId = mapId,
                    Row = row,
                    Column = column,
                    Scene = new WorldMapSceneReference()
                });
            }
        }

        startingMapId = "1-1";
        MarkLookupsDirty();
    }

    public bool TryGetDefinition(string mapId, out WorldMapDefinition definition)
    {
        RebuildLookupsIfNeeded();
        return mapIdLookup.TryGetValue(NormalizeMapId(mapId), out definition);
    }

    public bool TryGetDefinition(int row, int column, out WorldMapDefinition definition)
    {
        definition = null;
        if (maps == null)
        {
            return false;
        }

        for (int index = 0; index < maps.Count; index++)
        {
            WorldMapDefinition candidate = maps[index];
            if (candidate == null)
            {
                continue;
            }

            if (candidate.Row == row && candidate.Column == column)
            {
                definition = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryGetAdjacent(string mapId, MapTransitionDirection direction, out WorldMapDefinition definition)
    {
        definition = null;
        if (!TryGetDefinition(mapId, out WorldMapDefinition current))
        {
            return false;
        }

        int targetRow = current.Row;
        int targetColumn = current.Column;
        switch (direction)
        {
            case MapTransitionDirection.North:
                targetRow += 1;
                break;
            case MapTransitionDirection.East:
                targetColumn += 1;
                break;
            case MapTransitionDirection.South:
                targetRow -= 1;
                break;
            case MapTransitionDirection.West:
                targetColumn -= 1;
                break;
        }

        return TryGetDefinition(targetRow, targetColumn, out definition);
    }

    public List<string> ValidateCatalog()
    {
        var issues = new List<string>();
        var seenMapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCoordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (maps == null || maps.Count == 0)
        {
            issues.Add("Catalog contains no map entries.");
            return issues;
        }

        if (maps.Count != DefaultColumnCount * DefaultRowCount)
        {
            issues.Add($"Catalog contains {maps.Count} entries but the default world grid expects {DefaultColumnCount * DefaultRowCount}.");
        }

        for (int index = 0; index < maps.Count; index++)
        {
            WorldMapDefinition definition = maps[index];
            if (definition == null)
            {
                issues.Add($"Entry {index} is null.");
                continue;
            }

            definition.SyncEditorState();

            string normalizedMapId = NormalizeMapId(definition.MapId);
            if (string.IsNullOrWhiteSpace(normalizedMapId))
            {
                issues.Add($"Entry {index} is missing a map ID.");
            }
            else if (!seenMapIds.Add(normalizedMapId))
            {
                issues.Add($"Duplicate map ID '{normalizedMapId}'.");
            }

            string coordinateKey = $"{definition.Row}:{definition.Column}";
            if (!seenCoordinates.Add(coordinateKey))
            {
                issues.Add($"Duplicate grid coordinate ({definition.Row}, {definition.Column}).");
            }

            if (definition.Scene == null || !definition.Scene.HasScenePath)
            {
                issues.Add($"Map '{normalizedMapId}' is missing a scene reference.");
            }
        }

        if (!TryGetDefinition(StartingMapId, out _))
        {
            issues.Add($"Starting map '{StartingMapId}' does not exist in the catalog.");
        }

        return issues;
    }

    public void MarkLookupsDirty()
    {
        lookupsDirty = true;
    }

    private void OnEnable()
    {
        MarkLookupsDirty();
    }

    private void OnValidate()
    {
        if (maps == null)
        {
            maps = new List<WorldMapDefinition>();
        }

        for (int index = 0; index < maps.Count; index++)
        {
            maps[index]?.SyncEditorState();
        }

        startingMapId = NormalizeMapId(startingMapId);
        MarkLookupsDirty();
    }

    private void RebuildLookupsIfNeeded()
    {
        if (!lookupsDirty)
        {
            return;
        }

        mapIdLookup.Clear();
        if (maps != null)
        {
            for (int index = 0; index < maps.Count; index++)
            {
                WorldMapDefinition definition = maps[index];
                if (definition == null)
                {
                    continue;
                }

                string normalizedMapId = NormalizeMapId(definition.MapId);
                if (string.IsNullOrWhiteSpace(normalizedMapId) || mapIdLookup.ContainsKey(normalizedMapId))
                {
                    continue;
                }

                mapIdLookup.Add(normalizedMapId, definition);
            }
        }

        lookupsDirty = false;
    }

    public static string BuildDefaultMapId(int row, int column)
    {
        int mapNumber = row * 2 + (column / 2) + 1;
        int mapVariant = (column % 2) + 1;
        return $"{mapNumber}-{mapVariant}";
    }

    public static string NormalizeMapId(string mapId)
    {
        return string.IsNullOrWhiteSpace(mapId)
            ? string.Empty
            : mapId.Trim().ToLowerInvariant();
    }
}

[Serializable]
public sealed class WorldMapDefinition
{
    [SerializeField] private string mapId = string.Empty;
    [SerializeField] private int row;
    [SerializeField] private int column;
    [SerializeField] private WorldMapSceneReference scene = new();
    [SerializeField] private string headerLabel = string.Empty;
    [SerializeField] private Texture2D tileIcon;
    [SerializeField] private Texture2D minimapTextureOverride;

    public string MapId
    {
        get => mapId;
        set => mapId = WorldMapCatalog.NormalizeMapId(value);
    }

    public int Row
    {
        get => row;
        set => row = value;
    }

    public int Column
    {
        get => column;
        set => column = value;
    }

    public WorldMapSceneReference Scene
    {
        get => scene ??= new WorldMapSceneReference();
        set => scene = value ?? new WorldMapSceneReference();
    }

    public string HeaderLabel
    {
        get => headerLabel ?? string.Empty;
        set => headerLabel = value ?? string.Empty;
    }

    public Texture2D TileIcon
    {
        get => tileIcon;
        set => tileIcon = value;
    }

    public Texture2D MinimapTextureOverride
    {
        get => minimapTextureOverride;
        set => minimapTextureOverride = value;
    }

    public void SyncEditorState()
    {
        mapId = WorldMapCatalog.NormalizeMapId(mapId);
        scene ??= new WorldMapSceneReference();

#if UNITY_EDITOR
        scene.SyncEditorState();
#endif
    }
}
