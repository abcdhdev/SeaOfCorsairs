using System;
using UnityEngine;

[Serializable]
public sealed class HarpoonAmmoDefinition
{
    [SerializeField] private string id = "harpoon-25";
    [SerializeField] private string displayName = "25 Damage";
    [SerializeField, Min(0)] private int damage = 25;

    public string Id => id;
    public string DisplayName => displayName;
    public int Damage => damage;
}
