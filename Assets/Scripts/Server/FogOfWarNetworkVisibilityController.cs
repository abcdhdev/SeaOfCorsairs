using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side observer manager that keeps hidden entities simulated on the server
/// while only networking players and NPCs revealed by fog-of-war.
/// </summary>
public sealed class FogOfWarNetworkVisibilityController : MonoBehaviour
{
    private const string RuntimeObjectName = "[FogOfWarNetworkVisibility]";

    private static FogOfWarNetworkVisibilityController s_instance;

    [SerializeField, Min(0.05f)]
    private float visibilityRefreshInterval = FogOfWarVisibilitySettings.DefaultNetworkRefreshInterval;

    private readonly HashSet<Player> trackedPlayers = new();
    private readonly HashSet<NPC> trackedNpcs = new();
    private readonly List<Player> playerSnapshot = new(16);
    private readonly List<NPC> npcSnapshot = new(64);
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

        return IsWithinRevealRadius(viewer.transform.position, targetNpc.transform.position);
    }

    private static bool IsWithinRevealRadius(Vector3 viewerPosition, Vector3 targetPosition)
    {
        float revealRadius = FogOfWarVisibilitySettings.DefaultRevealRadius;
        return (targetPosition - viewerPosition).sqrMagnitude <= revealRadius * revealRadius;
    }
}
