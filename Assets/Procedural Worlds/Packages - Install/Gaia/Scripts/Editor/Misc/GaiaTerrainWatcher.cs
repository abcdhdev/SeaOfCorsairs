#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Gaia
{
    [InitializeOnLoad]
    public static class GaiaTerrainWatcher
    {
        public enum TerrainChangeType { Created, Removed }

        // Fires for both creation and removal
        public static event System.Action<Terrain, TerrainChangeType> TerrainChanged;

        private static HashSet<Terrain> knownTerrains = new HashSet<Terrain>();

        static GaiaTerrainWatcher()
        {
            EditorApplication.hierarchyChanged += CheckTerrains;
            CacheExistingTerrains();
        }

        private static void CacheExistingTerrains()
        {
            knownTerrains.Clear();
            foreach (var t in GameObject.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                knownTerrains.Add(t);
        }

        private static void CheckTerrains()
        {
            var currentTerrains = new HashSet<Terrain>(GameObject.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

            // Detect created terrains
            foreach (var t in currentTerrains)
            {
                if (!knownTerrains.Contains(t))
                {
                    knownTerrains.Add(t);
                    TerrainChanged?.Invoke(t, TerrainChangeType.Created);
                }
            }

            // Detect removed terrains
            var removedTerrains = new List<Terrain>();
            foreach (var t in knownTerrains)
            {
                if (!currentTerrains.Contains(t))
                    removedTerrains.Add(t);
            }
            foreach (var t in removedTerrains)
            {
                knownTerrains.Remove(t);
                TerrainChanged?.Invoke(t, TerrainChangeType.Removed);
            }
        }
    }
}
#endif
