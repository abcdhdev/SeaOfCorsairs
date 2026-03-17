using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaOfCorsair
{
    /// <summary>
    /// Generates a speed-driven ribbon wake that sits on top of the water surface.
    /// </summary>
    public class WakeGenerator : MonoBehaviour
    {
        private static readonly int WakeOpacityId = Shader.PropertyToID("_WakeOpacity");
        private static readonly int DistortionStrengthId = Shader.PropertyToID("_DistortionStrength");

        [Header("Wake Layout")]
        public List<Wake> wakes = new List<Wake>();
        [SerializeField] private bool autoPopulateWake = true;
        [SerializeField] private Vector3 defaultWakeOrigin = new Vector3(0.9f, 0f, -3.05f);
        [SerializeField] private float waterLevel = 0f;
        [SerializeField] private float waterSurfaceOffset = 0.05f;

        [Header("Wake Motion")]
        [SerializeField] private float genDistance = 0.65f;
        [SerializeField] private float maxAge = 6f;
        [SerializeField] private float speedThreshold = 0.5f;
        [SerializeField] private float maxVisualSpeed = 14f;
        [SerializeField] private float wakeDriftSpeed = 1.8f;
        [SerializeField] private float kelvinHalfAngle = 19.47f;
        [SerializeField] private float directionSmoothing = 7f;
        [SerializeField] private float lateralNoise = 0.18f;

        [Header("Wake Look")]
        [SerializeField] private string wakeShaderName = "SeaOfCorsair/ShipWakeRibbon";
        [SerializeField] private Color foamColor = new Color(0.92f, 0.97f, 1f, 1f);
        [SerializeField] private Color edgeColor = new Color(0.62f, 0.85f, 0.95f, 1f);
        [SerializeField] private float minWakeWidth = 0.22f;
        [SerializeField] private float maxWakeWidth = 1.35f;
        [SerializeField] private float maxWakeOpacity = 0.9f;
        [SerializeField] private float maxDistortionStrength = 0.03f;
        [SerializeField] private AnimationCurve widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.25f),
            new Keyframe(0.25f, 0.55f),
            new Keyframe(1f, 1.15f));
        [SerializeField] private Gradient colorGradient = CreateDefaultGradient();

        [Header("Legacy Replacement")]
        [SerializeField] private bool disableLegacySplashParticles = true;
        [SerializeField] private bool useLegacyParticlesOnly = false;
        [SerializeField] private string legacySplashName = "Water_splash";

        private readonly List<GameObject> _lineObjects = new List<GameObject>();
        private readonly List<LegacyParticleState> _legacyParticles = new List<LegacyParticleState>();
        private Material _wakeMaterial;
        private Vector3 _lastPosition;
        private Vector3 _smoothedPlanarForward = Vector3.back;
        private bool _hasLastPosition;

        private void Awake()
        {
            if (widthCurve == null || widthCurve.length == 0)
            {
                widthCurve = new AnimationCurve(
                    new Keyframe(0f, 0.25f),
                    new Keyframe(0.25f, 0.55f),
                    new Keyframe(1f, 1.15f));
            }

            if (colorGradient == null || colorGradient.colorKeys == null || colorGradient.colorKeys.Length == 0)
            {
                colorGradient = CreateDefaultGradient();
            }
        }

        private void OnEnable()
        {
            if (useLegacyParticlesOnly)
            {
                ToggleLegacyParticles(true);
                CleanupWakeLines();
                DestroyWakeMaterial();
                return;
            }

            EnsureDefaultWake();
            EnsureWakeMaterial();
            ToggleLegacyParticles(false);
            CreateWakeLines();
        }

        private void OnDisable()
        {
            ToggleLegacyParticles(true);
            CleanupWakeLines();
            DestroyWakeMaterial();
        }

        private void Update()
        {
            if (useLegacyParticlesOnly)
            {
                return;
            }

            float speed01 = UpdatePlanarForward();
            foreach (var wake in wakes)
            {
                if (wake == null || wake.lines.Count != 2)
                {
                    continue;
                }

                DoWake(-1, wake, wake.lines[0], speed01);
                DoWake(1, wake, wake.lines[1], speed01);
            }
        }

        private void EnsureDefaultWake()
        {
            if (!autoPopulateWake || wakes.Count > 0)
            {
                return;
            }

            wakes.Add(new Wake { origin = defaultWakeOrigin });
        }

        private void EnsureWakeMaterial()
        {
            if (_wakeMaterial != null)
            {
                ApplyMaterialProperties(_wakeMaterial);
                return;
            }

            Shader shader = Shader.Find(wakeShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            }

            if (shader == null)
            {
                Debug.LogWarning($"WakeGenerator on {name} could not find shader '{wakeShaderName}'.");
                return;
            }

            _wakeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = $"{name}_WakeMaterial"
            };
            ApplyMaterialProperties(_wakeMaterial);
        }

        private void ApplyMaterialProperties(Material material)
        {
            material.SetColor("_FoamColor", foamColor);
            material.SetColor("_EdgeColor", edgeColor);
            material.SetFloat(WakeOpacityId, maxWakeOpacity);
            material.SetFloat(DistortionStrengthId, maxDistortionStrength);
        }

        private void DestroyWakeMaterial()
        {
            if (_wakeMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_wakeMaterial);
            }
            else
            {
                DestroyImmediate(_wakeMaterial);
            }

            _wakeMaterial = null;
        }

        private void CreateWakeLines()
        {
            CleanupWakeLines();
            foreach (var wake in wakes)
            {
                if (wake == null)
                {
                    continue;
                }

                wake.lines.Clear();
                for (int side = 0; side < 2; side++)
                {
                    GameObject lineObject = new GameObject(side == 0 ? "Wake_Left" : "Wake_Right");
                    lineObject.transform.SetParent(transform, false);
                    lineObject.hideFlags = HideFlags.HideAndDontSave;

                    LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
                    ConfigureLineRenderer(lineRenderer);

                    _lineObjects.Add(lineObject);
                    wake.lines.Add(new WakeLine
                    {
                        lineRenderer = lineRenderer,
                        propertyBlock = new MaterialPropertyBlock(),
                        points = new List<WakePoint>()
                    });
                }
            }
        }

        private void ConfigureLineRenderer(LineRenderer lineRenderer)
        {
            lineRenderer.sharedMaterial = _wakeMaterial;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            lineRenderer.lightProbeUsage = LightProbeUsage.Off;
            lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            lineRenderer.allowOcclusionWhenDynamic = false;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.numCapVertices = 6;
            lineRenderer.numCornerVertices = 4;
            lineRenderer.widthCurve = widthCurve;
            lineRenderer.widthMultiplier = minWakeWidth;
            lineRenderer.colorGradient = colorGradient;
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 0;
        }

        private void CleanupWakeLines()
        {
            foreach (GameObject lineObject in _lineObjects)
            {
                if (lineObject == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(lineObject);
                }
                else
                {
                    DestroyImmediate(lineObject);
                }
            }

            _lineObjects.Clear();

            foreach (var wake in wakes)
            {
                if (wake == null)
                {
                    continue;
                }

                foreach (var wakeLine in wake.lines)
                {
                    wakeLine?.points.Clear();
                }

                wake.lines.Clear();
            }
        }

        private float UpdatePlanarForward()
        {
            Vector3 currentPosition = transform.position;
            if (!_hasLastPosition)
            {
                _lastPosition = currentPosition;
                Vector3 forward = transform.forward;
                forward.y = 0f;
                _smoothedPlanarForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.back;
                _hasLastPosition = true;
                return 0f;
            }

            Vector3 planarVelocity = currentPosition - _lastPosition;
            planarVelocity.y = 0f;
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            planarVelocity /= deltaTime;
            _lastPosition = currentPosition;

            Vector3 targetForward = planarVelocity.sqrMagnitude > 0.0001f ? planarVelocity.normalized : _smoothedPlanarForward;
            float smoothing = 1f - Mathf.Exp(-directionSmoothing * Time.deltaTime);
            _smoothedPlanarForward = Vector3.Slerp(_smoothedPlanarForward, targetForward, smoothing).normalized;

            float speed = planarVelocity.magnitude;
            if (speed <= speedThreshold)
            {
                return 0f;
            }

            return Mathf.InverseLerp(speedThreshold, Mathf.Max(speedThreshold + 0.01f, maxVisualSpeed), speed);
        }

        private void DoWake(int side, Wake wake, WakeLine wakeLine, float speed01)
        {
            if (wakeLine?.lineRenderer == null)
            {
                return;
            }

            Vector3 localOrigin = wake.origin;
            localOrigin.x *= side;

            Vector3 origin = transform.TransformPoint(localOrigin);
            origin.y = waterLevel + waterSurfaceOffset;

            bool shouldEmit = speed01 > 0f;
            if (shouldEmit && (wakeLine.points.Count == 0 || Vector3.Distance(wakeLine.points[0].pos, origin) > genDistance))
            {
                wakeLine.points.Insert(0, CreateWakePoint(origin, side, speed01));
            }

            for (int i = wakeLine.points.Count - 1; i >= 0; i--)
            {
                WakePoint point = wakeLine.points[i];
                point.age += Time.deltaTime;
                point.pos += point.dir * Time.deltaTime;
                point.pos.y = waterLevel + waterSurfaceOffset;

                if (point.age > maxAge)
                {
                    wakeLine.points.RemoveAt(i);
                }
            }

            if (!shouldEmit && wakeLine.points.Count == 0)
            {
                wakeLine.lineRenderer.positionCount = 0;
                return;
            }

            wakeLine.lineRenderer.widthMultiplier = Mathf.Lerp(minWakeWidth, maxWakeWidth, speed01);
            UpdatePropertyBlock(wakeLine, speed01);

            wakeLine.lineRenderer.positionCount = wakeLine.points.Count + 1;
            wakeLine.lineRenderer.SetPosition(0, origin);
            for (int i = 0; i < wakeLine.points.Count; i++)
            {
                wakeLine.lineRenderer.SetPosition(i + 1, wakeLine.points[i].pos);
            }
        }

        private void UpdatePropertyBlock(WakeLine wakeLine, float speed01)
        {
            MaterialPropertyBlock propertyBlock = wakeLine.propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.SetFloat(WakeOpacityId, Mathf.Lerp(0f, maxWakeOpacity, speed01));
            propertyBlock.SetFloat(DistortionStrengthId, Mathf.Lerp(0f, maxDistortionStrength, speed01));
            wakeLine.lineRenderer.SetPropertyBlock(propertyBlock);
        }

        private WakePoint CreateWakePoint(Vector3 pos, int side, float speed01)
        {
            float kelvinRadians = kelvinHalfAngle * Mathf.Deg2Rad;
            Vector3 sideVector = Vector3.Cross(Vector3.up, _smoothedPlanarForward).normalized * side;
            Vector3 wakeDirection = (-_smoothedPlanarForward * Mathf.Cos(kelvinRadians)) + (sideVector * Mathf.Sin(kelvinRadians));
            Vector3 jitter = Vector3.Cross(Vector3.up, wakeDirection).normalized * (Mathf.PerlinNoise(Time.time * 0.6f, side * 17.13f) - 0.5f) * lateralNoise;
            wakeDirection = (wakeDirection + jitter).normalized;
            float driftSpeed = Mathf.Lerp(0.7f, wakeDriftSpeed, speed01);
            return new WakePoint(pos, wakeDirection * driftSpeed);
        }

        private void ToggleLegacyParticles(bool enabled)
        {
            ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>(true);
            if (!enabled)
            {
                if (!disableLegacySplashParticles)
                {
                    return;
                }

                _legacyParticles.Clear();
                foreach (ParticleSystem particle in particles)
                {
                    if (!particle.name.StartsWith(legacySplashName))
                    {
                        continue;
                    }

                    _legacyParticles.Add(new LegacyParticleState
                    {
                        particleSystem = particle,
                        wasActive = particle.gameObject.activeSelf
                    });

                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particle.gameObject.SetActive(false);
                }

                return;
            }

            foreach (ParticleSystem particle in particles)
            {
                if (!particle.name.StartsWith(legacySplashName))
                {
                    continue;
                }

                particle.gameObject.SetActive(true);
                if (Application.isPlaying && !particle.isPlaying)
                {
                    particle.Play(true);
                }
            }

            foreach (LegacyParticleState state in _legacyParticles)
            {
                if (state.particleSystem == null)
                {
                    continue;
                }

                state.particleSystem.gameObject.SetActive(state.wasActive);
            }

            _legacyParticles.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.5f);
            foreach (Wake wake in wakes)
            {
                if (wake == null)
                {
                    continue;
                }

                Gizmos.DrawSphere(transform.TransformPoint(wake.origin.x, wake.origin.y, wake.origin.z), 0.1f);
                Gizmos.DrawSphere(transform.TransformPoint(-wake.origin.x, wake.origin.y, wake.origin.z), 0.1f);
            }
        }

        private static Gradient CreateDefaultGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.78f, 0.91f, 0.98f), 0f),
                    new GradientColorKey(new Color(0.96f, 0.98f, 1f), 0.25f),
                    new GradientColorKey(new Color(0.55f, 0.8f, 0.92f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.55f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        [System.Serializable]
        public class Wake
        {
            public Vector3 origin;
            public List<WakeLine> lines = new List<WakeLine>();
        }

        [System.Serializable]
        public class WakeLine
        {
            public LineRenderer lineRenderer;
            public List<WakePoint> points = new List<WakePoint>();
            [System.NonSerialized] public MaterialPropertyBlock propertyBlock;
        }

        [System.Serializable]
        public class WakePoint
        {
            public Vector3 pos;
            public Vector3 dir;
            public float age;

            public WakePoint(Vector3 pos, Vector3 dir)
            {
                this.pos = pos;
                this.dir = dir;
                age = 0f;
            }
        }

        private struct LegacyParticleState
        {
            public ParticleSystem particleSystem;
            public bool wasActive;
        }
    }
}
