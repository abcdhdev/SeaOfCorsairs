using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Editor window that generates a 2-color minimap texture from the baked NavMesh.
/// Walkable triangles are rendered in a configurable light color with optional
/// height-based shading and color quantization; non-walkable areas use a dark color.
/// Open via  Tools ▸ Generate Minimap Texture.
/// </summary>
public sealed class MinimapAssets : EditorWindow
{
    // ── Defaults ───────────────────────────────────────────────────────────
    private const string OutputPath = "Assets/Textures/MinimapNavMesh.png";
    private const string WalkableAreaName = "Walkable";
    private const string PreferredNavMeshSurfaceObjectName = "NavMesh";

    private static readonly Color DefaultWalkable = new Color(0.71f, 0.78f, 0.67f, 1f);       // light sage
    private static readonly Color DefaultNonWalkable = new Color(0.16f, 0.18f, 0.20f, 1f);     // dark charcoal
    private static readonly Color DefaultWalkableShadow = new Color(0.45f, 0.52f, 0.40f, 1f);  // darker sage

    // ── Serialized settings ────────────────────────────────────────────────
    [SerializeField] private int resolution = 512;
    [SerializeField] private float boundsPadding = 2f;

    [SerializeField] private Color walkableColor = DefaultWalkable;
    [SerializeField] private Color nonWalkableColor = DefaultNonWalkable;

    [Header("Height Shading")]
    [SerializeField] private bool useHeightShading = true;
    [SerializeField] private Color walkableShadowColor = DefaultWalkableShadow;

    [Header("Quantization")]
    [SerializeField] private bool quantizeColors = true;
    [SerializeField, Range(2, 16)] private int quantizationSteps = 4;

    [Header("RenderTexture Capture")]
    [SerializeField] private Camera snapshotCamera;
    [SerializeField] private bool autoFitSnapshotCamera = true;
    [SerializeField] private float snapshotHeightPadding = 25f;
    [SerializeField] private Color snapshotBackgroundColor = new Color(0.16f, 0.18f, 0.20f, 1f);

    [Header("Scene View Overlay")]
    [SerializeField] private bool showInSceneView = true;
    [SerializeField, Range(100f, 400f)] private float sceneViewOverlaySize = 200f;

    private Vector2 scrollPosition;
    private Texture2D previewTexture;
    private bool previewFoldout = true;

