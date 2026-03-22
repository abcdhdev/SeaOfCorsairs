using UnityEngine;

/// <summary>
/// Spawns floating world-space damage or heal numbers.
/// Each number is a temporary GameObject with a <see cref="DamageNumber"/> component.
/// </summary>
public static class DamageNumberService
{
    private const float WorldYOffset = 3f;

    public static void Show(
        Vector3 worldPosition,
        int amount,
        bool isHeal,
        DamageNumberEffectStyle effectStyle = DamageNumberEffectStyle.Default)
    {
        if (amount <= 0)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        float jitterX = Random.Range(-1f, 1f);
        float jitterZ = Random.Range(-1f, 1f);
        Vector3 spawnPos = worldPosition + new Vector3(jitterX, WorldYOffset, jitterZ);

        GameObject go = new GameObject("DamageNumber")
        {
            hideFlags = HideFlags.DontSave
        };
        go.transform.position = spawnPos;

        DamageNumber damageNumber = go.AddComponent<DamageNumber>();
        damageNumber.Initialize(amount, isHeal, cam, effectStyle);
    }
}
