using System.Collections;
using System;
using UnityEngine;
using PrimeTween;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class Cannon : NetworkBehaviour
{
    [SerializeField, HideInInspector] private GameObject cannonballPrefab;
    [SerializeField, HideInInspector] private float fireSpeed = 10.0f;
    [SerializeField, HideInInspector] private float arcHeightFactor = 0.2f;
    [SerializeField, HideInInspector] private int damage = 20;
    [SerializeField, HideInInspector] private int baseDamage = 20;
    [SerializeField, HideInInspector] private int ammoBonusDamage;
    [SerializeField, HideInInspector] private float maxHitDistance = 150f;
    [SerializeField, HideInInspector] private Material projectileMaterialOverride;

    private PlayerAttack _playerAttack;
    public float FireSpeed => Mathf.Max(0.01f, fireSpeed);

    private PlayerAttack Attack
    {
        get
        {
            if (_playerAttack == null)
                _playerAttack = GetComponent<PlayerAttack>();
            return _playerAttack;
        }
    }

    public void ApplySettings(
        GameObject newCannonballPrefab,
        float newFireSpeed,
        float newArcHeightFactor,
        int newBaseDamage,
        float newMaxHitDistance,
        float newShootingInterval)
    {
        if (newCannonballPrefab != null)
        {
            cannonballPrefab = newCannonballPrefab;
        }

        fireSpeed = Mathf.Max(0.01f, newFireSpeed);
        arcHeightFactor = Mathf.Max(0f, newArcHeightFactor);
        baseDamage = Mathf.Max(0, newBaseDamage);
        RecalculateDamage();
        maxHitDistance = Mathf.Max(0f, newMaxHitDistance);
    }

    public void ApplyAmmoOverride(int newAmmoBonusDamage, Material newProjectileMaterial)
    {
        ammoBonusDamage = Mathf.Max(0, newAmmoBonusDamage);
        projectileMaterialOverride = newProjectileMaterial;
        RecalculateDamage();
    }

    public void FireAt(GameObject target, bool applyImpactDamage = true)
    {
        GameObject cannonball = Instantiate(cannonballPrefab, transform.position, Quaternion.identity);
        ApplyProjectileMaterial(cannonball);

        Vector3 startPos = transform.position;
        Vector3 lastKnownTargetPos = target.transform.position;
        
        float distance = Vector3.Distance(startPos, lastKnownTargetPos);
        float tweenDuration = distance / fireSpeed;

        Tween.Custom(cannonball, 0f, 1f, tweenDuration, (cb, t) =>
        {
            if (target != null)
                lastKnownTargetPos = target.transform.position;
            
            float currentDistance = Vector3.Distance(startPos, lastKnownTargetPos);
            float dynamicArcHeight = currentDistance * arcHeightFactor;

            Vector3 linearPos = Vector3.Lerp(startPos, lastKnownTargetPos, t);
            float height = 4 * dynamicArcHeight * t * (1 - t);
            cb.transform.position = linearPos + Vector3.up * height;
        }, ease: Ease.Linear)
        .OnComplete(() =>
        {
            Destroy(cannonball);
            if (applyImpactDamage)
            {
                OnProjectileImpact(target);
            }
        });
    }

    private void ApplyProjectileMaterial(GameObject projectile)
    {
        if (projectile == null || projectileMaterialOverride == null)
        {
            return;
        }

        Renderer projectileRenderer = projectile.GetComponentInChildren<Renderer>();
        if (projectileRenderer == null)
        {
            return;
        }

        projectileRenderer.sharedMaterial = projectileMaterialOverride;
    }

    private void RecalculateDamage()
    {
        damage = Mathf.Max(0, baseDamage + ammoBonusDamage);
    }

    private void OnProjectileImpact(GameObject target)
    {
        if (target == null) return;

        // Delegate all damage to PlayerAttack (server-authoritative path)
        if (Attack != null)
        {
            Attack.RequestDamage(target);
        }
        else
        {
            Debug.LogWarning($"Cannon on {gameObject.name}: Missing PlayerAttack; skipping damage.");
        }
    }

    public void PlayReplicatedFire(GameObject target)
    {
        if (target != null)
        {
            FireAt(target, false);
        }
    }

}
