using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side observer manager that keeps hidden entities simulated on the server
/// while only networking players, NPCs, monsters, and reward boxes to the clients
/// that are allowed to observe them.
/// </summary>
public sealed class FogOfWarNetworkVisibilityController : MonoBehaviour
{
    private const string RuntimeObjectName = "[FogOfWarNetworkVisibility]";

    private static FogOfWarNetworkVisibilityController s_instance;

    [SerializeField, Min(0.05f)]
    private float visibilityRefreshInterval = FogOfWarVisibilitySettings.DefaultNetworkRefreshInterval;

    private readonly HashSet<Player> trackedPlayers = new();
    private readonly HashSet<NPC> trackedNpcs = new();
    private readonly HashSet<Monster> trackedMonsters = new();
    private readonly HashSet<SeaRewardBox> trackedRewardBoxes = new();
    private readonly List<Player> playerSnapshot = new(16);
    private readonly List<NPC> npcSnapshot = new(64);
    private readonly List<Monster> monsterSnapshot = new(64);
    private readonly List<SeaRewardBox> rewardBoxSnapshot = new(64);
    private Coroutine refreshLoop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null)
        {
            return;
        }

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        s_instance = runtimeObject.AddComponent<FogOfWarNetworkVisibilityController>();
    }

    public static void Register(Player player)
    {
        if (player == null)
        {
            return;
        }

        EnsureInstance();
        if (s_instance == null)
        {
            return;
        }

        s_instance.trackedPlayers.Add(player);
        s_instance.RefreshVisibilityNow();
    }

    public static void Unregister(Player player)
    {
        if (s_instance == null || player == null)
        {
            return;
        }

        s_instance.trackedPlayers.Remove(player);
    }

    public static void Register(NPC npc)
    {
        if (npc == null)
        {
            return;
        }

        EnsureInstance();
        if (s_instance == null)
        {
            return;
        }

        s_instance.trackedNpcs.Add(npc);
        s_instance.RefreshVisibilityNow();
    }

    public static void Unregister(NPC npc)
    {
        if (s_instance == null || npc == null)
        {
            return;
        }

        s_instance.trackedNpcs.Remove(npc);
    }

    public static void Register(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        EnsureInstance();
        if (s_instance == null)
        {
            return;
        }

        s_instance.trackedMonsters.Add(monster);
        s_instance.RefreshVisibilityNow();
    }

    public static void Unregister(Monster monster)
    {
        if (s_instance == null || monster == null)
        {
            return;
        }

        s_instance.trackedMonsters.Remove(monster);
    }

    public static void Register(SeaRewardBox rewardBox)
    {
        if (rewardBox == null)
        {
            return;
        }

        EnsureInstance();
        if (s_instance == null)
        {
            return;
        }

        s_instance.trackedRewardBoxes.Add(rewardBox);
        s_instance.RefreshVisibilityNow();
    }

    public static void Unregister(SeaRewardBox rewardBox)
    {
        if (s_instance == null || rewardBox == null)
        {
            return;
        }

        s_instance.trackedRewardBoxes.Remove(rewardBox);
    }

    public static bool ShouldPlayerBeVisibleToClient(Player targetPlayer, ulong clientId)
    {
        if (targetPlayer == null || targetPlayer.NetworkObject == null)
        {
            return false;
        }

        if (clientId == targetPlayer.OwnerClientId)
        {
            return true;
        }

        return ShouldPlayerBeVisibleToViewer(targetPlayer, ResolveViewer(clientId));
    }

    public static bool ShouldNpcBeVisibleToClient(NPC targetNpc, ulong clientId)
    {
        return ShouldNpcBeVisibleToViewer(targetNpc, ResolveViewer(clientId));
    }

    public static bool ShouldMonsterBeVisibleToClient(Monster targetMonster, ulong clientId)
    {
        return ShouldComponentBeVisibleToViewer(targetMonster, ResolveViewer(clientId));
    }

    public static bool ShouldRewardBoxBeVisibleToClient(SeaRewardBox rewardBox, ulong clientId)
    {
        return ShouldComponentBeVisibleToViewer(rewardBox, ResolveViewer(clientId));
    }

    private static void EnsureInstance()
    {
        if (s_instance != null)
        {
            return;
        }

        Bootstrap();
    }

    private void OnEnable()
    {
        if (refreshLoop == null)
        {
            refreshLoop = StartCoroutine(RefreshLoop());
        }
    }

    private void OnDisable()
    {
        if (refreshLoop != null)
        {
            StopCoroutine(refreshLoop);
            refreshLoop = null;
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private IEnumerator RefreshLoop()
    {
        while (true)
        {
            if (ShouldRefreshVisibility())
            {
                RefreshVisibility();
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, visibilityRefreshInterval));
                continue;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private void RefreshVisibilityNow()
    {
        if (ShouldRefreshVisibility())
        {
            RefreshVisibility();
        }
    }

    private bool ShouldRefreshVisibility()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsServer;
    }

    private void RefreshVisibility()
    {
        trackedPlayers.RemoveWhere(player => player == null || player.NetworkObject == null || !player.NetworkObject.IsSpawned);
        trackedNpcs.RemoveWhere(npc => npc == null || npc.NetworkObject == null || !npc.NetworkObject.IsSpawned);
        trackedMonsters.RemoveWhere(monster => monster == null || monster.NetworkObject == null || !monster.NetworkObject.IsSpawned);
        trackedRewardBoxes.RemoveWhere(rewardBox => rewardBox == null || rewardBox.NetworkObject == null || !rewardBox.NetworkObject.IsSpawned);

        playerSnapshot.Clear();
        foreach (Player player in trackedPlayers)
        {
            playerSnapshot.Add(player);
        }

        npcSnapshot.Clear();
        foreach (NPC npc in trackedNpcs)
        {
            npcSnapshot.Add(npc);
        }

        monsterSnapshot.Clear();
        foreach (Monster monster in trackedMonsters)
        {
            monsterSnapshot.Add(monster);
        }

        rewardBoxSnapshot.Clear();
        foreach (SeaRewardBox rewardBox in trackedRewardBoxes)
        {
            rewardBoxSnapshot.Add(rewardBox);
        }

        for (int viewerIndex = 0; viewerIndex < playerSnapshot.Count; viewerIndex++)
        {
            Player viewer = playerSnapshot[viewerIndex];
            if (viewer == null || viewer.NetworkObject == null || !viewer.NetworkObject.IsSpawned)
            {
                continue;
            }

            ulong viewerClientId = viewer.OwnerClientId;

            for (int playerIndex = 0; playerIndex < playerSnapshot.Count; playerIndex++)
            {
                Player targetPlayer = playerSnapshot[playerIndex];
                if (targetPlayer == null || targetPlayer.NetworkObject == null || !targetPlayer.NetworkObject.IsSpawned)
                {
                    continue;
                }

                bool shouldBeVisible = ShouldPlayerBeVisibleToViewer(targetPlayer, viewer);
                SyncVisibility(targetPlayer.NetworkObject, viewerClientId, shouldBeVisible);
            }

            for (int npcIndex = 0; npcIndex < npcSnapshot.Count; npcIndex++)
            {
                NPC targetNpc = npcSnapshot[npcIndex];
                if (targetNpc == null || targetNpc.NetworkObject == null || !targetNpc.NetworkObject.IsSpawned)
                {
                    continue;
                }

                bool shouldBeVisible = ShouldNpcBeVisibleToViewer(targetNpc, viewer);
                SyncVisibility(targetNpc.NetworkObject, viewerClientId, shouldBeVisible);
            }

            for (int monsterIndex = 0; monsterIndex < monsterSnapshot.Count; monsterIndex++)
            {
                Monster targetMonster = monsterSnapshot[monsterIndex];
                if (targetMonster == null || targetMonster.NetworkObject == null || !targetMonster.NetworkObject.IsSpawned)
                {
                    continue;
                }

                bool shouldBeVisible = ShouldComponentBeVisibleToViewer(targetMonster, viewer);
                SyncVisibility(targetMonster.NetworkObject, viewerClientId, shouldBeVisible);
            }

            for (int rewardBoxIndex = 0; rewardBoxIndex < rewardBoxSnapshot.Count; rewardBoxIndex++)
            {
                SeaRewardBox rewardBox = rewardBoxSnapshot[rewardBoxIndex];
                if (rewardBox == null || rewardBox.NetworkObject == null || !rewardBox.NetworkObject.IsSpawned)
                {
                    continue;
                }

                bool shouldBeVisible = ShouldComponentBeVisibleToViewer(rewardBox, viewer);
                SyncVisibility(rewardBox.NetworkObject, viewerClientId, shouldBeVisible);
            }
        }
    }

    private static void SyncVisibility(NetworkObject networkObject, ulong clientId, bool shouldBeVisible)
    {
        if (networkObject == null || !networkObject.IsSpawned)
        {
            return;
        }

        bool isVisible = networkObject.IsNetworkVisibleTo(clientId);
        if (shouldBeVisible)
        {
            if (!isVisible)
            {
                networkObject.NetworkShow(clientId);
            }

            return;
        }

        if (isVisible)
        {
            networkObject.NetworkHide(clientId);
        }
    }

    private static Player ResolveViewer(ulong clientId)
    {
        if (s_instance != null)
        {
            foreach (Player player in s_instance.trackedPlayers)
            {
                if (player != null && player.OwnerClientId == clientId)
                {
                    return player;
                }
            }
        }

        return PlayerManager.Instance != null ? PlayerManager.Instance.GetPlayer(clientId) : null;
    }

    private static bool ShouldPlayerBeVisibleToViewer(Player targetPlayer, Player viewer)
    {
        if (targetPlayer == null || targetPlayer.NetworkObject == null)
        {
            return false;
        }

        if (viewer == null || viewer.NetworkObject == null || !viewer.NetworkObject.IsSpawned)
        {
            return false;
        }

        if (!WorldMapMembershipUtility.AreInSameMap(targetPlayer, viewer))
        {
            return false;
        }

        return targetPlayer == viewer || IsWithinRevealRadius(viewer.transform.position, targetPlayer.transform.position);
    }

    private static bool ShouldNpcBeVisibleToViewer(NPC targetNpc, Player viewer)
    {
        if (targetNpc == null || targetNpc.NetworkObject == null)
        {
            return false;
        }

        if (viewer == null || viewer.NetworkObject == null || !viewer.NetworkObject.IsSpawned)
        {
            return false;
        }

        if (!WorldMapMembershipUtility.AreInSameMap(targetNpc, viewer))
        {
            return false;
        }

        return IsWithinRevealRadius(viewer.transform.position, targetNpc.transform.position);
    }

    private static bool ShouldComponentBeVisibleToViewer(Component targetComponent, Player viewer)
    {
        if (targetComponent == null)
        {
            return false;
        }

        if (viewer == null || viewer.NetworkObject == null || !viewer.NetworkObject.IsSpawned || viewer.IsDead)
        {
            return false;
        }

        return WorldMapMembershipUtility.AreInSameMap(targetComponent, viewer);
    }

    private static bool IsWithinRevealRadius(Vector3 viewerPosition, Vector3 targetPosition)
    {
        float revealRadius = FogOfWarVisibilitySettings.DefaultRevealRadius;
        return (targetPosition - viewerPosition).sqrMagnitude <= revealRadius * revealRadius;
    }
}
