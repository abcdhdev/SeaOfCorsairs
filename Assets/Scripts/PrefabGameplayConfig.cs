using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PrefabGameplayConfig", menuName = "Sea Wars/Prefab Gameplay Config")]
public class PrefabGameplayConfig : ScriptableObject
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

    [Header("NPC Roaming")]
    [SerializeField, Min(0f)] private float npcRoamRadius = 200f;
    [SerializeField, Min(0f)] private float npcRoamWaitTime = 3f;
    [SerializeField, Min(0f)] private float npcLeashRadius = 300f;
    [SerializeField, Min(0f)] private float npcHomeArrivalDistance = 8f;

    [Header("Cannon")]
    [SerializeField] private GameObject cannonballPrefab;
    [SerializeField, Min(0.01f)] private float cannonFireSpeed = 100f;
    [SerializeField, Min(0f)] private float cannonArcHeightFactor = 0.2f;
    // Base damage before ammo bonuses are applied.
    [SerializeField, Min(0)] private int cannonDamage = 20;
    [SerializeField, Min(0f)] private float cannonMaxHitDistance = 150f;
    [SerializeField, Min(0.05f)] private float cannonShootingInterval = 2f;

    [Header("Cannon Ammo")]
    [SerializeField] private List<CannonAmmoDefinition> cannonAmmoTypes = new();

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
    public float NpcRoamRadius => npcRoamRadius;
    public float NpcRoamWaitTime => npcRoamWaitTime;
    public float NpcLeashRadius => npcLeashRadius;
    public float NpcHomeArrivalDistance => npcHomeArrivalDistance;
    public GameObject CannonballPrefab => cannonballPrefab;
    public float CannonFireSpeed => cannonFireSpeed;
    public float CannonArcHeightFactor => cannonArcHeightFactor;
    public int CannonDamage => cannonDamage;
    public float CannonMaxHitDistance => cannonMaxHitDistance;
    public float CannonShootingInterval => cannonShootingInterval;
    public IReadOnlyList<CannonAmmoDefinition> CannonAmmoTypes => cannonAmmoTypes;
    public bool HealthBarPlaceUnderTarget => healthBarPlaceUnderTarget;
    public Vector3 HealthBarWorldOffset => healthBarWorldOffset;
    public bool HideHealthBarWhenEmpty => hideHealthBarWhenEmpty;
    public bool ShowWorldNameplate => showWorldNameplate;
    public float WorldNameplateMaxRenderDistance => worldNameplateMaxRenderDistance;
}
