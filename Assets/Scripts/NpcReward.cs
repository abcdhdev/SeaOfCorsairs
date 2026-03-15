using System;
using UnityEngine;

[Serializable]
public struct NpcReward
{
    [SerializeField, Min(0)] private int pearls;
    [SerializeField, Min(0)] private int gold;
    [SerializeField, Min(0)] private int experience;

    public int Pearls => Mathf.Max(0, pearls);
    public int Gold => Mathf.Max(0, gold);
    public int Experience => Mathf.Max(0, experience);

    public bool IsEmpty => Pearls <= 0 && Gold <= 0 && Experience <= 0;
}
