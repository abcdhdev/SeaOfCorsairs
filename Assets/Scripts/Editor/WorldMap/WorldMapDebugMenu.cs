using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class WorldMapDebugMenu
{
    private const string MenuRoot = "Tools/World Map/Debug/";
    private const double TransitionTimeoutSeconds = 5d;
    private const double MovementTimeoutSeconds = 4d;
    private const float MovementSuccessDistance = 2f;
    private const float MovementSampleDistance = 16f;
    private const float MovementInsetPadding = 4f;

    private static WorldMapMovementSmokeTest activeSmokeTest;

    [MenuItem(MenuRoot + "Run Transition Smoke Test")]
    private static void RunTransitionSmokeTest()
    {
        if (!TryGetLocalPlayer(out Player player, out string reason))
        {
            Debug.LogWarning($"WorldMap debug: smoke test could not start. {reason}");
            return;
        }

        if (!TryResolveFirstAdjacentDirection(player, out MapTransitionDirection direction, out string destinationMapId))
        {
            Debug.LogWarning($"WorldMap debug: smoke test could not find an adjacent map for '{player.CurrentWorldMapId}'.", player);
            return;
        }

        CancelSmokeTest("restarting");

        activeSmokeTest = new WorldMapMovementSmokeTest
        {
            Player = player,
            ExpectedDestinationMapId = destinationMapId,
            Direction = direction,
            StartedAt = EditorApplication.timeSinceStartup
        };

        EditorApplication.update -= TickSmokeTest;
        EditorApplication.update += TickSmokeTest;

        Debug.Log($"WorldMap debug: starting transition smoke test {player.CurrentWorldMapId} -> {destinationMapId} via {direction}.", player);
        if (!player.DebugForceAdjacentMapTransition(direction))
        {
            CancelSmokeTest("force transition request failed");
        }
    }

    [MenuItem(MenuRoot + "Run Transition Smoke Test", true)]
    private static bool ValidateRunTransitionSmokeTest()
    {
        return CanRunDebugAction();
    }

    [MenuItem(MenuRoot + "Cancel Transition Smoke Test")]
    private static void CancelTransitionSmokeTestMenu()
    {
        CancelSmokeTest("canceled from menu");
    }

    [MenuItem(MenuRoot + "Cancel Transition Smoke Test", true)]
    private static bool ValidateCancelTransitionSmokeTestMenu()
    {
        return activeSmokeTest != null;
    }

    [MenuItem(MenuRoot + "Force Transition/North")]
    private static void ForceNorth()
    {
        ForceTransition(MapTransitionDirection.North);
    }

    [MenuItem(MenuRoot + "Force Transition/North", true)]
    private static bool ValidateForceNorth()
    {
        return CanRunDebugAction();
    }

    [MenuItem(MenuRoot + "Force Transition/East")]
    private static void ForceEast()
    {
        ForceTransition(MapTransitionDirection.East);
    }

    [MenuItem(MenuRoot + "Force Transition/East", true)]
    private static bool ValidateForceEast()
    {
        return CanRunDebugAction();
    }

    [MenuItem(MenuRoot + "Force Transition/South")]
    private static void ForceSouth()
    {
        ForceTransition(MapTransitionDirection.South);
    }

    [MenuItem(MenuRoot + "Force Transition/South", true)]
    private static bool ValidateForceSouth()
    {
        return CanRunDebugAction();
    }

    [MenuItem(MenuRoot + "Force Transition/West")]
    private static void ForceWest()
    {
        ForceTransition(MapTransitionDirection.West);
    }

    [MenuItem(MenuRoot + "Force Transition/West", true)]
    private static bool ValidateForceWest()
    {
        return CanRunDebugAction();
    }

    [MenuItem(MenuRoot + "Move Local Player To Probe Target")]
    private static void MoveLocalPlayerToProbeTarget()
    {
        if (!TryGetLocalPlayer(out Player player, out string reason))
        {
            Debug.LogWarning($"WorldMap debug: move probe request failed. {reason}");
            return;
        }

        if (!TryGetComponent(player, out ClickToMove clickToMove, out reason))
        {
            Debug.LogWarning($"WorldMap debug: move probe request failed. {reason}", player);
            return;
        }

        Vector3 target = ResolveMovementTarget(player, out string note);
        Debug.Log($"WorldMap debug: issuing move probe toward {target} ({note}).", player);
        clickToMove.OnClick(target);
    }

    [MenuItem(MenuRoot + "Move Local Player To Probe Target", true)]
    private static bool ValidateMoveLocalPlayerToProbeTarget()
    {
        return CanRunDebugAction();
    }

    private static void ForceTransition(MapTransitionDirection direction)
    {
        if (!TryGetLocalPlayer(out Player player, out string reason))
        {
            Debug.LogWarning($"WorldMap debug: could not force transition {direction}. {reason}");
            return;
        }

        Debug.Log($"WorldMap debug: forcing transition {direction} from {player.CurrentWorldMapId} via editor menu.", player);
        if (!player.DebugForceAdjacentMapTransition(direction))
        {
            Debug.LogWarning($"WorldMap debug: transition request {direction} was rejected.", player);
        }
    }

    private static void TickSmokeTest()
    {
        if (activeSmokeTest == null)
        {
            EditorApplication.update -= TickSmokeTest;
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            CancelSmokeTest("play mode ended");
            return;
        }

        Player player = activeSmokeTest.Player;
        if (player == null)
        {
            CancelSmokeTest("local player no longer exists");
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (!activeSmokeTest.MoveIssued)
        {
            if (!string.Equals(player.CurrentWorldMapId, activeSmokeTest.ExpectedDestinationMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                if (now - activeSmokeTest.StartedAt > TransitionTimeoutSeconds)
                {
                    WorldMapTravelDebugInfo lastInfo = player.LastWorldMapTravelDebugInfo;
                    CancelSmokeTest(
                        $"transition timeout waiting for '{activeSmokeTest.ExpectedDestinationMapId}'. " +
                        $"Current map: '{player.CurrentWorldMapId}'. Last note: '{lastInfo.Note}'.");
                }

                return;
            }

            if (!TryGetComponent(player, out ClickToMove clickToMove, out string reason))
            {
                CancelSmokeTest($"movement probe could not start. {reason}");
                return;
            }

            activeSmokeTest.MoveIssued = true;
            activeSmokeTest.MoveIssuedAt = now;
            activeSmokeTest.MoveStartPosition = player.transform.position;
            activeSmokeTest.MoveTargetPosition = ResolveMovementTarget(player, out string note);

            Debug.Log(
                $"WorldMap debug: transition smoke test issuing movement probe toward {activeSmokeTest.MoveTargetPosition} ({note}).",
                player);
            clickToMove.OnClick(activeSmokeTest.MoveTargetPosition);
            return;
        }

        float movedDistance = Vector3.Distance(player.transform.position, activeSmokeTest.MoveStartPosition);
        if (movedDistance >= MovementSuccessDistance)
        {
            Debug.Log(
                $"WorldMap debug: smoke test passed. Player moved {movedDistance:0.0}m on map '{player.CurrentWorldMapId}' after transition.",
                player);
            CancelSmokeTest("completed");
            return;
        }

        if (now - activeSmokeTest.MoveIssuedAt <= MovementTimeoutSeconds)
        {
            return;
        }

        string agentSummary = "NavMeshAgent unavailable";
        if (player.TryGetComponent(out NavMeshAgent navMeshAgent))
        {
            string remainingDistance = navMeshAgent.enabled && navMeshAgent.isOnNavMesh
                ? navMeshAgent.remainingDistance.ToString("0.0")
                : "--";
            agentSummary =
                $"enabled={navMeshAgent.enabled}, isOnNavMesh={navMeshAgent.isOnNavMesh}, hasPath={navMeshAgent.hasPath}, " +
                $"remainingDistance={remainingDistance}, velocity={navMeshAgent.velocity.magnitude:0.0}";
        }

        WorldMapTravelDebugInfo debugInfo = player.LastWorldMapTravelDebugInfo;
        CancelSmokeTest(
            $"movement timeout after transition. Moved {movedDistance:0.0}m. Agent: {agentSummary}. " +
            $"Travel note: '{debugInfo.Note}'. Probe note: '{debugInfo.MovementProbeNote}'.");
    }

    private static Vector3 ResolveMovementTarget(Player player, out string note)
    {
        WorldMapTravelDebugInfo debugInfo = player.LastWorldMapTravelDebugInfo;
        if (debugInfo.HasData && debugInfo.MovementProbeSucceeded)
        {
            note = $"using travel debug probe target on {debugInfo.DestinationMapId}";
            return debugInfo.MovementProbeTargetWorldPosition;
        }

        if (WorldMapManager.Instance != null &&
            WorldMapManager.Instance.TryGetCurrentScene(player, out WorldMapSceneAuthoring currentScene) &&
            currentScene != null)
        {
            Vector3 localPosition = currentScene.WorldToGameplayLocal(player.transform.position);
            Vector3 desiredLocalPosition = localPosition + new Vector3(12f, 0f, 12f);
            Vector3 desiredWorldPosition = currentScene.GameplayLocalToWorld(desiredLocalPosition);
            Vector3 clampedWorldPosition = currentScene.ClampWorldPositionToPlayableBounds(desiredWorldPosition, MovementInsetPadding);

            int areaMask = SeaSpawnSurfaceUtility.ResolveWalkableAreaMask();
            if (SeaSpawnSurfaceUtility.TrySampleWaterNavMeshPosition(
                    clampedWorldPosition,
                    MovementSampleDistance,
                    areaMask,
                    out Vector3 waterSample))
            {
                note = "using clamped water NavMesh sample";
                return waterSample;
            }

            if (NavMesh.SamplePosition(clampedWorldPosition, out NavMeshHit navMeshHit, MovementSampleDistance, areaMask))
            {
                note = "using clamped NavMesh sample";
                return navMeshHit.position;
            }

            note = "using clamped in-bounds world position";
            return clampedWorldPosition;
        }

        note = "using local player position fallback";
        return player.transform.position;
    }

    private static bool TryResolveFirstAdjacentDirection(
        Player player,
        out MapTransitionDirection direction,
        out string destinationMapId)
    {
        direction = default;
        destinationMapId = string.Empty;

        WorldMapManager manager = WorldMapManager.Instance;
        if (manager == null)
        {
            return false;
        }

        MapTransitionDirection[] directions =
        {
            MapTransitionDirection.East,
            MapTransitionDirection.North,
            MapTransitionDirection.South,
            MapTransitionDirection.West
        };

        for (int index = 0; index < directions.Length; index++)
        {
            MapTransitionDirection candidateDirection = directions[index];
            if (!manager.TryGetAdjacentDefinition(player.CurrentWorldMapId, candidateDirection, out WorldMapDefinition destination) ||
                destination == null ||
                string.IsNullOrWhiteSpace(destination.MapId))
            {
                continue;
            }

            direction = candidateDirection;
            destinationMapId = destination.MapId;
            return true;
        }

        return false;
    }

    private static bool TryGetLocalPlayer(out Player player, out string reason)
    {
        player = Player.LocalPlayer;
        if (!EditorApplication.isPlaying)
        {
            reason = "Play mode is not active.";
            return false;
        }

        if (player == null)
        {
            reason = "Player.LocalPlayer is not available yet.";
            return false;
        }

        if (!player.IsSpawned)
        {
            reason = "Local player exists but is not spawned.";
            return false;
        }

        if (player.IsDead)
        {
            reason = "Local player is dead.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryGetComponent<T>(Player player, out T component, out string reason) where T : Component
    {
        component = null;
        if (player == null)
        {
            reason = "Player reference is missing.";
            return false;
        }

        if (!player.TryGetComponent(out component) || component == null)
        {
            reason = $"Required component '{typeof(T).Name}' is missing on '{player.name}'.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanRunDebugAction()
    {
        return EditorApplication.isPlaying && Player.LocalPlayer != null && Player.LocalPlayer.IsSpawned && !Player.LocalPlayer.IsDead;
    }

    private static void CancelSmokeTest(string reason)
    {
        if (activeSmokeTest != null && !string.IsNullOrWhiteSpace(reason))
        {
            Debug.Log($"WorldMap debug: transition smoke test {reason}.", activeSmokeTest.Player);
        }

        activeSmokeTest = null;
        EditorApplication.update -= TickSmokeTest;
    }

    private sealed class WorldMapMovementSmokeTest
    {
        public Player Player;
        public string ExpectedDestinationMapId;
        public MapTransitionDirection Direction;
        public double StartedAt;
        public bool MoveIssued;
        public double MoveIssuedAt;
        public Vector3 MoveStartPosition;
        public Vector3 MoveTargetPosition;
    }
}
