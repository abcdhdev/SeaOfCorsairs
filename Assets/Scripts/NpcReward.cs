using System;
using UnityEngine;

[Serializable]
public struct NpcReward
{
    [SerializeField, Min(0)] private int diamonds;
    [SerializeField, Min(0)] private int gold;
    [SerializeField, Min(0)] private int experience;

    public int Diamonds => Mathf.Max(0, diamonds);
    public int Gold => Mathf.Max(0, gold);
    public int Experience => Mathf.Max(0, experience);

    public bool IsEmpty => Diamonds <= 0 && Gold <= 0 && Experience <= 0;
}
