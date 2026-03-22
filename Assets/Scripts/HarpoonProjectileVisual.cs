using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class HarpoonProjectileVisual : MonoBehaviour
{
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorPropertyId = Shader.PropertyToID("_Color");
    private static Mesh sharedMesh;
    private static Material sharedMaterial;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool isConfigured;

    public static GameObject Create(Vector3 position, Color color, float scale = 0.75f)
    {
        GameObject projectile = new GameObject("HarpoonProjectile");
        projectile.transform.position = position;

        HarpoonProjectileVisual visual = projectile.AddComponent<HarpoonProjectileVisual>();
        visual.Initialize(color, scale);
        return projectile;
    }

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();

        meshFilter.sharedMesh = GetSharedMesh();
        Material material = GetSharedMaterial();
        if (material != null)
        {
            meshRenderer.sharedMaterial = material;
        }

        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    public void Initialize(Color color, float scale)
    {
        transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        SetColor(color);
        isConfigured = true;
        FaceCamera();
    }

    private void LateUpdate()
    {
        if (!isConfigured)
        {
            return;
        }

        FaceCamera();
    }

    private void FaceCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 toCamera = camera.transform.position - transform.position;
        if (toCamera.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toCamera, camera.transform.up);
    }

    private void SetColor(Color color)
    {
        propertyBlock = propertyBlock ?? new MaterialPropertyBlock();
        propertyBlock.SetColor(BaseColorPropertyId, color);
        propertyBlock.SetColor(LegacyColorPropertyId, color);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private static Mesh GetSharedMesh()
    {
        if (sharedMesh != null)
        {
            return sharedMesh;
        }

        sharedMesh = new Mesh
        {
            name = "HarpoonProjectileTriangle",
            hideFlags = HideFlags.HideAndDontSave
        };
        sharedMesh.vertices = new[]
        {
            new Vector3(0f, 0.5f, 0f),
            new Vector3(-0.3f, -0.25f, 0f),
            new Vector3(0.3f, -0.25f, 0f)
        };
        sharedMesh.triangles = new[] { 0, 1, 2 };
        sharedMesh.uv = new[]
        {
            new Vector2(0.5f, 1f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f)
        };
        sharedMesh.RecalculateNormals();
        sharedMesh.RecalculateBounds();
        return sharedMesh;
    }

    private static Material GetSharedMaterial()
    {
        if (sharedMaterial != null)
        {
            return sharedMaterial;
        }

        Shader shader = Shader.Find("SeaOfCorsair/HarpoonProjectileUnlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogWarning("HarpoonProjectileVisual: Could not find a compatible unlit shader.");
            return null;
        }

        sharedMaterial = new Material(shader)
        {
            name = "HarpoonProjectileMaterial",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedMaterial;
    }
}