    // ── Menu entry ─────────────────────────────────────────────────────────
    [MenuItem("Tools/Generate Minimap Texture")]
    public static void ShowWindow()
    {
        MinimapAssets window = GetWindow<MinimapAssets>("Minimap Generator");
        window.minSize = new Vector2(340f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        LoadPreviewFromDisk();
        SceneView.duringSceneGui -= OnSceneViewGui;
        SceneView.duringSceneGui += OnSceneViewGui;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
        ClearPreviewTexture();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Minimap Texture Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // ── Output ────────────────────────────────────────────────────
        EditorGUILayout.HelpBox($"Output: {OutputPath}", MessageType.None);
        EditorGUILayout.Space(4);

        // ── Resolution & padding ──────────────────────────────────────
        EditorGUILayout.LabelField("Texture", EditorStyles.boldLabel);
        resolution = EditorGUILayout.IntPopup("Resolution", resolution,
            new[] { "256", "512", "1024", "2048" },
            new[] { 256, 512, 1024, 2048 });
        boundsPadding = EditorGUILayout.FloatField("Bounds Padding", boundsPadding);
        EditorGUILayout.Space(6);

        // ── Colors ────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
        walkableColor = EditorGUILayout.ColorField("Walkable", walkableColor);
        nonWalkableColor = EditorGUILayout.ColorField("Non-Walkable", nonWalkableColor);
        EditorGUILayout.Space(6);

        // ── Height shading ────────────────────────────────────────────
        EditorGUILayout.LabelField("Height Shading", EditorStyles.boldLabel);
        useHeightShading = EditorGUILayout.Toggle("Enable", useHeightShading);
        if (useHeightShading)
        {
            walkableShadowColor = EditorGUILayout.ColorField("Low-Elevation Color", walkableShadowColor);
        }
        EditorGUILayout.Space(6);

        // ── Quantization ──────────────────────────────────────────────
        EditorGUILayout.LabelField("Color Quantization", EditorStyles.boldLabel);
        quantizeColors = EditorGUILayout.Toggle("Enable", quantizeColors);
        if (quantizeColors)
        {
            quantizationSteps = EditorGUILayout.IntSlider("Steps", quantizationSteps, 2, 16);
        }
        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("RenderTexture Capture", EditorStyles.boldLabel);
        snapshotCamera = (Camera)EditorGUILayout.ObjectField("Capture Camera", snapshotCamera, typeof(Camera), true);
        autoFitSnapshotCamera = EditorGUILayout.Toggle("Auto Fit Camera", autoFitSnapshotCamera);
        if (autoFitSnapshotCamera)
        {
            snapshotHeightPadding = EditorGUILayout.FloatField("Height Padding", snapshotHeightPadding);
        }
        snapshotBackgroundColor = EditorGUILayout.ColorField("Background", snapshotBackgroundColor);
        EditorGUILayout.Space(6);

        // ── Scene View Overlay ────────────────────────────────────────
        EditorGUILayout.LabelField("Scene View Overlay", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        showInSceneView = EditorGUILayout.Toggle("Show in Scene View", showInSceneView);
        if (showInSceneView)
        {
            sceneViewOverlaySize = EditorGUILayout.Slider("Overlay Size", sceneViewOverlaySize, 100f, 400f);
        }
        if (EditorGUI.EndChangeCheck())
        {
            SceneView.RepaintAll();
        }
        EditorGUILayout.Space(12);

        // ── Generate button ───────────────────────────────────────────
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f, 1f);
        if (GUILayout.Button("Generate From NavMesh", GUILayout.Height(32f)))
        {
            Generate();
        }
        if (GUILayout.Button("Generate From Camera Snapshot", GUILayout.Height(32f)))
        {
            GenerateFromCameraSnapshot();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        // ── Reset ─────────────────────────────────────────────────────
        if (GUILayout.Button("Reset to Defaults"))
        {
            resolution = 512;
            boundsPadding = 2f;
            walkableColor = DefaultWalkable;
            nonWalkableColor = DefaultNonWalkable;
            walkableShadowColor = DefaultWalkableShadow;
            useHeightShading = true;
            quantizeColors = true;
            quantizationSteps = 4;
            snapshotCamera = null;
            autoFitSnapshotCamera = true;
            snapshotHeightPadding = 25f;
            snapshotBackgroundColor = DefaultNonWalkable;
        }

        EditorGUILayout.Space(12);

        // ── Preview ───────────────────────────────────────────────────
        DrawPreview();

        EditorGUILayout.EndScrollView();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Generation
    // ═══════════════════════════════════════════════════════════════════════

    private void Generate()
    {
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();

        if (tri.vertices == null || tri.vertices.Length == 0)
        {
            EditorUtility.DisplayDialog("Minimap Generator", "No NavMesh data found.\nBake a NavMesh first.", "OK");
            return;
        }

        Vector3[] verts = tri.vertices;
        int[] indices = tri.indices;
        int triCount = indices.Length / 3;

        int[] areas = tri.areas;
        int walkableArea = NavMesh.GetAreaFromName(WalkableAreaName);
        bool canFilterByArea = walkableArea >= 0 && areas != null && areas.Length == triCount;
        if (!canFilterByArea)
        {
            Debug.LogWarning($"[MinimapAssets] Could not filter by area '{WalkableAreaName}'. Generating from all triangulated areas.");
        }

        // 1) Compute world bounds from included triangles only.
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        int includedTriCount = 0;

        for (int t = 0; t < triCount; t++)
        {
            if (canFilterByArea && areas[t] != walkableArea)
                continue;

            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];

            ExpandBounds(verts[i0], ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            ExpandBounds(verts[i1], ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            ExpandBounds(verts[i2], ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            includedTriCount++;
        }

        if (includedTriCount == 0)
        {
            EditorUtility.DisplayDialog(
                "Minimap Generator",
                $"No triangles found for NavMesh area '{WalkableAreaName}'.",
                "OK");
            return;
        }

        minX -= boundsPadding;
        minZ -= boundsPadding;
        maxX += boundsPadding;
        maxZ += boundsPadding;

        // Force square.
        float spanX = maxX - minX;
        float spanZ = maxZ - minZ;
        if (spanX > spanZ)
        {
            float d = (spanX - spanZ) * 0.5f;
            minZ -= d;
            maxZ += d;
        }
        else if (spanZ > spanX)
        {
            float d = (spanZ - spanX) * 0.5f;
            minX -= d;
            maxX += d;
        }

        spanX = maxX - minX;
        spanZ = maxZ - minZ;
        float heightRange = Mathf.Max(0.01f, maxY - minY);

        Debug.Log($"[MinimapAssets] Bounds X:[{minX:F1},{maxX:F1}] Z:[{minZ:F1},{maxZ:F1}] Y:[{minY:F1},{maxY:F1}] span:{spanX:F1}x{spanZ:F1}");

        // 2) Create pixel buffer.
        int res = Mathf.Clamp(resolution, 64, 4096);
        int totalPixels = res * res;

        Color32[] pixels = new Color32[totalPixels];
        float[] heightMap = new float[totalPixels];

        Color32 nonWalk32 = nonWalkableColor;
        for (int i = 0; i < totalPixels; i++)
        {
            pixels[i] = nonWalk32;
            heightMap[i] = -1f; // sentinel: not walkable
        }

        // 3) Rasterize included triangles.
        int rasterizedTriCount = 0;
        for (int t = 0; t < triCount; t++)
        {
            if (canFilterByArea && areas[t] != walkableArea)
                continue;

            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];
            Vector3 wA = verts[i0];
            Vector3 wB = verts[i1];
            Vector3 wC = verts[i2];

            Vector2Int pA = WorldToPixel(wA, minX, minZ, spanX, spanZ, res);
            Vector2Int pB = WorldToPixel(wB, minX, minZ, spanX, spanZ, res);
            Vector2Int pC = WorldToPixel(wC, minX, minZ, spanX, spanZ, res);

            float hA = (wA.y - minY) / heightRange;
            float hB = (wB.y - minY) / heightRange;
            float hC = (wC.y - minY) / heightRange;

            RasterizeTriangleWithHeight(pixels, heightMap, res, pA, pB, pC, hA, hB, hC);
            rasterizedTriCount++;
        }

        // 4) Colorize walkable pixels.
        Color walkHi = walkableColor;
        Color walkLo = useHeightShading ? walkableShadowColor : walkableColor;

        for (int i = 0; i < totalPixels; i++)
        {
            float h = heightMap[i];
            if (h < 0f)
                continue; // non-walkable, already filled

            float t2 = Mathf.Clamp01(h);

            if (quantizeColors && quantizationSteps >= 2)
            {
                t2 = Mathf.Floor(t2 * quantizationSteps) / (quantizationSteps - 1f);
                t2 = Mathf.Clamp01(t2);
            }

            pixels[i] = Color.Lerp(walkLo, walkHi, t2);
        }

        // 5) Save.
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        SaveTextureToOutput(tex);
        SetPreviewTexture(tex);

        Debug.Log($"[MinimapAssets] Saved {OutputPath} ({res}x{res}, {rasterizedTriCount} tris, quantize:{quantizeColors} steps:{quantizationSteps})");
        EditorUtility.DisplayDialog("Minimap Generator", $"Texture saved to:\n{OutputPath}\n\n{res}x{res}  -  {rasterizedTriCount} triangles", "OK");
    }

    private void GenerateFromCameraSnapshot()
    {
        int res = Mathf.Clamp(resolution, 64, 4096);

        Camera cameraToUse = snapshotCamera;
        GameObject tempCameraObject = null;
        RenderTexture renderTexture = null;
        Texture2D capturedTexture = null;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = null;

        try
        {
            if (!TryGetSnapshotBounds(out Bounds sceneBounds, out string boundsSource))
            {
                EditorUtility.DisplayDialog("Minimap Generator", "No scene renderers found to capture.", "OK");
                return;
            }

            Debug.Log($"[MinimapAssets] Using {boundsSource} bounds for snapshot capture: center {sceneBounds.center.ToString("F1")} size {sceneBounds.size.ToString("F1")}");

            if (cameraToUse == null)
            {
                tempCameraObject = new GameObject("MinimapSnapshotCamera");
                tempCameraObject.hideFlags = HideFlags.HideAndDontSave;
                cameraToUse = tempCameraObject.AddComponent<Camera>();
            }

            if (autoFitSnapshotCamera || snapshotCamera == null)
            {
                ConfigureSnapshotCamera(cameraToUse, sceneBounds);
            }

            previousTarget = cameraToUse.targetTexture;
            renderTexture = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 1;

            cameraToUse.targetTexture = renderTexture;
            cameraToUse.Render();

            RenderTexture.active = renderTexture;
            capturedTexture = new Texture2D(res, res, TextureFormat.RGBA32, false);
            capturedTexture.ReadPixels(new Rect(0f, 0f, res, res), 0, 0, false);
            capturedTexture.Apply();

            SaveTextureToOutput(capturedTexture);

            // Keep a copy for preview (capturedTexture is destroyed in finally).
            Texture2D previewCopy = new Texture2D(capturedTexture.width, capturedTexture.height, capturedTexture.format, false);
            Graphics.CopyTexture(capturedTexture, previewCopy);
            SetPreviewTexture(previewCopy);

            Debug.Log($"[MinimapAssets] Saved snapshot {OutputPath} ({res}x{res})");
            EditorUtility.DisplayDialog("Minimap Generator", $"Snapshot saved to:\n{OutputPath}\n\n{res}x{res}", "OK");
        }
        finally
        {
            if (cameraToUse != null)
            {
                cameraToUse.targetTexture = previousTarget;
            }

            RenderTexture.active = previousActive;

            if (capturedTexture != null)
            {
                DestroyImmediate(capturedTexture);
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }

            if (tempCameraObject != null)
            {
                DestroyImmediate(tempCameraObject);
            }
        }
    }

    private void ConfigureSnapshotCamera(Camera cam, Bounds sceneBounds)
    {
        float maxHorizontalExtent = Mathf.Max(sceneBounds.extents.x, sceneBounds.extents.z);
        float orthoSize = Mathf.Max(0.1f, maxHorizontalExtent + boundsPadding);
        float y = sceneBounds.max.y + Mathf.Max(1f, snapshotHeightPadding);

        cam.transform.position = new Vector3(sceneBounds.center.x, y, sceneBounds.center.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
        cam.aspect = 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = snapshotBackgroundColor;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = Mathf.Max(100f, y - sceneBounds.min.y + Mathf.Max(1f, snapshotHeightPadding));
    }

    private static bool TryGetSnapshotBounds(out Bounds bounds, out string boundsSource)
    {
        if (TryGetBoundsFromNavMeshSurface(out Bounds navMeshBounds))
        {
            if (TryGetBoundsFromTerrain(out Bounds terrainBounds))
            {
                bounds = ConstrainBoundsToTerrain(navMeshBounds, terrainBounds);
                boundsSource = "NavMeshSurface constrained to terrain";
                return true;
            }

            bounds = navMeshBounds;
            boundsSource = "NavMeshSurface";
            return true;
        }

        if (TryGetBoundsFromTerrain(out Bounds terrainOnlyBounds))
        {
            bounds = terrainOnlyBounds;
            boundsSource = "terrain";
            return true;
        }

        if (TryGetFilteredSceneBounds(out Bounds rendererBounds))
        {
            bounds = rendererBounds;
            boundsSource = "filtered scene renderers";
            return true;
        }

        bounds = default;
        boundsSource = null;
        return false;
    }

    private static bool TryGetBoundsFromNavMeshSurface(out Bounds bounds)
    {
        NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces == null || surfaces.Length == 0)
        {
            bounds = default;
            return false;
        }

        NavMeshSurface selectedSurface = SelectNavMeshSurface(surfaces);
        if (selectedSurface == null || !selectedSurface.isActiveAndEnabled || selectedSurface.collectObjects != CollectObjects.Volume)
        {
            bounds = default;
            return false;
        }

        bounds = GetSurfaceWorldBounds(selectedSurface);
        return bounds.size.x > 0.01f && bounds.size.z > 0.01f;
    }

    private static NavMeshSurface SelectNavMeshSurface(NavMeshSurface[] surfaces)
    {
        NavMeshSurface fallback = null;
        for (int i = 0; i < surfaces.Length; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null || !surface.isActiveAndEnabled || surface.collectObjects != CollectObjects.Volume)
            {
                continue;
            }

            fallback ??= surface;

            if (string.Equals(surface.gameObject.name, PreferredNavMeshSurfaceObjectName, System.StringComparison.OrdinalIgnoreCase))
            {
                return surface;
            }
        }

        return fallback;
    }

    private static bool TryGetBoundsFromTerrain(out Bounds bounds)
    {
        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            bounds = default;
            return false;
        }

        bool foundTerrain = false;
        bounds = default;

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || !terrain.isActiveAndEnabled || terrain.terrainData == null)
            {
                continue;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            Bounds terrainBounds = new Bounds(terrainPosition + terrainSize * 0.5f, terrainSize);

            if (!foundTerrain)
            {
                bounds = terrainBounds;
                foundTerrain = true;
                continue;
            }

            bounds.Encapsulate(terrainBounds);
        }

        return foundTerrain;
    }

    private static Bounds ConstrainBoundsToTerrain(Bounds navMeshBounds, Bounds terrainBounds)
    {
        Vector3 min = navMeshBounds.min;
        Vector3 max = navMeshBounds.max;

        min.x = Mathf.Max(min.x, terrainBounds.min.x);
        max.x = Mathf.Min(max.x, terrainBounds.max.x);
        min.z = Mathf.Max(min.z, terrainBounds.min.z);
        max.z = Mathf.Min(max.z, terrainBounds.max.z);

        if (min.x > max.x || min.z > max.z)
        {
            min.x = terrainBounds.min.x;
            max.x = terrainBounds.max.x;
            min.z = terrainBounds.min.z;
            max.z = terrainBounds.max.z;
        }

        min.y = terrainBounds.min.y;
        max.y = terrainBounds.max.y;

        Bounds constrainedBounds = default;
        constrainedBounds.SetMinMax(min, max);
        return constrainedBounds;
    }

    private static bool TryGetFilteredSceneBounds(out Bounds bounds)
    {
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

        bool found = false;
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || ShouldIgnoreRendererForSnapshotBounds(r))
                continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
    }

