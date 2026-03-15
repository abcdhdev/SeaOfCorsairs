using UnityEngine;

public interface ITargetable
{
    GameObject TargetGameObject { get; }
    bool CanBeTargeted { get; }
}
