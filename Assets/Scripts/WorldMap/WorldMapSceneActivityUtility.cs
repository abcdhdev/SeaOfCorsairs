using System;
using UnityEngine;

public static class WorldMapSceneActivityUtility
{
    public static bool HasActivePlayersOnMap(Component scopedComponent)
    {
        if (scopedComponent == null)
        {
            return true;
        }

        WorldMapManager manager = WorldMapManager.Instance;
        if (manager == null || !manager.TryGetMapId(scopedComponent, out string mapId))
        {
            return true;
        }

        return manager.GetPlayerCount(mapId) > 0;
    }

    public static bool IsRelevantPlayerForScopedMap(Component scopedComponent, Player player)
    {
        if (player == null)
        {
            return false;
        }

        if (scopedComponent == null)
        {
            return true;
        }

        bool hasScopedMap = WorldMapMembershipUtility.TryGetMapId(scopedComponent, out string scopedMapId);
        bool hasPlayerMap = WorldMapMembershipUtility.TryGetMapId(player, out string playerMapId);
        if (!hasScopedMap || !hasPlayerMap)
        {
            return true;
        }

        return string.Equals(scopedMapId, playerMapId, StringComparison.OrdinalIgnoreCase);
    }
}