    private static bool ShouldIgnoreRendererForSnapshotBounds(Renderer renderer)
    {
        if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
        {
            return true;
        }

        int waterLayer = LayerMask.NameToLayer("Water");
        if (waterLayer >= 0 && renderer.gameObject.layer == waterLayer)
        {
            return true;
        }

        if (ContainsInsensitive(renderer.gameObject.name, "water") ||
            ContainsInsensitive(renderer.gameObject.name, "ocean") ||
            ContainsInsensitive(renderer.gameObject.name, "sea"))
        {
            return true;
        }

        Material sharedMaterial = renderer.sharedMaterial;
        if (sharedMaterial != null)
        {
            if (ContainsInsensitive(sharedMaterial.name, "water") ||
                (sharedMaterial.shader != null && ContainsInsensitive(sharedMaterial.shader.name, "water")))
            {
                return true;
            }
        }

        Bounds rendererBounds = renderer.bounds;
        float horizontalSpan = Mathf.Max(rendererBounds.size.x, rendererBounds.size.z);
        if (horizontalSpan > 4096f && rendererBounds.size.y <= 10f)
        {
            return true;
        }

        return false;
    }

    private static bool ContainsInsensitive(string value, string token)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
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

    private static void SaveTextureToOutput(Texture2D tex)
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(OutputPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);

