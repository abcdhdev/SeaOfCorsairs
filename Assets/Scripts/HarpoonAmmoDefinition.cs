using System;
using UnityEngine;

[Serializable]
public sealed class HarpoonAmmoDefinition
{
    [SerializeField] private string id = "harpoon-25";
    [SerializeField] private string displayName = "25 Damage";
    [SerializeField, Min(0)] private int damage = 25;
    [SerializeField] private Color projectileColor = new Color(0.8039216f, 0.49803922f, 0.19607843f, 1f);

    public string Id => id;
    public string DisplayName => displayName;
    public int Damage => damage;
    public Color ProjectileColor => projectileColor;
}
