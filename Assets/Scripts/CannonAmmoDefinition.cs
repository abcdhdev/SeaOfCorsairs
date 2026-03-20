using System;
using UnityEngine;

[Serializable]
public sealed class CannonAmmoDefinition
{
    [SerializeField] private string id = "standard";
    [SerializeField] private string displayName = "Standard";
    // Bonus damage added on top of the configured base cannon damage.
    [SerializeField, Min(0)] private int damage = 20;
    [SerializeField] private Material projectileMaterial;

    public string Id => id;
    public string DisplayName => displayName;
    public int Damage => damage;
    public Material ProjectileMaterial => projectileMaterial;
}

