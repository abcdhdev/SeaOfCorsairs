using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpawnPointResolver
{
    public static bool TryGetPlayerSpawnTransform(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        spawnPosition = Vector3.zero;
        spawnRotation = Quaternion.identity;

        List<Transform> spawnCandidates = CollectPlayerSpawnCandidates();
        if (spawnCandidates.Count == 0)
        {
            return false;
        }

        Transform selectedSpawn = spawnCandidates[UnityEngine.Random.Range(0, spawnCandidates.Count)];
        spawnPosition = selectedSpawn.position;
        spawnRotation = selectedSpawn.rotation;
        return true;
    }

    private static List<Transform> CollectPlayerSpawnCandidates()
    {
        var spawnCandidates = new List<Transform>(4);

        PlayerSpawnPoint[] explicitSpawnPoints = UnityEngine.Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
        for (int i = 0; i < explicitSpawnPoints.Length; i++)
        {
            PlayerSpawnPoint spawnPoint = explicitSpawnPoints[i];
            if (spawnPoint == null || !spawnPoint.isActiveAndEnabled)
            {
                continue;
            }

            spawnCandidates.Add(spawnPoint.transform);
        }

        if (spawnCandidates.Count > 0)
        {
            return spawnCandidates;
        }

        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!string.Equals(candidate.name, "SpawnPoint", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate.TryGetComponent(out NpcSpawnPoint _))
            {
                continue;
            }

            spawnCandidates.Add(candidate);
        }

        return spawnCandidates;
    }
}
