using UnityEngine;

public static class CombatActionItemUtility
{
    private const float BlackGunpowderDamageMultiplier = 1.1f;
    private const float AgwesArmorPlatingDamageMultiplier = 0.9f;

    public static int ApplyOutgoingDamageModifiers(GameObject attacker, int baseDamage)
    {
        if (baseDamage <= 0)
        {
            return 0;
        }

        if (TryGetActionItemOwner(attacker, out Player attackingPlayer) &&
            attackingPlayer.HasActionItem(PlayerActionItemType.BlackGunpowder))
        {
            return ScaleDamage(baseDamage, BlackGunpowderDamageMultiplier);
        }

        return baseDamage;
    }

    public static int ApplyIncomingDamageModifiers(
        GameObject target,
        int incomingDamage,
        GameObject damageSource,
        out DamageNumberEffectStyle effectStyle)
    {
        effectStyle = DamageNumberEffectStyle.Default;

        int resolvedDamage = Mathf.Max(0, incomingDamage);
        if (resolvedDamage <= 0)
        {
            return 0;
        }

        if (damageSource != null &&
            TryGetActionItemOwner(target, out Player defendingPlayer) &&
            defendingPlayer.HasActionItem(PlayerActionItemType.AgwesArmorPlating))
        {
            effectStyle = DamageNumberEffectStyle.AgwesArmorPlating;
            return ScaleDamage(resolvedDamage, AgwesArmorPlatingDamageMultiplier);
        }

        if (TryGetActionItemOwner(damageSource, out Player attackingPlayer) &&
            attackingPlayer.HasActionItem(PlayerActionItemType.BlackGunpowder))
        {
            effectStyle = DamageNumberEffectStyle.BlackGunpowder;
        }

        return resolvedDamage;
    }

    public static DamageNumberEffectStyle NormalizeDamageNumberEffectStyle(int effectStyleValue)
    {
        return effectStyleValue switch
        {
            (int)DamageNumberEffectStyle.BlackGunpowder => DamageNumberEffectStyle.BlackGunpowder,
            (int)DamageNumberEffectStyle.AgwesArmorPlating => DamageNumberEffectStyle.AgwesArmorPlating,
            _ => DamageNumberEffectStyle.Default
        };
    }

    private static int ScaleDamage(int damage, float multiplier)
    {
        if (damage <= 0)
        {
            return 0;
        }

        return Mathf.Max(1, Mathf.RoundToInt(damage * multiplier));
    }

    private static bool TryGetActionItemOwner(GameObject candidate, out Player player)
    {
        player = null;
        if (candidate == null)
        {
            return false;
        }

        return candidate.TryGetComponent(out player);
    }
}

public static class ActionItemIconCatalog
{
    private const string HudBlackGunpowderPath = "ActionItems/Icon_BlackGunpowder";
    private const string HudAgwesArmorPlatePath = "ActionItems/Icon_AgwesArmorPlate";
    private const string DamageBlackGunpowderPath = "ActionItems/BlackGunpowder";
    private const string DamageAgwesArmorPlatePath = "ActionItems/AgwesArmorPlate";

    private static Texture2D hudBlackGunpowderIcon;
    private static Texture2D hudAgwesArmorPlateIcon;
    private static Texture2D damageBlackGunpowderIcon;
    private static Texture2D damageAgwesArmorPlateIcon;

    public static Texture2D GetHudIcon(PlayerActionItemType actionItem)
    {
        return actionItem switch
        {
            PlayerActionItemType.BlackGunpowder => LoadTexture(ref hudBlackGunpowderIcon, HudBlackGunpowderPath),
            PlayerActionItemType.AgwesArmorPlating => LoadTexture(ref hudAgwesArmorPlateIcon, HudAgwesArmorPlatePath),
            _ => null
        };
    }

    public static Texture2D GetDamageIcon(DamageNumberEffectStyle effectStyle)
    {
        return effectStyle switch
        {
            DamageNumberEffectStyle.BlackGunpowder => LoadTexture(ref damageBlackGunpowderIcon, DamageBlackGunpowderPath),
            DamageNumberEffectStyle.AgwesArmorPlating => LoadTexture(ref damageAgwesArmorPlateIcon, DamageAgwesArmorPlatePath),
            _ => null
        };
    }

    private static Texture2D LoadTexture(ref Texture2D cache, string resourcePath)
    {
        if (cache == null)
        {
            cache = Resources.Load<Texture2D>(resourcePath);
        }

        return cache;
    }
}
