using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public partial class Player
{
    private const float WorldMapNavMeshSampleDistance = 128f;
    private const float WorldMapTeleportBoundsPadding = 1f;
    private const float WorldMapPreferredTeleportSampleDistance = 16f;

    private readonly NetworkVariable<FixedString32Bytes> m_currentWorldMapId = new(
        new FixedString32Bytes("1-1"),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public event Action<string, string> OnWorldMapIdChanged = delegate { };

    public string CurrentWorldMapId => WorldMapCatalog.NormalizeMapId(m_currentWorldMapId.Value.ToString());
    public WorldMapTravelDebugInfo LastWorldMapTravelDebugInfo => lastWorldMapTravelDebugInfo;

    private WorldMapTravelDebugInfo lastWorldMapTravelDebugInfo;

    public bool RequestMapTransition(MapTransitionDirection direction)
    {
        if (!IsOwner || !IsSpawned || IsDead)
        {
            return false;
        }

        RequestMapTransitionServerRpc(direction);
        return true;
    }

    public bool DebugForceAdjacentMapTransition(MapTransitionDirection direction, float normalizedOrthogonal = 0.5f)
    {
        if (!IsOwner || !IsSpawned || IsDead)
        {
            return false;
        }

        Debug.Log($"WorldMap debug: forcing adjacent transition {direction} from {CurrentWorldMapId} with orthogonal {normalizedOrthogonal:0.00}.", this);
        DebugForceAdjacentMapTransitionServerRpc(direction, Mathf.Clamp01(normalizedOrthogonal));
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
        string previousMapId = WorldMapCatalog.NormalizeMapId(previousValue.ToString());
        string currentMapId = WorldMapCatalog.NormalizeMapId(currentValue.ToString());

        OnWorldMapIdChanged?.Invoke(previousMapId, currentMapId);

        if (IsOwner &&
            !string.Equals(previousMapId, currentMapId, StringComparison.OrdinalIgnoreCase))
        {
            StartCoroutine(CenterLocalCameraAfterWorldMapChange());
        }
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

        WorldMapManager.Instance.TryGetLoadedScene(destinationMapId, out WorldMapSceneAuthoring destinationScene);
        ApplyResolvedWorldMapTransition(direction, destinationMapId, destinationPosition, destinationRotation, destinationScene, "travel-zone");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DebugForceAdjacentMapTransitionServerRpc(MapTransitionDirection direction, float normalizedOrthogonal)
    {
        if (!IsServer || IsDead)
        {
            return;
        }

        if (WorldMapManager.Instance == null ||
            !WorldMapManager.Instance.TryResolveDebugTravel(this, direction, normalizedOrthogonal, out string destinationMapId, out Vector3 destinationPosition, out Quaternion destinationRotation))
        {
            PublishWorldMapTravelDebug(new WorldMapTravelDebugInfo
            {
                HasData = true,
                Trigger = "debug-force",
                SourceMapId = CurrentWorldMapId,
                DestinationMapId = string.Empty,
                Direction = direction,
                StartWorldPosition = transform.position,
                Note = "Failed to resolve adjacent debug travel."
            });
            return;
        }

        WorldMapManager.Instance.TryGetLoadedScene(destinationMapId, out WorldMapSceneAuthoring destinationScene);
        ApplyResolvedWorldMapTransition(direction, destinationMapId, destinationPosition, destinationRotation, destinationScene, "debug-force");
    }

    private void ApplyResolvedWorldMapTransition(
        MapTransitionDirection direction,
        string destinationMapId,
        Vector3 desiredPosition,
        Quaternion rotation,
        WorldMapSceneAuthoring destinationScene,
        string trigger)
    {
        WorldMapTravelDebugInfo debugInfo = CreateWorldMapTravelDebugInfo(direction, destinationMapId, desiredPosition, destinationScene, trigger);
        Vector3 resolvedPosition = ResolveWorldMapNavMeshPosition(desiredPosition, destinationScene, ref debugInfo);

        bool hasAgent = TryGetComponent(out NavMeshAgent navMeshAgent);
        debugInfo.AgentPresent = hasAgent;
        debugInfo.AgentEnabled = hasAgent && navMeshAgent.enabled;

        if (hasAgent && navMeshAgent.enabled)
        {
            navMeshAgent.ResetPath();
            if (!navMeshAgent.Warp(resolvedPosition))
            {
                Vector3 fallbackPosition = ResolveWorldMapHardFallbackPosition(desiredPosition, destinationScene);
                if (resolvedPosition != fallbackPosition && navMeshAgent.Warp(fallbackPosition))
                {
                    resolvedPosition = fallbackPosition;
                    debugInfo.Note = AppendDebugNote(debugInfo.Note, "Primary warp failed; used respawn/clamped warp fallback.");
                }
                else
                {
                    transform.position = fallbackPosition;
                    resolvedPosition = fallbackPosition;
                    debugInfo.ResolutionStrategy = string.IsNullOrWhiteSpace(debugInfo.ResolutionStrategy)
                        ? "transform-fallback"
                        : $"{debugInfo.ResolutionStrategy} -> transform-fallback";
                    debugInfo.Note = AppendDebugNote(debugInfo.Note, "NavMeshAgent warp failed; moved transform directly.");
                }
            }
        }
        else
        {
            transform.position = resolvedPosition;
            debugInfo.Note = AppendDebugNote(debugInfo.Note, hasAgent ? "NavMeshAgent is disabled during teleport." : "Player has no NavMeshAgent.");
        }

        transform.rotation = rotation;
        SetCurrentWorldMapIdServer(destinationMapId);
        FinalizeWorldMapTravelDebug(ref debugInfo, resolvedPosition, destinationScene, navMeshAgent);
        PublishWorldMapTravelDebug(debugInfo);
    }

    private void TeleportServerToWorldMapPosition(Vector3 desiredPosition, Quaternion rotation, WorldMapSceneAuthoring destinationScene = null)
    {
        WorldMapTravelDebugInfo debugInfo = default;
        Vector3 resolvedPosition = ResolveWorldMapNavMeshPosition(desiredPosition, destinationScene, ref debugInfo);

        if (TryGetComponent(out NavMeshAgent navMeshAgent) && navMeshAgent.enabled)
        {
            navMeshAgent.ResetPath();
            if (!navMeshAgent.Warp(resolvedPosition))
            {
                Vector3 fallbackPosition = ResolveWorldMapHardFallbackPosition(desiredPosition, destinationScene);
                if (resolvedPosition != fallbackPosition && navMeshAgent.Warp(fallbackPosition))
                {
                    resolvedPosition = fallbackPosition;
                }
                else
                {
                    transform.position = fallbackPosition;
                    resolvedPosition = fallbackPosition;
                }
            }
        }
        else
        {
            transform.position = resolvedPosition;
        }

        transform.rotation = rotation;
    }

    private WorldMapTravelDebugInfo CreateWorldMapTravelDebugInfo(
        MapTransitionDirection direction,
        string destinationMapId,
        Vector3 desiredPosition,
        WorldMapSceneAuthoring destinationScene,
        string trigger)
    {
        WorldMapTravelDebugInfo debugInfo = new()
        {
            HasData = true,
            Trigger = trigger ?? string.Empty,
            SourceMapId = CurrentWorldMapId,
            DestinationMapId = WorldMapCatalog.NormalizeMapId(destinationMapId),
            Direction = direction,
            StartWorldPosition = transform.position,
            RequestedWorldPosition = desiredPosition,
            RequestedLocalPosition = destinationScene != null ? destinationScene.WorldToGameplayLocal(desiredPosition) : desiredPosition,
            RequestedInBounds = destinationScene == null || destinationScene.IsWithinPlayableBounds(desiredPosition, WorldMapTeleportBoundsPadding)
        };

        if (destinationScene == null)
        {
            debugInfo.Note = "Destination scene is missing.";
            return debugInfo;
        }

        if (!debugInfo.RequestedInBounds)
        {
            debugInfo.Note = "Requested destination lies outside playable bounds.";
        }

        return debugInfo;
    }

    private void FinalizeWorldMapTravelDebug(
        ref WorldMapTravelDebugInfo debugInfo,
        Vector3 resolvedPosition,
        WorldMapSceneAuthoring destinationScene,
        NavMeshAgent navMeshAgent)
    {
        debugInfo.FinalWorldPosition = resolvedPosition;
        debugInfo.FinalLocalPosition = destinationScene != null
            ? destinationScene.WorldToGameplayLocal(resolvedPosition)
            : resolvedPosition;
        debugInfo.FinalInBounds = destinationScene == null || destinationScene.IsWithinPlayableBounds(resolvedPosition, WorldMapTeleportBoundsPadding);
        debugInfo.AgentOnNavMeshAfterTeleport = navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;

        debugInfo.MovementProbeSucceeded = TryRunWorldMapMovementProbe(
            navMeshAgent,
            resolvedPosition,
            destinationScene,
            out Vector3 movementProbeTargetWorldPosition,
            out Vector3 movementProbeTargetLocalPosition,
            out string movementProbeNote);
        debugInfo.MovementProbeTargetWorldPosition = movementProbeTargetWorldPosition;
        debugInfo.MovementProbeTargetLocalPosition = movementProbeTargetLocalPosition;
        debugInfo.MovementProbeNote = movementProbeNote;

        if (!debugInfo.FinalInBounds)
        {
            debugInfo.Note = AppendDebugNote(debugInfo.Note, "Final landed position is still outside playable bounds.");
        }
    }

    private static bool TryRunWorldMapMovementProbe(
        NavMeshAgent navMeshAgent,
        Vector3 currentPosition,
        WorldMapSceneAuthoring destinationScene,
        out Vector3 movementProbeTargetWorldPosition,
        out Vector3 movementProbeTargetLocalPosition,
        out string movementProbeNote)
    {
        movementProbeTargetWorldPosition = currentPosition;
        movementProbeTargetLocalPosition = destinationScene != null
            ? destinationScene.WorldToGameplayLocal(currentPosition)
            : currentPosition;

        if (destinationScene == null)
        {
            movementProbeNote = "No destination scene available for movement probe.";
            return false;
        }

        if (navMeshAgent == null)
        {
            movementProbeNote = "Player is missing a NavMeshAgent.";
            return false;
        }

        if (!navMeshAgent.enabled)
        {
            movementProbeNote = "NavMeshAgent is disabled after teleport.";
            return false;
        }

        int areaMask = navMeshAgent.areaMask;
        Vector3 probeTarget = ResolveWorldMapHardFallbackPosition(currentPosition, destinationScene);
        if (!NavMesh.SamplePosition(probeTarget, out NavMeshHit targetHit, WorldMapNavMeshSampleDistance, areaMask))
        {
            movementProbeNote = "Could not sample the movement probe target onto the NavMesh.";
            return false;
        }

        movementProbeTargetWorldPosition = targetHit.position;
        movementProbeTargetLocalPosition = destinationScene.WorldToGameplayLocal(targetHit.position);

        if (!navMeshAgent.isOnNavMesh)
        {
            movementProbeNote = "NavMeshAgent is not on a NavMesh after teleport.";
            return false;
        }

        var path = new NavMeshPath();
        if (!navMeshAgent.CalculatePath(targetHit.position, path))
        {
            movementProbeNote = "NavMeshAgent.CalculatePath returned false.";
            return false;
        }

        if (path.status != NavMeshPathStatus.PathComplete)
        {
            movementProbeNote = $"Movement probe path status is {path.status}.";
            return false;
        }

        int cornerCount = path.corners != null ? path.corners.Length : 0;
        movementProbeNote = $"Path complete to probe target ({cornerCount} corners).";
        return true;
    }

    private void PublishWorldMapTravelDebug(WorldMapTravelDebugInfo debugInfo)
    {
        lastWorldMapTravelDebugInfo = debugInfo;

        if (!debugInfo.HasData)
        {
            return;
        }

        Debug.Log(
            $"WorldMap travel debug: trigger={debugInfo.Trigger}, dir={debugInfo.Direction}, " +
            $"{debugInfo.SourceMapId} -> {debugInfo.DestinationMapId}, strategy={debugInfo.ResolutionStrategy}, " +
            $"requestedLocal={FormatWorldMapDebugVector(debugInfo.RequestedLocalPosition)}, " +
            $"finalLocal={FormatWorldMapDebugVector(debugInfo.FinalLocalPosition)}, " +
            $"inBounds={debugInfo.FinalInBounds}, agentOnNavMesh={debugInfo.AgentOnNavMeshAfterTeleport}, " +
            $"moveProbe={debugInfo.MovementProbeSucceeded} ({debugInfo.MovementProbeNote})",
            this);
    }

    private static Vector3 ResolveWorldMapNavMeshPosition(Vector3 desiredPosition, WorldMapSceneAuthoring destinationScene, ref WorldMapTravelDebugInfo debugInfo)
    {
        int walkableAreaMask = SeaSpawnSurfaceUtility.ResolveWalkableAreaMask();

        Vector3 preferredPosition = ResolveWorldMapTeleportFallbackPosition(desiredPosition, destinationScene);
        if (TryResolveWorldMapTeleportPosition(
                preferredPosition,
                WorldMapPreferredTeleportSampleDistance,
                walkableAreaMask,
                destinationScene,
                "requested-preferred",
                out Vector3 preferredSample,
                out string preferredStrategy))
        {
            debugInfo.ResolutionStrategy = preferredStrategy;
            return preferredSample;
        }

        if (TryResolveWorldMapTeleportPosition(
                preferredPosition,
                WorldMapNavMeshSampleDistance,
                walkableAreaMask,
                destinationScene,
                "requested-broad",
                out Vector3 broadSample,
                out string broadStrategy))
        {
            debugInfo.ResolutionStrategy = broadStrategy;
            return broadSample;
        }

        if (destinationScene != null &&
            destinationScene.TryResolveRespawnTransform(out Vector3 respawnPosition, out _))
        {
            Vector3 preferredRespawnPosition = ResolveWorldMapTeleportFallbackPosition(respawnPosition, destinationScene);
            if (TryResolveWorldMapTeleportPosition(
                    preferredRespawnPosition,
                    WorldMapPreferredTeleportSampleDistance,
                    walkableAreaMask,
                    destinationScene,
                    "respawn-preferred",
                    out Vector3 respawnSample,
                    out string respawnPreferredStrategy))
            {
                debugInfo.ResolutionStrategy = respawnPreferredStrategy;
                debugInfo.Note = AppendDebugNote(debugInfo.Note, "Used respawn anchor sample fallback.");
                return respawnSample;
            }

            if (TryResolveWorldMapTeleportPosition(
                    preferredRespawnPosition,
                    WorldMapNavMeshSampleDistance,
                    walkableAreaMask,
                    destinationScene,
                    "respawn-broad",
                    out Vector3 respawnBroadSample,
                    out string respawnBroadStrategy))
            {
                debugInfo.ResolutionStrategy = respawnBroadStrategy;
                debugInfo.Note = AppendDebugNote(debugInfo.Note, "Used respawn anchor sample fallback.");
                return respawnBroadSample;
            }

            debugInfo.ResolutionStrategy = "respawn-fallback";
            debugInfo.Note = AppendDebugNote(debugInfo.Note, "Used respawn anchor fallback without NavMesh sample.");
            return preferredRespawnPosition;
        }

        debugInfo.ResolutionStrategy = "clamped-requested";
        return preferredPosition;
    }

    private static bool TryResolveWorldMapTeleportPosition(
        Vector3 desiredPosition,
        float sampleDistance,
        int walkableAreaMask,
        WorldMapSceneAuthoring destinationScene,
        string strategyPrefix,
        out Vector3 resolvedPosition,
        out string resolutionStrategy)
    {
        if (SeaSpawnSurfaceUtility.TrySampleWaterNavMeshPosition(
                desiredPosition,
                sampleDistance,
                walkableAreaMask,
                out Vector3 waterNavMeshPosition) &&
            IsValidWorldMapTeleportPosition(waterNavMeshPosition, destinationScene))
        {
            resolvedPosition = waterNavMeshPosition;
            resolutionStrategy = $"{strategyPrefix}-water";
            return true;
        }

        if (NavMesh.SamplePosition(
                desiredPosition,
                out NavMeshHit navMeshHit,
                sampleDistance,
                walkableAreaMask) &&
            IsValidWorldMapTeleportPosition(navMeshHit.position, destinationScene))
        {
            resolvedPosition = navMeshHit.position;
            resolutionStrategy = $"{strategyPrefix}-navmesh";
            return true;
        }

        resolvedPosition = default;
        resolutionStrategy = string.Empty;
        return false;
    }

    private static bool IsValidWorldMapTeleportPosition(Vector3 position, WorldMapSceneAuthoring destinationScene)
    {
        return destinationScene == null || destinationScene.IsWithinPlayableBounds(position, WorldMapTeleportBoundsPadding);
    }

    private static Vector3 ResolveWorldMapTeleportFallbackPosition(Vector3 desiredPosition, WorldMapSceneAuthoring destinationScene)
    {
        return destinationScene != null
            ? destinationScene.ClampWorldPositionToPlayableBounds(desiredPosition, WorldMapTeleportBoundsPadding)
            : desiredPosition;
    }

    private static Vector3 ResolveWorldMapHardFallbackPosition(Vector3 desiredPosition, WorldMapSceneAuthoring destinationScene)
    {
        if (destinationScene != null &&
            destinationScene.TryResolveRespawnTransform(out Vector3 respawnPosition, out _))
        {
            return destinationScene.ClampWorldPositionToPlayableBounds(respawnPosition, WorldMapTeleportBoundsPadding);
        }

        return ResolveWorldMapTeleportFallbackPosition(desiredPosition, destinationScene);
    }

    private static string AppendDebugNote(string existingNote, string nextNote)
    {
        if (string.IsNullOrWhiteSpace(nextNote))
        {
            return existingNote ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(existingNote))
        {
            return nextNote;
        }

        return $"{existingNote} {nextNote}";
    }

    private static string FormatWorldMapDebugVector(Vector3 value)
    {
        return $"({value.x:0.0}, {value.y:0.0}, {value.z:0.0})";
    }

    private IEnumerator CenterLocalCameraAfterWorldMapChange()
    {
        yield return null;

        if (!IsOwner)
        {
            yield break;
        }

        IsometricCameraController cameraController = FindFirstObjectByType<IsometricCameraController>();
        if (cameraController == null)
        {
            yield break;
        }

        cameraController.target = transform;
        cameraController.CenterOnTarget();
    }
}
