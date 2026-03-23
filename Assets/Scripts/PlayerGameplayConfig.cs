using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerGameplayConfig", menuName = "Sea Wars/Player Gameplay Config")]
public sealed class PlayerGameplayConfig : ScriptableObject
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0.01f)] private float repairRate = 2f;
    [SerializeField, Min(0)] private int repairAmount = 5;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float navMeshSpeed = 45f;
    [SerializeField, Min(0f)] private float navMeshAcceleration = 8f;
    [SerializeField, Min(0f)] private float navMeshAngularSpeed = 120f;
    [SerializeField, Min(0f)] private float navMeshStoppingDistance = 0f;

    [Header("Cannon")]
    [SerializeField, Min(0f)] private float cannonArcHeightFactor = 0.2f;

    [Header("Cannon Ammo")]
    [SerializeField] private List<CannonAmmoDefinition> cannonAmmoTypes = new();
    [SerializeField] private List<HarpoonAmmoDefinition> harpoonAmmoTypes = new();

    [Header("World Health UI")]
    [SerializeField] private bool healthBarPlaceUnderTarget = false;
    [SerializeField] private Vector3 healthBarWorldOffset = Vector3.zero;
    [SerializeField] private bool hideHealthBarWhenEmpty = true;

    [Header("World Nameplate")]
    [SerializeField] private bool showWorldNameplate = true;
    [SerializeField, Min(0f)] private float worldNameplateMaxRenderDistance = 300f;

    public int MaxHealth => maxHealth;
    public float RepairRate => repairRate;
    public int RepairAmount => repairAmount;
    public float NavMeshSpeed => navMeshSpeed;
    public float NavMeshAcceleration => navMeshAcceleration;
    public float NavMeshAngularSpeed => navMeshAngularSpeed;
    public float NavMeshStoppingDistance => navMeshStoppingDistance;
    public float CannonArcHeightFactor => cannonArcHeightFactor;
    public IReadOnlyList<CannonAmmoDefinition> CannonAmmoTypes => cannonAmmoTypes;
    public IReadOnlyList<HarpoonAmmoDefinition> HarpoonAmmoTypes => harpoonAmmoTypes;
    public bool HealthBarPlaceUnderTarget => healthBarPlaceUnderTarget;
    public Vector3 HealthBarWorldOffset => healthBarWorldOffset;
    public bool HideHealthBarWhenEmpty => hideHealthBarWhenEmpty;
    public bool ShowWorldNameplate => showWorldNameplate;
    public float WorldNameplateMaxRenderDistance => worldNameplateMaxRenderDistance;
}
