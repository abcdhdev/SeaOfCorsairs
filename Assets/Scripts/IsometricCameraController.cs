using UnityEngine;
using UnityEngine.InputSystem;
using Unity.AI.Navigation;
using UnityEngine.Serialization;

public class IsometricCameraController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Movement speed of the camera in world units per second.")]
    [SerializeField] private float moveSpeed = 10f;

    [Header("Input")]
    [Tooltip("Reference to the Input Action for movement (e.g., Player/Move).")]
    [SerializeField] private InputActionReference moveAction;
    
    [Tooltip("Offset from the target when following.")]
    [SerializeField] private Vector3 followOffset = new Vector3(-10f, 10f, -10f); // Example offset, adjust as needed

    [Header("Zoom")]
    [Tooltip("Perspective camera to zoom. If not set, the Camera on this GameObject is used.")]
    [SerializeField] private Camera zoomCamera;
    [FormerlySerializedAs("minOrthographicSize")]
    [SerializeField, Min(1f)] private float minFieldOfView = 10f;
    [FormerlySerializedAs("maxOrthographicSize")]
    [SerializeField, Min(1f)] private float maxFieldOfView = 25f;

    [Header("Movement Bounds")]
    [Tooltip("When enabled, camera XZ movement is clamped to playable bounds.")]
    [SerializeField] private bool limitMovementToPlayableArea = true;
    [Tooltip("Try to read playable bounds from a NavMeshSurface set to Collect Objects = Volume.")]
    [SerializeField] private bool autoResolveBoundsFromNavMeshSurface = true;
    [Tooltip("Preferred NavMeshSurface object name when multiple surfaces exist.")]
    [SerializeField] private string navMeshSurfaceObjectName = "NavMesh";
    [Tooltip("Fallback minimum world XZ bounds when auto-resolve is unavailable.")]
    [SerializeField] private Vector2 fallbackBoundsMin = new Vector2(-225f, -225f);
    [Tooltip("Fallback maximum world XZ bounds when auto-resolve is unavailable.")]
    [SerializeField] private Vector2 fallbackBoundsMax = new Vector2(225f, 225f);
    [Tooltip("Inset from the playable edges in world units.")]
    [SerializeField, Min(0f)] private float boundsInset = 0f;
    [Tooltip("World-space Y height of the plane used to clamp the camera view footprint.")]
    [SerializeField] private float boundsProjectionHeight = 0f;

    private bool isFollowing = false;
    [SerializeField] public Transform target;
    private Camera cachedZoomCamera;

    private bool movementBoundsResolved;
    private float movementMinX;
    private float movementMaxX;
    private float movementMinZ;
    private float movementMaxZ;

    private void Awake()
    {
        // Subscribe to player spawn in Awake - static events survive instance creation
        Player.LocalPlayerSpawned += OnLocalPlayerSpawned;
        ResolveMovementBounds();
    }

    private void Start()
    {
        ClampFieldOfView();
        
        // Check if player already spawned (we subscribed after it happened)
        if (Player.LocalPlayer != null)
        {
            target = Player.LocalPlayer.transform;
        }
    }

    private void OnDestroy()
    {
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
    }

    private void OnLocalPlayerSpawned(Transform player)
    {
        target = player;
        Debug.Log($"IsometricCameraController: Local player set to {player.name}");
        CenterOnTarget();
    }

    private void LateUpdate()
    {
        if (isFollowing && target != null)
        {
            transform.position = ClampToPlayableArea(target.position + followOffset);
        }
    }
    
    private void Update()
    {
        MoveCamera();
    }

    public void CenterOnTarget()
    {
        // Lazy-fetch target if not set
        if (target == null && Player.LocalPlayer != null)
        {
            target = Player.LocalPlayer.transform;
        }
        
        if (target == null)
        {
            Debug.LogWarning("IsometricCameraController: Cannot center, target is null!");
            return;
        }
        Debug.Log($"IsometricCameraController: Centering on {target.name}");
        isFollowing = true;
        transform.position = ClampToPlayableArea(target.position + followOffset);
    }

    public float MinZoom => Mathf.Min(minFieldOfView, maxFieldOfView);
    public float MaxZoom => Mathf.Max(minFieldOfView, maxFieldOfView);

    public bool SupportsZoom()
    {
        Camera targetCamera = ResolveZoomCamera();
        return targetCamera != null && !targetCamera.orthographic;
    }

    public float GetZoom()
    {
        Camera targetCamera = ResolveZoomCamera();
        return targetCamera != null ? targetCamera.fieldOfView : MinZoom;
    }

    public void SetZoom(float fieldOfView)
    {
        Camera targetCamera = ResolveZoomCamera();
        if (targetCamera == null || targetCamera.orthographic)
        {
            return;
        }

        targetCamera.fieldOfView = Mathf.Clamp(fieldOfView, MinZoom, MaxZoom);
    }

    private Camera ResolveZoomCamera()
    {
        if (zoomCamera != null)
        {
            cachedZoomCamera = zoomCamera;
            return zoomCamera;
        }

        if (cachedZoomCamera == null)
        {
            cachedZoomCamera = GetComponent<Camera>();
            if (cachedZoomCamera == null)
            {
                cachedZoomCamera = Camera.main;
            }
        }

        return cachedZoomCamera;
    }

    private void ClampFieldOfView()
    {
        Camera targetCamera = ResolveZoomCamera();
        if (targetCamera == null || targetCamera.orthographic)
        {
            return;
        }

        targetCamera.fieldOfView = Mathf.Clamp(targetCamera.fieldOfView, MinZoom, MaxZoom);
    }

    private void MoveCamera()
    {
        ResolveMovementBounds();

        // Keep the camera responsive (follow/center) but ignore manual panning while meta UI is active.
        if (LoginOverlayController.IsMetaUiActive || ChatController.IsChatInputFocused)
        {
            return;
        }

        Vector2 input = Vector2.zero;

        // Read input if action is set
        if (moveAction != null && moveAction.action != null)
        {
            input = moveAction.action.ReadValue<Vector2>();
        }
        else
        {
             return;
        }

        // Check for input to break following
        if (input.sqrMagnitude > 0.01f)
        {
            if (isFollowing) Debug.Log("IsometricCameraController: follow broken by input " + input);
            isFollowing = false;
        }

        if (isFollowing) return;

        // Calculate movement vector
        // We want X input to move world X, and Y (vertical input) to move world Z.
        Vector3 movement = new Vector3(input.x, 0f, input.y);

        if (movement.sqrMagnitude > 1f)
        {
            movement.Normalize();
        }

        // Apply movement relative to World Space
        transform.Translate(movement * moveSpeed * Time.deltaTime, Space.World);
        transform.position = ClampToPlayableArea(transform.position);
    }

    private Vector3 ClampToPlayableArea(Vector3 position)
    {
        if (!limitMovementToPlayableArea)
        {
            return position;
        }

        if (!movementBoundsResolved)
        {
            ResolveMovementBounds();
            if (!movementBoundsResolved)
            {
                return position;
            }
        }

        float playableMinX = movementMinX + boundsInset;
        float playableMaxX = movementMaxX - boundsInset;
        float playableMinZ = movementMinZ + boundsInset;
        float playableMaxZ = movementMaxZ - boundsInset;

        if (playableMinX > playableMaxX)
        {
            float midX = (movementMinX + movementMaxX) * 0.5f;
            playableMinX = midX;
            playableMaxX = midX;
        }

        if (playableMinZ > playableMaxZ)
        {
            float midZ = (movementMinZ + movementMaxZ) * 0.5f;
            playableMinZ = midZ;
            playableMaxZ = midZ;
        }

        // Clamp by projected viewport footprint (not camera pivot), so tilted isometric
        // cameras remain inside bounds based on what they actually see.
        if (!TryGetProjectedFootprint(position, out float viewMinX, out float viewMaxX, out float viewMinZ, out float viewMaxZ))
        {
            // Fallback to center clamping when projection is unavailable.
            position.x = Mathf.Clamp(position.x, playableMinX, playableMaxX);
            position.z = Mathf.Clamp(position.z, playableMinZ, playableMaxZ);
            return position;
        }

        float dx = ComputeAxisCorrection(viewMinX, viewMaxX, playableMinX, playableMaxX);
        float dz = ComputeAxisCorrection(viewMinZ, viewMaxZ, playableMinZ, playableMaxZ);

        position.x += dx;
        position.z += dz;
        return position;
    }

    private static float ComputeAxisCorrection(float viewMin, float viewMax, float playableMin, float playableMax)
    {
        float playableSpan = playableMax - playableMin;
        float viewSpan = viewMax - viewMin;

        if (viewSpan > playableSpan)
        {
            // View is larger than playable span: center as best-effort.
            return ((playableMin + playableMax) * 0.5f) - ((viewMin + viewMax) * 0.5f);
        }

        if (viewMin < playableMin)
        {
            return playableMin - viewMin;
        }

        if (viewMax > playableMax)
        {
            return playableMax - viewMax;
        }

        return 0f;
    }

    private bool TryGetProjectedFootprint(Vector3 cameraPosition, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;

        Camera targetCamera = ResolveZoomCamera();
        if (targetCamera == null)
        {
            return false;
        }

        Transform cameraTransform = targetCamera.transform;
        Vector3 originalPosition = cameraTransform.position;
        bool changedPosition = (originalPosition - cameraPosition).sqrMagnitude > 0f;

        if (changedPosition)
        {
            cameraTransform.position = cameraPosition;
        }

        Plane projectionPlane = new Plane(Vector3.up, new Vector3(0f, boundsProjectionHeight, 0f));
        Vector3 bottomLeft = Vector3.zero;
        Vector3 bottomRight = Vector3.zero;
        Vector3 topRight = Vector3.zero;
        Vector3 topLeft = Vector3.zero;
        bool success =
            TryProjectViewportPointToPlane(targetCamera, projectionPlane, 0f, 0f, out bottomLeft) &&
            TryProjectViewportPointToPlane(targetCamera, projectionPlane, 1f, 0f, out bottomRight) &&
            TryProjectViewportPointToPlane(targetCamera, projectionPlane, 1f, 1f, out topRight) &&
            TryProjectViewportPointToPlane(targetCamera, projectionPlane, 0f, 1f, out topLeft);

        if (changedPosition)
        {
            cameraTransform.position = originalPosition;
        }

        if (!success)
        {
            return false;
        }

        minX = Mathf.Min(bottomLeft.x, bottomRight.x, topRight.x, topLeft.x);
        maxX = Mathf.Max(bottomLeft.x, bottomRight.x, topRight.x, topLeft.x);
        minZ = Mathf.Min(bottomLeft.z, bottomRight.z, topRight.z, topLeft.z);
        maxZ = Mathf.Max(bottomLeft.z, bottomRight.z, topRight.z, topLeft.z);
        return true;
    }

    private static bool TryProjectViewportPointToPlane(Camera camera, Plane plane, float viewportX, float viewportY, out Vector3 point)
    {
        Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
        if (plane.Raycast(ray, out float distance) && distance >= 0f)
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = default;
        return false;
    }

    private void ResolveMovementBounds()
    {
        if (!limitMovementToPlayableArea)
        {
            movementBoundsResolved = false;
            return;
        }

        if (autoResolveBoundsFromNavMeshSurface && TryResolveBoundsFromNavMeshSurface())
        {
            ConstrainResolvedBoundsToTerrain();
            return;
        }

        if (TryResolveBoundsFromTerrain())
        {
            return;
        }

        // Fallback to manual bounds when no suitable NavMeshSurface is available.
        movementMinX = Mathf.Min(fallbackBoundsMin.x, fallbackBoundsMax.x);
        movementMaxX = Mathf.Max(fallbackBoundsMin.x, fallbackBoundsMax.x);
        movementMinZ = Mathf.Min(fallbackBoundsMin.y, fallbackBoundsMax.y);
        movementMaxZ = Mathf.Max(fallbackBoundsMin.y, fallbackBoundsMax.y);
        movementBoundsResolved = true;
    }

    private bool TryResolveBoundsFromNavMeshSurface()
    {
        NavMeshSurface[] surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces == null || surfaces.Length == 0)
        {
            return false;
        }

        NavMeshSurface selectedSurface = SelectNavMeshSurface(surfaces);
        if (selectedSurface == null || selectedSurface.collectObjects != CollectObjects.Volume)
        {
            return false;
        }

        Bounds worldBounds = GetSurfaceWorldBounds(selectedSurface);
        if (worldBounds.size.x <= 0.01f || worldBounds.size.z <= 0.01f)
        {
            return false;
        }

        movementMinX = worldBounds.min.x;
        movementMaxX = worldBounds.max.x;
        movementMinZ = worldBounds.min.z;
        movementMaxZ = worldBounds.max.z;
        movementBoundsResolved = true;
        return true;
    }

    private void ConstrainResolvedBoundsToTerrain()
    {
        if (!TryGetCombinedTerrainBounds(out float terrainMinX, out float terrainMaxX, out float terrainMinZ, out float terrainMaxZ))
        {
            return;
        }

        // Keep auto-resolved playable bounds inside the rendered terrain footprint.
        movementMinX = Mathf.Max(movementMinX, terrainMinX);
        movementMaxX = Mathf.Min(movementMaxX, terrainMaxX);
        movementMinZ = Mathf.Max(movementMinZ, terrainMinZ);
        movementMaxZ = Mathf.Min(movementMaxZ, terrainMaxZ);

        if (movementMinX > movementMaxX || movementMinZ > movementMaxZ)
        {
            movementMinX = terrainMinX;
            movementMaxX = terrainMaxX;
            movementMinZ = terrainMinZ;
            movementMaxZ = terrainMaxZ;
        }
    }

    private bool TryResolveBoundsFromTerrain()
    {
        if (!TryGetCombinedTerrainBounds(out float terrainMinX, out float terrainMaxX, out float terrainMinZ, out float terrainMaxZ))
        {
            return false;
        }

        movementMinX = terrainMinX;
        movementMaxX = terrainMaxX;
        movementMinZ = terrainMinZ;
        movementMaxZ = terrainMaxZ;
        movementBoundsResolved = true;
        return true;
    }

    private static bool TryGetCombinedTerrainBounds(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;

        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            return false;
        }

        bool foundTerrain = false;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || !terrain.isActiveAndEnabled || terrain.terrainData == null)
            {
                continue;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            float terrainMinX = terrainPosition.x;
            float terrainMaxX = terrainPosition.x + terrainSize.x;
            float terrainMinZ = terrainPosition.z;
            float terrainMaxZ = terrainPosition.z + terrainSize.z;

            if (!foundTerrain)
            {
                minX = terrainMinX;
                maxX = terrainMaxX;
                minZ = terrainMinZ;
                maxZ = terrainMaxZ;
                foundTerrain = true;
                continue;
            }

            minX = Mathf.Min(minX, terrainMinX);
            maxX = Mathf.Max(maxX, terrainMaxX);
            minZ = Mathf.Min(minZ, terrainMinZ);
            maxZ = Mathf.Max(maxZ, terrainMaxZ);
        }

        return foundTerrain;
    }

    private NavMeshSurface SelectNavMeshSurface(NavMeshSurface[] surfaces)
    {
        NavMeshSurface fallback = null;
        for (int i = 0; i < surfaces.Length; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null || !surface.isActiveAndEnabled)
            {
                continue;
            }

            if (surface.collectObjects != CollectObjects.Volume)
            {
                continue;
            }

            fallback ??= surface;

            if (!string.IsNullOrWhiteSpace(navMeshSurfaceObjectName) &&
                string.Equals(surface.gameObject.name, navMeshSurfaceObjectName, System.StringComparison.OrdinalIgnoreCase))
            {
                return surface;
            }
        }

        return fallback;
    }

    private static Bounds GetSurfaceWorldBounds(NavMeshSurface surface)
    {
        Bounds localBounds = new Bounds(surface.center, surface.size);
        Matrix4x4 localToWorld = Matrix4x4.TRS(surface.transform.position, surface.transform.rotation, Vector3.one);
        return GetWorldBounds(localToWorld, localBounds);
    }

    private static Bounds GetWorldBounds(Matrix4x4 localToWorld, Bounds bounds)
    {
        Vector3 absAxisX = Abs(localToWorld.MultiplyVector(Vector3.right));
        Vector3 absAxisY = Abs(localToWorld.MultiplyVector(Vector3.up));
        Vector3 absAxisZ = Abs(localToWorld.MultiplyVector(Vector3.forward));
        Vector3 worldPosition = localToWorld.MultiplyPoint(bounds.center);
        Vector3 worldSize = absAxisX * bounds.size.x + absAxisY * bounds.size.y + absAxisZ * bounds.size.z;
        return new Bounds(worldPosition, worldSize);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
    
    private void OnEnable()
    {
        if (moveAction != null && moveAction.action != null)
            moveAction.action.Enable();
    }

    private void OnDisable()
    {
        if (moveAction != null && moveAction.action != null)
            moveAction.action.Disable();
    }
}