        TextureImporter imp = AssetImporter.GetAtPath(OutputPath) as TextureImporter;
        if (imp != null)
        {
            imp.textureType = TextureImporterType.Default;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
    }

    private static void ExpandBounds(
        Vector3 v,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY,
        ref float minZ,
        ref float maxZ)
    {
        if (v.x < minX) minX = v.x;
        if (v.x > maxX) maxX = v.x;

        if (v.y < minY) minY = v.y;
        if (v.y > maxY) maxY = v.y;

        if (v.z < minZ) minZ = v.z;
        if (v.z > maxZ) maxZ = v.z;
    }
    private static Vector2Int WorldToPixel(Vector3 w, float minX, float minZ, float spanX, float spanZ, int res)
    {
        float u = (w.x - minX) / spanX;
        float v = (w.z - minZ) / spanZ;
        return new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(u * res), 0, res - 1),
            Mathf.Clamp(Mathf.FloorToInt(v * res), 0, res - 1));
    }

    /// <summary>
    /// Barycentric triangle rasterizer with a pixel-center test.
    /// This avoids scanline rounding seams that show up as contour artifacts.
    /// </summary>
    private static void RasterizeTriangleWithHeight(
        Color32[] pixels, float[] heightMap, int res,
        Vector2Int a, Vector2Int b, Vector2Int c,
        float hA, float hB, float hC)
    {
        float ax = a.x;
        float ay = a.y;
        float bx = b.x;
        float by = b.y;
        float cx = c.x;
        float cy = c.y;

        float area2 = EdgeFunction(ax, ay, bx, by, cx, cy);
        if (Mathf.Abs(area2) < 1e-5f)
        {
            return;
        }

        int minX = Mathf.Clamp(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), 0, res - 1);
        int maxX = Mathf.Clamp(Mathf.Max(a.x, Mathf.Max(b.x, c.x)), 0, res - 1);
        int minY = Mathf.Clamp(Mathf.Min(a.y, Mathf.Min(b.y, c.y)), 0, res - 1);
        int maxY = Mathf.Clamp(Mathf.Max(a.y, Mathf.Max(b.y, c.y)), 0, res - 1);

        float invArea2 = 1f / area2;
        const float edgeEpsilon = -1e-4f;

        for (int y = minY; y <= maxY; y++)
        {
            int rowOff = y * res;
            float py = y + 0.5f;

            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;

                float w0 = EdgeFunction(bx, by, cx, cy, px, py) * invArea2;
                float w1 = EdgeFunction(cx, cy, ax, ay, px, py) * invArea2;
                float w2 = 1f - w0 - w1;

                if (w0 < edgeEpsilon || w1 < edgeEpsilon || w2 < edgeEpsilon)
                    continue;

                float h = w0 * hA + w1 * hB + w2 * hC;
                int idx = rowOff + x;

                // Keep the top-most height when two triangles overlap a pixel.
                if (h <= heightMap[idx])
                    continue;

                heightMap[idx] = h;
                pixels[idx] = Color.white; // placeholder
            }
        }
    }

    private static float EdgeFunction(float ax, float ay, float bx, float by, float px, float py)
    {
        return (px - ax) * (by - ay) - (py - ay) * (bx - ax);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Preview
    // ═══════════════════════════════════════════════════════════════════════

    private void DrawPreview()
    {
        previewFoldout = EditorGUILayout.Foldout(previewFoldout, "Output Preview", true, EditorStyles.foldoutHeader);
        if (!previewFoldout)
        {
            return;
        }

        if (previewTexture == null)
        {
            EditorGUILayout.HelpBox("No preview available. Generate a texture to see the result here.", MessageType.Info);
            if (GUILayout.Button("Load From Disk"))
            {
                LoadPreviewFromDisk();
            }
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"{previewTexture.width} × {previewTexture.height}", EditorStyles.miniLabel);

        // Compute a size that fits the window width while keeping the aspect ratio.
        float availableWidth = EditorGUIUtility.currentViewWidth - 32f;
        float previewSize = Mathf.Clamp(availableWidth, 128f, 512f);
        float aspectRatio = (float)previewTexture.height / Mathf.Max(1f, previewTexture.width);
        float previewHeight = previewSize * aspectRatio;

        Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewHeight, GUILayout.ExpandWidth(false));
        EditorGUI.DrawPreviewTexture(previewRect, previewTexture, null, ScaleMode.ScaleToFit);

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reload From Disk"))
        {
            LoadPreviewFromDisk();
        }
        if (GUILayout.Button("Ping Asset"))
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Texture2D>(OutputPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void SetPreviewTexture(Texture2D texture)
    {
        ClearPreviewTexture();
        previewTexture = texture;
        Repaint();
        SceneView.RepaintAll();
    }

    private void ClearPreviewTexture()
    {
        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }
    }

    private void LoadPreviewFromDisk()
    {
        ClearPreviewTexture();

        Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(OutputPath);
        if (asset == null)
        {
            return;
        }

        // Create an independent copy so the preview is not tied to the imported asset.
        previewTexture = new Texture2D(asset.width, asset.height, asset.format, false);
        Graphics.CopyTexture(asset, previewTexture);
        Repaint();
        SceneView.RepaintAll();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Scene View Overlay
    // ═══════════════════════════════════════════════════════════════════════

    private void OnSceneViewGui(SceneView sceneView)
    {
        if (!showInSceneView || previewTexture == null)
        {
            return;
        }

        Handles.BeginGUI();

        float size = Mathf.Clamp(sceneViewOverlaySize, 100f, 400f);
        float padding = 12f;
        float labelHeight = 18f;
        float totalHeight = size + labelHeight + 6f;

        // Position at bottom-right of the Scene view.
        float viewWidth = sceneView.position.width;
        float viewHeight = sceneView.position.height;
        float x = viewWidth - size - padding;
        float y = viewHeight - totalHeight - padding - 22f; // 22 accounts for the Scene view toolbar

        // Semi-transparent background panel.
        Rect bgRect = new Rect(x - 6f, y - 6f, size + 12f, totalHeight + 12f);
        EditorGUI.DrawRect(bgRect, new Color(0f, 0f, 0f, 0.55f));

        // Subtle border.
        DrawRectOutline(bgRect, new Color(1f, 1f, 1f, 0.12f));

        // Texture.
        Rect texRect = new Rect(x, y, size, size);
        GUI.DrawTexture(texRect, previewTexture, ScaleMode.ScaleToFit);

        // Label.
        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 1f, 1f, 0.7f) }
        };
        Rect labelRect = new Rect(x, y + size + 4f, size, labelHeight);
        GUI.Label(labelRect, $"Minimap  {previewTexture.width}\u00d7{previewTexture.height}", labelStyle);

        Handles.EndGUI();
    }

    private static void DrawRectOutline(Rect rect, Color color)
    {
        // Top
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        // Bottom
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        // Left
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        // Right
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }
}
