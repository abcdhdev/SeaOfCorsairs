using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton manager that maintains a registry of all connected players.
/// Access via PlayerManager.Instance.
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    private readonly Dictionary<ulong, Player> players = new();

    /// <summary>
    /// Read-only dictionary of all connected players keyed by client ID.
    /// </summary>
    public IReadOnlyDictionary<ulong, Player> Players => players;

    /// <summary>
    /// Reference to the local player, or null if not spawned yet.
    /// </summary>
    public Player LocalPlayer { get; private set; }

    /// <summary>
    /// Fired when any player is added to the registry.
    /// </summary>
    public static event Action<Player> OnPlayerAdded;

    /// <summary>
    /// Fired when any player is removed from the registry.
    /// </summary>
    public static event Action<Player> OnPlayerRemoved;

    /// <summary>
    /// Fired when the local player spawns.
    /// </summary>
    public static event Action<Player> OnLocalPlayerSpawned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Register a player when they spawn on the network.
    /// Called by Player.OnNetworkSpawn().
    /// </summary>
    public void RegisterPlayer(Player player)
    {
        if (player == null) return;

        ulong clientId = player.OwnerClientId;

        if (players.ContainsKey(clientId))
        {
            Debug.LogWarning($"PlayerManager: Player with clientId {clientId} already registered.");
            return;
        }

        players[clientId] = player;
        Debug.Log($"PlayerManager: Player registered - ClientId: {clientId}, Name: {player.gameObject.name}");

        OnPlayerAdded?.Invoke(player);

        if (player.IsOwner)
        {
            LocalPlayer = player;
            Debug.Log($"PlayerManager: Local player set - {player.gameObject.name}");
            OnLocalPlayerSpawned?.Invoke(player);
        }
    }

    /// <summary>
    /// Unregister a player when they despawn from the network.
    /// Called by Player.OnNetworkDespawn().
    /// </summary>
    public void UnregisterPlayer(ulong clientId)
    {
        if (players.TryGetValue(clientId, out Player player))
        {
            players.Remove(clientId);
            Debug.Log($"PlayerManager: Player unregistered - ClientId: {clientId}");

            if (LocalPlayer == player)
            {
                LocalPlayer = null;
            }

            OnPlayerRemoved?.Invoke(player);
        }
    }

    /// <summary>
    /// Get a player by their client ID.
    /// </summary>
    public Player GetPlayer(ulong clientId)
    {
        players.TryGetValue(clientId, out Player player);
        return player;
    }

    /// <summary>
    /// Get all players as a list.
    /// </summary>
    public List<Player> GetAllPlayers()
    {
        return new List<Player>(players.Values);
    }

    /// <summary>
    /// Get the count of connected players.
    /// </summary>
    public int PlayerCount => players.Count;
}
