using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class CaptainBarakVaneWorldMarker : MonoBehaviour, IClickable
{
    private const string VisualRootName = "PortraitVisual";
    private static Mesh sharedPortraitMesh;
    private static Material sharedPortraitMaterial;

    [Header("Placement")]
    [SerializeField] private bool snapToGroundOnEnable = true;
    [SerializeField, Min(1f)] private float groundSnapRayHeight = 120f;

    [Header("Portrait")]
    [SerializeField] private float portraitWidth = 7f;
    [SerializeField] private float portraitHeight = 9.5f;
    [SerializeField] private float colliderDepth = 1.5f;
    [SerializeField] private float idleBobAmplitude = 0.35f;
    [SerializeField] private float idleBobFrequency = 1.15f;

    private BoxCollider interactionCollider;
    private Transform visualRoot;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Vector3 visualBaseLocalPosition;

    private void Awake()
    {
        EnsureVisualSetup();
    }

    private void OnEnable()
    {
        EnsureVisualSetup();

        if (snapToGroundOnEnable)
        {
            SnapToGround();
        }
    }

    private void OnValidate()
    {
        portraitWidth = Mathf.Max(1f, portraitWidth);
        portraitHeight = Mathf.Max(1f, portraitHeight);
        colliderDepth = Mathf.Max(0.25f, colliderDepth);
        idleBobAmplitude = Mathf.Max(0f, idleBobAmplitude);
        idleBobFrequency = Mathf.Max(0f, idleBobFrequency);
        EnsureVisualSetup();
    }

    private void LateUpdate()
    {
        FaceMainCamera();
        UpdateIdleBob();
    }

    public void OnClick(Vector3 position)
    {
        GameUIController controller = FindFirstObjectByType<GameUIController>();
        if (controller == null)
        {
            Debug.LogWarning("CaptainBarakVaneWorldMarker: Could not find an active GameUIController.");
            return;
        }

        controller.ShowArubaCauldronFromWorld();
    }

    private void EnsureVisualSetup()
    {
        interactionCollider = GetComponent<BoxCollider>();
        if (interactionCollider == null)
        {
            interactionCollider = gameObject.AddComponent<BoxCollider>();
        }

        if (visualRoot == null)
        {
            Transform existingChild = transform.Find(VisualRootName);
            if (existingChild != null)
            {
                visualRoot = existingChild;
            }
            else
            {
                GameObject visualObject = new GameObject(VisualRootName);
                visualObject.transform.SetParent(transform, false);
                visualRoot = visualObject.transform;
            }
        }

        if (meshFilter == null)
        {
            meshFilter = visualRoot.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = visualRoot.gameObject.AddComponent<MeshFilter>();
            }
        }

        if (meshRenderer == null)
        {
            meshRenderer = visualRoot.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = visualRoot.gameObject.AddComponent<MeshRenderer>();
            }
        }

        meshFilter.sharedMesh = GetSharedPortraitMesh();
        meshRenderer.sharedMaterial = GetSharedPortraitMaterial();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.allowOcclusionWhenDynamic = false;
        meshRenderer.sortingOrder = 25;

        visualBaseLocalPosition = new Vector3(0f, portraitHeight * 0.5f, 0f);
        visualRoot.localPosition = visualBaseLocalPosition;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = new Vector3(portraitWidth, portraitHeight, 1f);

        interactionCollider.center = visualBaseLocalPosition;
        interactionCollider.size = new Vector3(portraitWidth, portraitHeight, colliderDepth);
    }

    private void FaceMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        Vector3 toCamera = mainCamera.transform.position - transform.position;
        if (toCamera.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toCamera.normalized, mainCamera.transform.up);
    }

    private void UpdateIdleBob()
    {
        if (visualRoot == null || idleBobAmplitude <= 0f || idleBobFrequency <= 0f)
        {
            return;
        }

        float bobOffset = Mathf.Sin(Time.unscaledTime * idleBobFrequency) * idleBobAmplitude;
        visualRoot.localPosition = visualBaseLocalPosition + Vector3.up * bobOffset;
    }

    private void SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * groundSnapRayHeight;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, groundSnapRayHeight * 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return;
        }

        System.Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));
        for (int index = 0; index < hits.Length; index++)
        {
            Collider hitCollider = hits[index].collider;
            if (hitCollider == null)
            {
                continue;
            }

            Transform hitTransform = hitCollider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 snappedPosition = transform.position;
            snappedPosition.y = hits[index].point.y;
            transform.position = snappedPosition;
            return;
        }
    }

    private static Mesh GetSharedPortraitMesh()
    {
        if (sharedPortraitMesh != null)
        {
            return sharedPortraitMesh;
        }

        sharedPortraitMesh = new Mesh
        {
            name = "CaptainBarakVanePortraitMesh",
            hideFlags = HideFlags.HideAndDontSave
        };
        sharedPortraitMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        sharedPortraitMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        sharedPortraitMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        sharedPortraitMesh.RecalculateNormals();
        sharedPortraitMesh.RecalculateBounds();
        return sharedPortraitMesh;
    }

    private static Material GetSharedPortraitMaterial()
    {
        Texture2D portraitTexture = ArubaCauldronRuntime.LoadPortrait();
        if (sharedPortraitMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Transparent")
                            ?? Shader.Find("Unlit/Texture")
                            ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return null;
            }

            sharedPortraitMaterial = new Material(shader)
            {
                name = "CaptainBarakVanePortraitMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (sharedPortraitMaterial.HasProperty("_BaseColor"))
        {
            sharedPortraitMaterial.SetColor("_BaseColor", Color.white);
        }

        if (sharedPortraitMaterial.HasProperty("_Color"))
        {
            sharedPortraitMaterial.SetColor("_Color", Color.white);
        }

        if (sharedPortraitMaterial.HasProperty("_Cull"))
        {
            sharedPortraitMaterial.SetFloat("_Cull", 0f);
        }

        if (sharedPortraitMaterial.HasProperty("_Surface"))
        {
            sharedPortraitMaterial.SetFloat("_Surface", 1f);
        }

        if (sharedPortraitMaterial.HasProperty("_ZWrite"))
        {
            sharedPortraitMaterial.SetFloat("_ZWrite", 0f);
        }

        if (portraitTexture != null)
        {
            if (sharedPortraitMaterial.HasProperty("_BaseMap"))
            {
                sharedPortraitMaterial.SetTexture("_BaseMap", portraitTexture);
            }

            if (sharedPortraitMaterial.HasProperty("_MainTex"))
            {
                sharedPortraitMaterial.SetTexture("_MainTex", portraitTexture);
            }
        }

        return sharedPortraitMaterial;
    }
}
