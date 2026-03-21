using UnityEngine;

public interface ISeaEntity
{
    SeaEntityType EntityType { get; }
    GameObject EntityGameObject { get; }
    string DisplayName { get; }
}
