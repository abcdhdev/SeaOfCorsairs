using System;
using UnityEngine;

public interface IHealthSystem
{
    int MaxHealth { get; }
    int CurrentHealth { get; }

    event Action<float> OnHealthChanged;

    void TakeDamage(int damage);
    void StartRepairing();
    void StopRepairing();
}