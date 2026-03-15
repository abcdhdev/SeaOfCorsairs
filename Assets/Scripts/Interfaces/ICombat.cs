using UnityEngine;

public interface ICombat
{
    void EnterCombat(GameObject aggressor);
    void ExitCombat(GameObject aggressor);
}