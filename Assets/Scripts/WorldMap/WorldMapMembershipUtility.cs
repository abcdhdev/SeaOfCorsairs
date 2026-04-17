using UnityEngine;

public static class WorldMapMembershipUtility
{
    public static bool TryGetMapId(GameObject target, out string mapId)
    {
        mapId = string.Empty;
        return target != null && TryGetMapId(target.transform, out mapId);
    }

    public static bool TryGetMapId(Component target, out string mapId)
    {
        mapId = string.Empty;
        if (target == null)
        {
            return false;
        }

        if (target is Player player)
        {
            mapId = player.CurrentWorldMapId;
            return !string.IsNullOrWhiteSpace(mapId);
        }

        if (target is NPC npc)
        {
            mapId = npc.CurrentWorldMapId;
            return !string.IsNullOrWhiteSpace(mapId);
        }

        if (target is Monster monster)
        {
            mapId = monster.CurrentWorldMapId;
            return !string.IsNullOrWhiteSpace(mapId);
        }

        if (target is SeaRewardBox rewardBox)
        {
            mapId = rewardBox.CurrentWorldMapId;
            return !string.IsNullOrWhiteSpace(mapId);
        }

        WorldMapManager manager = WorldMapManager.Instance;
        return manager != null && manager.TryGetMapId(target, out mapId);
    }

    public static bool AreInSameMap(Component left, Component right)
    {
        return TryGetMapId(left, out string leftMapId) &&
               TryGetMapId(right, out string rightMapId) &&
               string.Equals(leftMapId, rightMapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
