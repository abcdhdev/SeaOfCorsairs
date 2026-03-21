using UnityEngine;

public interface IDamageSourceReceiver
{
    void TakeDamage(int damage, GameObject damageSource);
}
