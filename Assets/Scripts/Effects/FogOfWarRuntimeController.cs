using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Effects
{
    /// <summary>
    /// Builds and updates a world-space fog-of-war mask for the active local player.
    /// A world-space fog plane samples this mask to cover unrevealed space.
    /// </summary>
    public sealed class FogOfWarRuntimeController : MonoBehaviour
    {
        private const string RuntimeObjectName = "[FogOfWarRuntime]";
        private const string FogPlaneMaterialResourcePath = "Materials/FogOfWarWorldPlane";
        private const int TextureResolution = 256;
        private const float DefaultRevealRadius = 38f;
        private const float DefaultRevealSoftness = 12f;
        private const float FallbackWorldSpan = 512f;
        private const string PreferredNavMeshSurfaceName = "NavMesh";
        private const float FogPlaneHeightOffset = 8f;

        private static readonly int MaskTextureId = Shader.PropertyToID("_BoatAttackFogOfWarMask");
        private static readonly int BoundsId = Shader.PropertyToID("_BoatAttackFogOfWarBounds");
        private static readonly int EnabledId = Shader.PropertyToID("_BoatAttackFogOfWarEnabled");
        private static readonly int FogColorId = Shader.PropertyToID("_BoatAttackFogOfWarFogColor");
        private static readonly int WaterLevelId = Shader.PropertyToID("_BoatAttackFogOfWarWaterLevel");
        private static readonly int FogMaskId = Shader.PropertyToID("_FogMask");
        private static readonly int FogTexelSizeId = Shader.PropertyToID("_FogTexelSize");
        private static readonly int FogTintId = Shader.PropertyToID("_FogTint");

        private static FogOfWarRuntimeController s_instance;

        [SerializeField] public float seaLevel = 10f;
        [SerializeField, Min(8f)] private float revealRadius = DefaultRevealRadius;
        [SerializeField, Min(0.5f)] private float revealSoftness = DefaultRevealSoftness;

        private Texture2D maskTexture;
        private Color32[] maskPixels;
        private Transform revealTarget;
        private GameObject fogPlaneObject;
        private Material fogPlaneMaterialInstance;
        private Vector2 worldMin;
        private Vector2 worldSize;
        private bool boundsResolved;
        private bool maskDirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isBatchMode || s_instance != null)
            {
                return;
            }

            var runtimeObject = new GameObject(RuntimeObjectName);
            DontDestroyOnLoad(runtimeObject);
            s_instance = runtimeObject.AddComponent<FogOfWarRuntimeController>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureMaskTexture();
            EnsureFogPlane();
            RefreshBounds(clearMask: true);
            PushStaticGlobals();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }

            if (maskTexture != null)
            {
                Destroy(maskTexture);
                maskTexture = null;
            }

            if (fogPlaneMaterialInstance != null)
            {
                Destroy(fogPlaneMaterialInstance);
                fogPlaneMaterialInstance = null;
            }

            if (fogPlaneObject != null)
            {
                Destroy(fogPlaneObject);
                fogPlaneObject = null;
            }
        }

        private void Update()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            UpdateRevealTarget();
            UpdateDynamicGlobals();
            UpdateFogPlaneTransform();

            if (revealTarget == null)
            {
                Shader.SetGlobalFloat(EnabledId, 0f);
                SetFogPlaneVisible(false);
                return;
            }

            if (!boundsResolved)
            {
                RefreshBounds(clearMask: true);
            }

            if (!boundsResolved)
            {
                Shader.SetGlobalFloat(EnabledId, 0f);
                SetFogPlaneVisible(false);
                return;
            }

            EnsureMaskTexture();
            EnsureFogPlane();
            ResetMask(uploadImmediately: false);
            PaintReveal(revealTarget.position);
            UploadMaskIfDirty();

            Shader.SetGlobalFloat(EnabledId, 1f);
            SetFogPlaneVisible(true);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            revealTarget = null;
            RefreshBounds(clearMask: true);
        }

        private void UpdateRevealTarget()
        {
            if (revealTarget != null)
            {
                return;
            }

            if (Player.LocalPlayer != null)
            {
                revealTarget = Player.LocalPlayer.transform;
            }
        }

        private void EnsureMaskTexture()
        {
            if (maskTexture != null && maskPixels != null && maskPixels.Length == TextureResolution * TextureResolution)
            {
                return;
            }

            maskTexture = new Texture2D(TextureResolution, TextureResolution, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "FogOfWarMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            maskPixels = new Color32[TextureResolution * TextureResolution];
            ResetMask(uploadImmediately: true);
            Shader.SetGlobalTexture(MaskTextureId, maskTexture);
            if (fogPlaneMaterialInstance != null)
            {
                fogPlaneMaterialInstance.SetTexture(FogMaskId, maskTexture);
                fogPlaneMaterialInstance.SetVector(FogTexelSizeId, new Vector4(1f / TextureResolution, 1f / TextureResolution, TextureResolution, TextureResolution));
            }
        }

        private void ResetMask(bool uploadImmediately)
        {
            if (maskPixels == null)
            {
                return;
            }

            for (int i = 0; i < maskPixels.Length; i++)
            {
                maskPixels[i] = new Color32(0, 0, 0, 255);
            }

            maskDirty = true;
            if (uploadImmediately)
            {
                UploadMaskIfDirty();
            }
        }

        private void UploadMaskIfDirty()
        {
            if (!maskDirty || maskTexture == null || maskPixels == null)
            {
                return;
            }

            maskTexture.SetPixels32(maskPixels);
            maskTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            Shader.SetGlobalTexture(MaskTextureId, maskTexture);
            maskDirty = false;
        }

        private void RefreshBounds(bool clearMask)
        {
            boundsResolved = false;

            if (TryResolveNavMeshBounds(out var bounds) || TryResolveTerrainBounds(out bounds) || TryResolveSceneRendererBounds(out bounds))
            {
                ApplyBounds(bounds, clearMask);
                return;
            }

            if (revealTarget != null)
            {
                bounds = new Bounds(
                    new Vector3(revealTarget.position.x, 0f, revealTarget.position.z),
                    new Vector3(FallbackWorldSpan, 64f, FallbackWorldSpan));
                ApplyBounds(bounds, clearMask);
            }
        }

        private void ApplyBounds(Bounds bounds, bool clearMask)
        {
            worldMin = new Vector2(bounds.min.x, bounds.min.z);
            worldSize = new Vector2(Mathf.Max(bounds.size.x, 1f), Mathf.Max(bounds.size.z, 1f));
            boundsResolved = true;

            PushStaticGlobals();

            if (clearMask)
            {
                EnsureMaskTexture();
                ResetMask(uploadImmediately: true);
            }
        }

        private void PushStaticGlobals()
        {
            Shader.SetGlobalVector(BoundsId, new Vector4(worldMin.x, worldMin.y, worldSize.x, worldSize.y));
            Shader.SetGlobalTexture(MaskTextureId, maskTexture);
        }

        private void UpdateDynamicGlobals()
        {
            Color fogColor = RenderSettings.fogColor.linear;
            Shader.SetGlobalColor(FogColorId, fogColor);
            Shader.SetGlobalFloat(WaterLevelId, seaLevel);

            if (fogPlaneMaterialInstance != null)
            {
                fogPlaneMaterialInstance.SetColor(FogTintId, fogColor);
            }
        }

        private void PaintReveal(Vector3 worldPosition)
        {
            if (maskPixels == null || worldSize.x <= 0f || worldSize.y <= 0f)
            {
                return;
            }

            float pixelsPerWorldX = TextureResolution / worldSize.x;
            float pixelsPerWorldY = TextureResolution / worldSize.y;
            float clampedSoftness = Mathf.Clamp(revealSoftness, 0.5f, revealRadius);
            float innerRadius = Mathf.Max(0f, revealRadius - clampedSoftness);

            int centerX = Mathf.RoundToInt((worldPosition.x - worldMin.x) * pixelsPerWorldX);
            int centerY = Mathf.RoundToInt((worldPosition.z - worldMin.y) * pixelsPerWorldY);
            int radiusInPixelsX = Mathf.CeilToInt(revealRadius * pixelsPerWorldX);
            int radiusInPixelsY = Mathf.CeilToInt(revealRadius * pixelsPerWorldY);

            int minX = Mathf.Clamp(centerX - radiusInPixelsX, 0, TextureResolution - 1);
            int maxX = Mathf.Clamp(centerX + radiusInPixelsX, 0, TextureResolution - 1);
            int minY = Mathf.Clamp(centerY - radiusInPixelsY, 0, TextureResolution - 1);
            int maxY = Mathf.Clamp(centerY + radiusInPixelsY, 0, TextureResolution - 1);

            bool anyChanged = false;

            for (int y = minY; y <= maxY; y++)
            {
                float sampleWorldZ = worldMin.y + ((y + 0.5f) / TextureResolution) * worldSize.y;
                float dz = sampleWorldZ - worldPosition.z;

                for (int x = minX; x <= maxX; x++)
                {
                    float sampleWorldX = worldMin.x + ((x + 0.5f) / TextureResolution) * worldSize.x;
                    float dx = sampleWorldX - worldPosition.x;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);

                    if (distance > revealRadius)
                    {
                        continue;
                    }

                    float strength = distance <= innerRadius
                        ? 1f
                        : Mathf.InverseLerp(revealRadius, innerRadius, distance);

                    byte revealValue = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * byte.MaxValue);
                    int pixelIndex = y * TextureResolution + x;
                    Color32 pixel = maskPixels[pixelIndex];

                    if (revealValue > pixel.r)
                    {
                        pixel.r = revealValue;
                    }

                    if (!pixel.Equals(maskPixels[pixelIndex]))
                    {
                        maskPixels[pixelIndex] = pixel;
                        anyChanged = true;
                    }
                }
            }

            maskDirty |= anyChanged;
        }

        private static bool TryResolveTerrainBounds(out Bounds bounds)
        {
            bounds = default;
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

                Vector3 terrainSize = terrain.terrainData.size;
                Bounds terrainBounds = new Bounds(
                    terrain.transform.position + terrainSize * 0.5f,
                    terrainSize);

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

        private void EnsureFogPlane()
        {
            if (fogPlaneObject == null)
            {
                fogPlaneObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                fogPlaneObject.name = "FogOfWarPlane";
                fogPlaneObject.transform.SetParent(transform, false);
                fogPlaneObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                fogPlaneObject.hideFlags = HideFlags.HideAndDontSave;

                Collider collider = fogPlaneObject.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                Renderer renderer = fogPlaneObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                }
            }

            if (fogPlaneMaterialInstance == null)
            {
                Material baseMaterial = Resources.Load<Material>(FogPlaneMaterialResourcePath);
                if (baseMaterial == null)
                {
                    Debug.LogError($"FogOfWarRuntimeController: Missing Resources material at Assets/Resources/{FogPlaneMaterialResourcePath}.mat");
                    return;
                }

                fogPlaneMaterialInstance = new Material(baseMaterial)
                {
                    name = "FogOfWarPlane_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };

                fogPlaneMaterialInstance.SetTexture(FogMaskId, maskTexture);
                fogPlaneMaterialInstance.SetVector(FogTexelSizeId, new Vector4(1f / TextureResolution, 1f / TextureResolution, TextureResolution, TextureResolution));

                Renderer renderer = fogPlaneObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = fogPlaneMaterialInstance;
                }
            }
        }

        private void UpdateFogPlaneTransform()
        {
            if (fogPlaneObject == null || !boundsResolved)
            {
                return;
            }

            Vector3 center = new Vector3(worldMin.x + worldSize.x * 0.5f, seaLevel + FogPlaneHeightOffset, worldMin.y + worldSize.y * 0.5f);
            fogPlaneObject.transform.position = center;
            fogPlaneObject.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
        }

        private void SetFogPlaneVisible(bool visible)
        {
            if (fogPlaneObject != null && fogPlaneObject.activeSelf != visible)
            {
                fogPlaneObject.SetActive(visible);
            }
        }

        private static bool TryResolveNavMeshBounds(out Bounds bounds)
        {
            bounds = default;
            NavMeshSurface[] surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            if (surfaces == null || surfaces.Length == 0)
            {
                return false;
            }

            NavMeshSurface selectedSurface = null;
            for (int i = 0; i < surfaces.Length; i++)
            {
                NavMeshSurface surface = surfaces[i];
                if (surface == null || !surface.isActiveAndEnabled || surface.collectObjects != CollectObjects.Volume)
                {
                    continue;
                }

                if (selectedSurface == null)
                {
                    selectedSurface = surface;
                }

                if (string.Equals(surface.gameObject.name, PreferredNavMeshSurfaceName, System.StringComparison.OrdinalIgnoreCase))
                {
                    selectedSurface = surface;
                    break;
                }
            }

            if (selectedSurface == null)
            {
                return false;
            }

            Bounds localBounds = new Bounds(selectedSurface.center, selectedSurface.size);
            Matrix4x4 localToWorld = Matrix4x4.TRS(selectedSurface.transform.position, selectedSurface.transform.rotation, Vector3.one);
            bounds = GetWorldBounds(localToWorld, localBounds);
            return bounds.size.x > 1f && bounds.size.z > 1f;
        }

        private static bool TryResolveSceneRendererBounds(out Bounds bounds)
        {
            bounds = default;
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            bool foundRenderer = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    !renderer.gameObject.scene.IsValid() ||
                    !renderer.gameObject.scene.isLoaded)
                {
                    continue;
                }

                if (!foundRenderer)
                {
                    bounds = renderer.bounds;
                    foundRenderer = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return foundRenderer;
        }

        private static Bounds GetWorldBounds(Matrix4x4 localToWorld, Bounds bounds)
        {
            Vector3 axisX = Abs(localToWorld.MultiplyVector(Vector3.right));
            Vector3 axisY = Abs(localToWorld.MultiplyVector(Vector3.up));
            Vector3 axisZ = Abs(localToWorld.MultiplyVector(Vector3.forward));
            Vector3 worldCenter = localToWorld.MultiplyPoint(bounds.center);
            Vector3 worldSize = axisX * bounds.size.x + axisY * bounds.size.y + axisZ * bounds.size.z;
            return new Bounds(worldCenter, worldSize);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}
