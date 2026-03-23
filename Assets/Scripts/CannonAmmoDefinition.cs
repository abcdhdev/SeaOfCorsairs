using System;
using UnityEngine;

[Serializable]
public sealed class CannonAmmoDefinition
{
    [SerializeField] private string id = "standard";
    [SerializeField] private string displayName = "Standard";
    // Base shot damage before equipped cannon bonuses and critical-hit modifiers are applied.
    [SerializeField, Min(0)] private int damage = 20;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField, HideInInspector] private Material projectileMaterial;

    public string Id => id;
    public string DisplayName => displayName;
    public int Damage => damage;
    public GameObject ProjectilePrefab => projectilePrefab;
}

