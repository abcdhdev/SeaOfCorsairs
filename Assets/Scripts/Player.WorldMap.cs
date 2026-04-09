using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public partial class Player
{
    private readonly NetworkVariable<FixedString32Bytes> m_currentWorldMapId = new(
        new FixedString32Bytes("1-1"),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public event Action<string, string> OnWorldMapIdChanged = delegate { };

    public string CurrentWorldMapId => WorldMapCatalog.NormalizeMapId(m_currentWorldMapId.Value.ToString());

    public bool RequestMapTransition(MapTransitionDirection direction)
    {
        if (!IsOwner || !IsSpawned || IsDead)
        {
            return false;
        }

        RequestMapTransitionServerRpc(direction);
        return true;
    }

    private void InitializeWorldMapSubscriptions()
    {
        m_currentWorldMapId.OnValueChanged += OnCurrentWorldMapIdChanged;
    }

    private void DisposeWorldMapSubscriptions()
    {
        m_currentWorldMapId.OnValueChanged -= OnCurrentWorldMapIdChanged;
    }

    private void InitializeWorldMapOnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        string resolvedStartingMapId = ResolveInitialWorldMapId();
        SetCurrentWorldMapIdServer(resolvedStartingMapId, forceNotify: true);
    }

    private void HandleWorldMapNetworkDespawn()
    {
        if (!IsServer)
        {
            return;
        }

        string currentMapId = CurrentWorldMapId;
        if (!string.IsNullOrWhiteSpace(currentMapId))
        {
            WorldMapManager.Instance?.NotifyPlayerMapChanged(this, currentMapId, string.Empty);
        }
    }

    private void SetCurrentWorldMapIdServer(string mapId, bool forceNotify = false)
    {
        if (!IsServer)
        {
            return;
        }

        string normalizedMapId = WorldMapCatalog.NormalizeMapId(mapId);
        if (string.IsNullOrWhiteSpace(normalizedMapId))
        {
            normalizedMapId = "1-1";
        }

        string previousMapId = CurrentWorldMapId;
        bool changed = !string.Equals(previousMapId, normalizedMapId, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            m_currentWorldMapId.Value = new FixedString32Bytes(normalizedMapId);
        }

        if (changed || forceNotify)
        {
            WorldMapManager.Instance?.NotifyPlayerMapChanged(this, previousMapId, normalizedMapId);
        }
    }

    private bool TryGetWorldMapSpawnTransform(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        if (WorldMapManager.Instance != null &&
            WorldMapManager.Instance.TryResolveRespawn(this, out spawnPosition, out spawnRotation))
        {
            return true;
        }

        return SpawnPointResolver.TryGetPlayerSpawnTransform(out spawnPosition, out spawnRotation);
    }

    private string ResolveInitialWorldMapId()
    {
        string currentMapId = CurrentWorldMapId;
        if (!string.IsNullOrWhiteSpace(currentMapId))
        {
            return currentMapId;
        }

        return WorldMapManager.Instance != null
            ? WorldMapManager.Instance.StartingMapId
            : "1-1";
    }

    private void OnCurrentWorldMapIdChanged(FixedString32Bytes previousValue, FixedString32Bytes currentValue)
    {
        OnWorldMapIdChanged?.Invoke(
            WorldMapCatalog.NormalizeMapId(previousValue.ToString()),
            WorldMapCatalog.NormalizeMapId(currentValue.ToString()));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestMapTransitionServerRpc(MapTransitionDirection direction)
    {
        if (!IsServer || IsDead)
        {
            return;
        }

        if (WorldMapManager.Instance == null ||
            !WorldMapManager.Instance.TryResolveTravel(this, direction, out string destinationMapId, out Vector3 destinationPosition, out Quaternion destinationRotation))
        {
            return;
        }

        if (TryGetComponent(out NavMeshAgent navMeshAgent) && navMeshAgent.enabled)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.Warp(destinationPosition);
        }
        else
        {
            transform.position = destinationPosition;
        }

        transform.rotation = destinationRotation;
        SetCurrentWorldMapIdServer(destinationMapId);
    }
}
