using System;
using UnityEngine;

[Serializable]
public sealed class WorldMapTravelZone
{
    [SerializeField] private Vector3 center = Vector3.zero;
    [SerializeField] private Vector3 size = new(32f, 16f, 32f);

    public Vector3 Center
    {
        get => center;
        set => center = value;
    }

    public Vector3 Size
    {
        get => size;
        set => size = new Vector3(
            Mathf.Max(0.1f, value.x),
            Mathf.Max(0.1f, value.y),
            Mathf.Max(0.1f, value.z));
    }

    public bool IsConfigured => size.x > 0.01f && size.y > 0.01f && size.z > 0.01f;

    public bool Contains(Transform root, Vector3 worldPosition)
    {
        if (root == null)
        {
            return false;
        }

        return ContainsLocal(root.InverseTransformPoint(worldPosition));
    }

    public bool ContainsLocal(Vector3 localPosition)
    {
        Vector3 extents = size * 0.5f;
        return localPosition.x >= center.x - extents.x &&
               localPosition.x <= center.x + extents.x &&
               localPosition.z >= center.z - extents.z &&
               localPosition.z <= center.z + extents.z;
    }

    public Vector3 GetWorldCenter(Transform root)
    {
        return root != null ? root.TransformPoint(center) : center;
    }

    public void DrawGizmos(Transform root, Color color)
    {
        if (root == null)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = root.localToWorldMatrix;
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);
        Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.1f);
        Gizmos.DrawCube(center, size);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
