using UnityEngine;

public struct WorldMapTravelDebugInfo
{
    public bool HasData;
    public string Trigger;
    public string SourceMapId;
    public string DestinationMapId;
    public string ResolutionStrategy;
    public string Note;
    public string MovementProbeNote;
    public MapTransitionDirection Direction;
    public Vector3 StartWorldPosition;
    public Vector3 RequestedWorldPosition;
    public Vector3 RequestedLocalPosition;
    public Vector3 FinalWorldPosition;
    public Vector3 FinalLocalPosition;
    public Vector3 MovementProbeTargetWorldPosition;
    public Vector3 MovementProbeTargetLocalPosition;
    public bool RequestedInBounds;
    public bool FinalInBounds;
    public bool AgentPresent;
    public bool AgentEnabled;
    public bool AgentOnNavMeshAfterTeleport;
    public bool MovementProbeSucceeded;
}
