using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class MinimapHudController : MonoBehaviour
{
    private enum MarkerKind
    {
        Player,
        Npc
    }

    private sealed class MarkerEntry
    {
        public Component source;
        public Transform target;
        public VisualElement element;
        public MarkerKind kind;
        public float size;
        public bool offscreen;
        public bool seenThisRefresh;
    }

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Transform localPlayerOverride;

    [Header("Map Texture")]
    [SerializeField] private Texture2D mapTextureOverride;

    [Header("Map Bounds (World XZ)")]
    [SerializeField] private bool autoComputeWorldBoundsFromNavMeshSurface = true;
    [SerializeField] private string navMeshSurfaceObjectName = "NavMesh";
    [SerializeField] private bool autoComputeWorldBoundsFromWaypoints = true;
    [SerializeField] private bool forceSquareWorldBounds = true;
    [SerializeField] private Vector2 mapWorldMin = new Vector2(-256f, -256f);
    [SerializeField] private Vector2 mapWorldMax = new Vector2(256f, 256f);
    [SerializeField, Min(0f)] private float waypointBoundsPadding = 40f;
    [SerializeField] private bool invertMapX;
    [SerializeField] private bool invertMapY;

    [Header("Viewport Border")]
    [SerializeField] private bool showViewportBorder = true;
    [SerializeField] private Camera gameplayCameraOverride;
    [SerializeField] private bool projectViewportAtLocalPlayerHeight = true;
    [SerializeField] private float viewportProjectionHeight = 0f;
    [SerializeField] private Color viewportBorderColor = new Color(0.98f, 0.95f, 0.55f, 0.92f);
    [SerializeField, Min(0.5f)] private float viewportBorderWidth = 1.8f;

    [Header("Markers")]
    [SerializeField, Range(0f, 0.35f)] private float edgePadding = 0.08f;
    [SerializeField, Min(0f)] private float markerWorldYOffset = 1f;
    [SerializeField, Min(4f)] private float playerMarkerSize = 11f;
    [SerializeField, Min(4f)] private float npcMarkerSize = 8f;
    [SerializeField, Min(0.1f)] private float actorRefreshInterval = 0.6f;
    [SerializeField] private bool showPlayers = true;
    [SerializeField] private bool showNpcs = true;
    private const string OffscreenMarkerClass = "minimap-marker-offscreen";
    private const string MinimapTextureOutputPath = "Assets/Textures/MinimapNavMesh.png";

    private VisualElement hudRoot;
    private VisualElement minimapRoot;
    private VisualElement minimapRender;
    private VisualElement minimapMarkerLayer;
    private VisualElement minimapCenterReticle;
    private VisualElement minimapViewportBorder;
    private Label minimapModeLabel;
    private Label minimapZoomLabel;

    private readonly Dictionary<int, MarkerEntry> markers = new Dictionary<int, MarkerEntry>(64);
    private readonly List<int> staleMarkerIds = new List<int>(64);
    private readonly Vector2[] viewportOutlinePoints = new Vector2[4];

    private Transform localPlayer;
    private bool waypointBoundsInitialized;
    private bool viewportOutlineVisible;
    private bool uiReady;
    private float nextActorRefreshAt;
    private float nextUiBindAttemptAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnSceneLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachToGameUiControllers();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachToGameUiControllers();
    }

    private static void AttachToGameUiControllers()
    {
        GameUIController[] controllers = FindObjectsByType<GameUIController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            GameUIController controller = controllers[i];
            if (controller == null)
            {
                continue;
            }

            if (!controller.TryGetComponent(out MinimapHudController _))
            {
                controller.gameObject.AddComponent<MinimapHudController>();
            }
        }
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            Player.LocalPlayerSpawned += OnLocalPlayerSpawned;
            PlayerManager.OnPlayerAdded += OnTrackedPlayersChanged;
            PlayerManager.OnPlayerRemoved += OnTrackedPlayersChanged;

            waypointBoundsInitialized = false;
            nextActorRefreshAt = 0f;
            nextUiBindAttemptAt = 0f;

            TryResolveLocalPlayer();
        }

        TryInitializeUi();
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            PlayerManager.OnPlayerAdded -= OnTrackedPlayersChanged;
            PlayerManager.OnPlayerRemoved -= OnTrackedPlayersChanged;
        }

        UnhookUiCallbacks();
        ClearMarkers();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (uiReady && (minimapRoot == null || minimapRoot.panel == null))
        {
            UnhookUiCallbacks();
            ClearMarkers();
            uiReady = false;
        }

        if (!uiReady)
        {
            if (Time.unscaledTime >= nextUiBindAttemptAt)
            {
                TryInitializeUi();
                nextUiBindAttemptAt = Time.unscaledTime + 0.5f;
            }
            return;
        }

        if (localPlayer == null)
        {
            TryResolveLocalPlayer();
        }

        TryResolveBoundsFromNavMeshSurface();

        if (Time.unscaledTime >= nextActorRefreshAt)
        {
            RefreshTrackedActors();
            nextActorRefreshAt = Time.unscaledTime + Mathf.Max(0.1f, actorRefreshInterval);
        }

        UpdateMarkerPositions();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        UnityEditor.EditorApplication.delayCall -= RefreshEditorPreviewDelayed;
        UnityEditor.EditorApplication.delayCall += RefreshEditorPreviewDelayed;
    }

    private void RefreshEditorPreviewDelayed()
    {
        if (this == null || !isActiveAndEnabled || Application.isPlaying)
        {
            return;
        }

        uiReady = false;
        TryInitializeUi();
    }
#endif

    private void TryInitializeUi()
    {
        if (uiDocument == null)
        {
            uiDocument = FindMainHudDocument();
        }

        if (uiDocument == null)
        {
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        if (root == null)
        {
            return;
        }

        hudRoot = root.Q<VisualElement>("HudRoot");
        minimapRoot = root.Q<VisualElement>("MinimapRoot");
        minimapRender = root.Q<VisualElement>("MinimapRender");
        minimapMarkerLayer = root.Q<VisualElement>("MinimapMarkerLayer");
        minimapCenterReticle = root.Q<VisualElement>("MinimapCenterReticle");
        minimapModeLabel = root.Q<Label>("MinimapModeLabel");
        minimapZoomLabel = root.Q<Label>("MinimapZoomLabel");

        if (hudRoot == null || minimapRoot == null || minimapRender == null)
        {
            return;
        }

        ApplyMapTexture();

        if (!Application.isPlaying)
        {
            // Edit-mode: texture is applied, nothing else to do.
            uiReady = true;
            return;
        }

        if (minimapMarkerLayer == null)
        {
            return;
        }

        EnsureViewportOutlineElement();
        ConfigureCenterReticleRuntimeStyle();
        UIToolkitRaycastChecker.RegisterBlockingElement(minimapRoot);

        UpdateMapLabels();
        uiReady = true;
        nextActorRefreshAt = 0f;
    }

    private void ConfigureCenterReticleRuntimeStyle()
    {
        if (minimapCenterReticle == null)
        {
            return;
        }

        // The USS uses negative margins for static centering (left/top: 50%).
        // Runtime code computes pixel-perfect left/top, so margins must be neutralized.
        minimapCenterReticle.style.marginLeft = 0f;
        minimapCenterReticle.style.marginTop = 0f;
        minimapCenterReticle.style.marginRight = 0f;
        minimapCenterReticle.style.marginBottom = 0f;
    }

    private void ApplyMapTexture()
    {
        if (minimapRender == null)
        {
            return;
        }

        Texture2D texture = mapTextureOverride;

#if UNITY_EDITOR
        if (texture == null)
        {
            texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(MinimapTextureOutputPath);
        }
#endif

        if (texture != null)
        {
            minimapRender.style.backgroundImage = Background.FromTexture2D(texture);
        }
    }

    private void EnsureViewportOutlineElement()
    {
        if (minimapMarkerLayer == null)
        {
            return;
        }

        if (minimapViewportBorder != null)
        {
            return;
        }

        minimapViewportBorder = new VisualElement
        {
            pickingMode = PickingMode.Ignore
        };
        minimapViewportBorder.AddToClassList("minimap-viewport-border");
        minimapViewportBorder.generateVisualContent += OnGenerateViewportOutline;
        minimapMarkerLayer.Insert(0, minimapViewportBorder);
    }

    private void OnGenerateViewportOutline(MeshGenerationContext context)
    {
        if (!viewportOutlineVisible)
        {
            return;
        }

        Painter2D painter = context.painter2D;
        painter.lineWidth = Mathf.Max(0.5f, viewportBorderWidth);
        painter.strokeColor = viewportBorderColor;
        painter.BeginPath();
        painter.MoveTo(viewportOutlinePoints[0]);
        painter.LineTo(viewportOutlinePoints[1]);
        painter.LineTo(viewportOutlinePoints[2]);
        painter.LineTo(viewportOutlinePoints[3]);
        painter.ClosePath();
        painter.Stroke();
    }

    private void UpdateMapLabels()
    {
        if (minimapModeLabel != null)
        {
            minimapModeLabel.text = "A* Grid Map";
        }

        if (minimapZoomLabel != null)
        {
            Vector2 span = GetMapSpan();
            minimapZoomLabel.text = $"{Mathf.RoundToInt(Mathf.Max(span.x, span.y))}m span";
        }
    }

    private void RefreshTrackedActors()
    {
        foreach (KeyValuePair<int, MarkerEntry> pair in markers)
        {
            pair.Value.seenThisRefresh = false;
        }

        if (showPlayers && PlayerManager.Instance != null)
        {
            List<Player> players = PlayerManager.Instance.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
                if (player == null || !player.isActiveAndEnabled)
                {
                    continue;
                }

                if (player.transform == localPlayer)
                {
                    continue;
                }

                UpsertMarker(player, MarkerKind.Player, playerMarkerSize);
            }
        }

        if (showNpcs)
        {
            NPC[] npcs = FindObjectsByType<NPC>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NPC npc = npcs[i];
                if (npc == null || !npc.isActiveAndEnabled)
                {
                    continue;
                }

                UpsertMarker(npc, MarkerKind.Npc, npcMarkerSize);
            }
        }

        staleMarkerIds.Clear();
        foreach (KeyValuePair<int, MarkerEntry> pair in markers)
        {
            MarkerEntry entry = pair.Value;
            if (!entry.seenThisRefresh || entry.source == null || entry.target == null)
            {
                staleMarkerIds.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleMarkerIds.Count; i++)
        {
            RemoveMarker(staleMarkerIds[i]);
        }
    }

    private void UpsertMarker(Component source, MarkerKind kind, float size)
    {
        if (source == null || minimapMarkerLayer == null)
        {
            return;
        }

        int id = source.GetInstanceID();
        if (!markers.TryGetValue(id, out MarkerEntry entry))
        {
            entry = new MarkerEntry
            {
                source = source,
                target = source.transform,
                kind = kind,
                size = size,
                element = CreateMarkerElement(kind, size),
                offscreen = false
            };
            markers.Add(id, entry);
        }

        entry.source = source;
        entry.target = source.transform;
        entry.kind = kind;
        entry.size = size;
        entry.seenThisRefresh = true;
    }

    private VisualElement CreateMarkerElement(MarkerKind kind, float size)
    {
        VisualElement marker = new VisualElement
        {
            pickingMode = PickingMode.Ignore
        };

        marker.AddToClassList("minimap-marker");
        marker.AddToClassList(kind == MarkerKind.Player ? "minimap-marker-player" : "minimap-marker-npc");
        marker.style.width = size;
        marker.style.height = size;

        minimapMarkerLayer.Add(marker);
        return marker;
    }

    private void UpdateMarkerPositions()
    {
        if (minimapMarkerLayer == null)
        {
            return;
        }

        float width = minimapMarkerLayer.resolvedStyle.width;
        float height = minimapMarkerLayer.resolvedStyle.height;
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        Rect markerBounds = GetMarkerBounds(width, height);
        Rect viewportBounds = GetViewportBounds(width, height);
        bool hasLocalPlayer = localPlayer != null;

        if (hasLocalPlayer)
        {
            SetReticlePosition(width, height, markerBounds);
        }
        else if (minimapCenterReticle != null)
        {
            minimapCenterReticle.style.display = DisplayStyle.None;
        }

        UpdateViewportOutline(width, height, viewportBounds);

        staleMarkerIds.Clear();

        foreach (KeyValuePair<int, MarkerEntry> pair in markers)
        {
            int id = pair.Key;
            MarkerEntry entry = pair.Value;
            if (entry == null || entry.source == null || entry.target == null || entry.element == null)
            {
                staleMarkerIds.Add(id);
                continue;
            }

            if (entry.source is NPC npc && npc.CurrentHealth <= 0)
            {
                entry.element.style.display = DisplayStyle.None;
                continue;
            }

            entry.element.style.display = DisplayStyle.Flex;

            Vector3 worldPosition = entry.target.position + Vector3.up * markerWorldYOffset;
            Vector2 normalized = WorldToMapNormalized(worldPosition);
            bool outsideMap = normalized.x < 0f || normalized.x > 1f || normalized.y < 0f || normalized.y > 1f;
            Vector2 markerPosition = NormalizedToMapUiPosition(normalized, width, height);
            bool clampedByRect = ClampToRect(ref markerPosition, markerBounds);
            bool offscreen = outsideMap || clampedByRect;
            float halfSize = entry.size * 0.5f;

            entry.element.style.left = markerPosition.x - halfSize;
            entry.element.style.top = markerPosition.y - halfSize;

            if (offscreen != entry.offscreen)
            {
                entry.offscreen = offscreen;
                if (offscreen)
                {
                    entry.element.AddToClassList(OffscreenMarkerClass);
                }
                else
                {
                    entry.element.RemoveFromClassList(OffscreenMarkerClass);
                }
            }
        }

        for (int i = 0; i < staleMarkerIds.Count; i++)
        {
            RemoveMarker(staleMarkerIds[i]);
        }
    }

    private Vector2 SetReticlePosition(float width, float height, Rect markerBounds)
    {
        if (minimapCenterReticle == null || localPlayer == null)
        {
            return markerBounds.center;
        }

        minimapCenterReticle.style.display = DisplayStyle.Flex;

        Vector2 normalized = WorldToMapNormalized(localPlayer.position);
        Vector2 mapPosition = NormalizedToMapUiPosition(normalized, width, height);
        ClampToRect(ref mapPosition, markerBounds);

        float reticleWidth = minimapCenterReticle.resolvedStyle.width;
        float reticleHeight = minimapCenterReticle.resolvedStyle.height;
        if (reticleWidth <= 0f)
        {
            reticleWidth = 14f;
        }

        if (reticleHeight <= 0f)
        {
            reticleHeight = 14f;
        }

        minimapCenterReticle.style.left = mapPosition.x - reticleWidth * 0.5f;
        minimapCenterReticle.style.top = mapPosition.y - reticleHeight * 0.5f;

        float reticleYaw = localPlayer.eulerAngles.y;
        minimapCenterReticle.style.rotate = new StyleRotate(new Rotate(new Angle(reticleYaw, AngleUnit.Degree)));
        return mapPosition;
    }

    private void UpdateViewportOutline(float width, float height, Rect markerBounds)
    {
        if (minimapViewportBorder == null || !showViewportBorder)
        {
            SetViewportOutlineVisible(false);
            return;
        }

        Camera gameplayCamera = ResolveGameplayCamera();
        if (!TryPopulateViewportOutlinePoints(gameplayCamera, width, height, markerBounds))
        {
            SetViewportOutlineVisible(false);
            return;
        }

        SetViewportOutlineVisible(true);
        minimapViewportBorder.MarkDirtyRepaint();
    }

    private bool TryPopulateViewportOutlinePoints(Camera gameplayCamera, float width, float height, Rect markerBounds)
    {
        if (!IsGameplayCameraUsable(gameplayCamera))
        {
            return false;
        }

        float projectionHeight = projectViewportAtLocalPlayerHeight && localPlayer != null
            ? localPlayer.position.y
            : viewportProjectionHeight;

        Plane projectionPlane = new Plane(Vector3.up, new Vector3(0f, projectionHeight, 0f));

        if (!TryProjectViewportPointToPlane(gameplayCamera, projectionPlane, 0f, 0f, out Vector3 worldBottomLeft) ||
            !TryProjectViewportPointToPlane(gameplayCamera, projectionPlane, 1f, 0f, out Vector3 worldBottomRight) ||
            !TryProjectViewportPointToPlane(gameplayCamera, projectionPlane, 1f, 1f, out Vector3 worldTopRight) ||
            !TryProjectViewportPointToPlane(gameplayCamera, projectionPlane, 0f, 1f, out Vector3 worldTopLeft))
        {
            return false;
        }

        viewportOutlinePoints[0] = NormalizedToMapUiPosition(WorldToMapNormalized(worldBottomLeft), width, height);
        viewportOutlinePoints[1] = NormalizedToMapUiPosition(WorldToMapNormalized(worldBottomRight), width, height);
        viewportOutlinePoints[2] = NormalizedToMapUiPosition(WorldToMapNormalized(worldTopRight), width, height);
        viewportOutlinePoints[3] = NormalizedToMapUiPosition(WorldToMapNormalized(worldTopLeft), width, height);

        ClampToRect(ref viewportOutlinePoints[0], markerBounds);
        ClampToRect(ref viewportOutlinePoints[1], markerBounds);
        ClampToRect(ref viewportOutlinePoints[2], markerBounds);
        ClampToRect(ref viewportOutlinePoints[3], markerBounds);
        return true;
    }

    private static bool TryProjectViewportPointToPlane(Camera cam, Plane plane, float viewportX, float viewportY, out Vector3 worldPoint)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
        if (plane.Raycast(ray, out float distance) && distance >= 0f)
        {
            worldPoint = ray.GetPoint(distance);
            return true;
        }

        worldPoint = default;
        return false;
    }

    private Camera ResolveGameplayCamera()
    {
        if (IsGameplayCameraUsable(gameplayCameraOverride))
        {
            return gameplayCameraOverride;
        }

        Camera main = Camera.main;
        if (IsGameplayCameraUsable(main))
        {
            return main;
        }

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (IsGameplayCameraUsable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsGameplayCameraUsable(Camera camera)
    {
        return camera != null && camera.isActiveAndEnabled && camera.targetTexture == null;
    }

    private void SetViewportOutlineVisible(bool visible)
    {
        viewportOutlineVisible = visible;
        if (minimapViewportBorder == null)
        {
            return;
        }

        minimapViewportBorder.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private Vector2 WorldToMapNormalized(Vector3 worldPosition)
    {
        float minX = Mathf.Min(mapWorldMin.x, mapWorldMax.x);
        float maxX = Mathf.Max(mapWorldMin.x, mapWorldMax.x);
        float minZ = Mathf.Min(mapWorldMin.y, mapWorldMax.y);
        float maxZ = Mathf.Max(mapWorldMin.y, mapWorldMax.y);

        float spanX = Mathf.Max(0.01f, maxX - minX);
        float spanZ = Mathf.Max(0.01f, maxZ - minZ);

        float normalizedX = (worldPosition.x - minX) / spanX;
        float normalizedY = (worldPosition.z - minZ) / spanZ;

        if (invertMapX)
        {
            normalizedX = 1f - normalizedX;
        }

        if (invertMapY)
        {
            normalizedY = 1f - normalizedY;
        }

        return new Vector2(normalizedX, normalizedY);
    }

    private static Vector2 NormalizedToMapUiPosition(Vector2 normalized, float width, float height)
    {
        return new Vector2(normalized.x * width, (1f - normalized.y) * height);
    }

    private Rect GetMarkerBounds(float width, float height)
    {
        float normalizedPadding = Mathf.Clamp01(edgePadding);
        float padX = Mathf.Min(width * normalizedPadding, width * 0.45f);
        float padY = Mathf.Min(height * normalizedPadding, height * 0.45f);

        float boundsWidth = Mathf.Max(1f, width - (padX * 2f));
        float boundsHeight = Mathf.Max(1f, height - (padY * 2f));
        return new Rect(padX, padY, boundsWidth, boundsHeight);
    }

    private Rect GetViewportBounds(float width, float height)
    {
        // Keep the stroke inside the masked minimap frame without inheriting marker edge padding.
        float inset = Mathf.Max(0.5f, viewportBorderWidth) * 0.5f;
        float boundsWidth = Mathf.Max(1f, width - (inset * 2f));
        float boundsHeight = Mathf.Max(1f, height - (inset * 2f));
        return new Rect(inset, inset, boundsWidth, boundsHeight);
    }

    private static bool ClampToRect(ref Vector2 position, Rect bounds)
    {
        float clampedX = Mathf.Clamp(position.x, bounds.xMin, bounds.xMax);
        float clampedY = Mathf.Clamp(position.y, bounds.yMin, bounds.yMax);
        if (Mathf.Approximately(clampedX, position.x) && Mathf.Approximately(clampedY, position.y))
        {
            return false;
        }

        position = new Vector2(clampedX, clampedY);
        return true;
    }

    private Vector2 GetMapSpan()
    {
        float spanX = Mathf.Abs(mapWorldMax.x - mapWorldMin.x);
        float spanZ = Mathf.Abs(mapWorldMax.y - mapWorldMin.y);
        return new Vector2(spanX, spanZ);
    }

    private void TryResolveBoundsFromNavMeshSurface()
    {
        if (!autoComputeWorldBoundsFromNavMeshSurface || waypointBoundsInitialized)
        {
            return;
        }

        if (AstarNavigationUtility.TryGetGridGraphBounds(out Bounds gridBounds))
        {
            ApplyResolvedWorldBounds(gridBounds.min.x, gridBounds.min.z, gridBounds.max.x, gridBounds.max.z);
            return;
        }

        NavMeshSurface[] surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces == null || surfaces.Length == 0)
        {
            return;
        }

        NavMeshSurface selectedSurface = SelectNavMeshSurface(surfaces);
        if (selectedSurface == null || selectedSurface.collectObjects != CollectObjects.Volume)
        {
            return;
        }

        Bounds bounds = GetSurfaceWorldBounds(selectedSurface);
        if (bounds.size.x <= 0.01f || bounds.size.z <= 0.01f)
        {
            return;
        }

        ApplyResolvedWorldBounds(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
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

        // Match NavMeshSurface's local-to-world bounds conversion for volume collection.
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

    private void ApplyResolvedWorldBounds(float minX, float minZ, float maxX, float maxZ)
    {
        if (forceSquareWorldBounds)
        {
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float halfExtent = Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f;
            minX = centerX - halfExtent;
            maxX = centerX + halfExtent;
            minZ = centerZ - halfExtent;
            maxZ = centerZ + halfExtent;
        }

        mapWorldMin = new Vector2(minX, minZ);
        mapWorldMax = new Vector2(maxX, maxZ);
        waypointBoundsInitialized = true;
        UpdateMapLabels();
    }

    private void RemoveMarker(int markerId)
    {
        if (!markers.TryGetValue(markerId, out MarkerEntry entry))
        {
            return;
        }

        if (entry.element != null)
        {
            entry.element.RemoveFromHierarchy();
        }

        markers.Remove(markerId);
    }

    private void ClearMarkers()
    {
        staleMarkerIds.Clear();
        foreach (KeyValuePair<int, MarkerEntry> pair in markers)
        {
            if (pair.Value != null && pair.Value.element != null)
            {
                pair.Value.element.RemoveFromHierarchy();
            }
        }

        markers.Clear();
    }

    private void UnhookUiCallbacks()
    {
        if (minimapRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(minimapRoot);
        }

        if (minimapViewportBorder != null)
        {
            minimapViewportBorder.generateVisualContent -= OnGenerateViewportOutline;
            minimapViewportBorder.RemoveFromHierarchy();
            minimapViewportBorder = null;
        }

        viewportOutlineVisible = false;

        minimapRoot = null;
        minimapRender = null;
        minimapMarkerLayer = null;
        minimapCenterReticle = null;
        minimapModeLabel = null;
        minimapZoomLabel = null;
        hudRoot = null;
    }

    private void TryResolveLocalPlayer()
    {
        if (IsSceneTransformUsable(localPlayerOverride))
        {
            localPlayer = localPlayerOverride;
            return;
        }

        if (Player.LocalPlayer != null && IsSceneTransformUsable(Player.LocalPlayer.transform))
        {
            localPlayer = Player.LocalPlayer.transform;
            return;
        }

        if (PlayerManager.Instance != null &&
            PlayerManager.Instance.LocalPlayer != null &&
            IsSceneTransformUsable(PlayerManager.Instance.LocalPlayer.transform))
        {
            localPlayer = PlayerManager.Instance.LocalPlayer.transform;
            return;
        }

        localPlayer = null;
    }

    private void OnLocalPlayerSpawned(Transform playerTransform)
    {
        localPlayer = IsSceneTransformUsable(playerTransform) ? playerTransform : null;
    }

    private void OnTrackedPlayersChanged(Player _)
    {
        nextActorRefreshAt = 0f;
    }

    private static UIDocument FindMainHudDocument()
    {
        UIDocument[] docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        for (int i = 0; i < docs.Length; i++)
        {
            UIDocument doc = docs[i];
            if (doc == null || doc.rootVisualElement == null)
            {
                continue;
            }

            if (doc.rootVisualElement.Q<VisualElement>("HudRoot") != null)
            {
                return doc;
            }
        }

        return null;
    }

    private static bool IsSceneTransformUsable(Transform candidate)
    {
        return candidate != null &&
               candidate.gameObject.scene.IsValid() &&
               candidate.gameObject.scene.isLoaded;
    }
}
