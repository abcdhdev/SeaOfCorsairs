using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void EnsureHealthTemplateInstance()
    {
        if (combatOverlayLayer == null)
        {
            return;
        }

        if (combatOverlayLayer.Q<VisualElement>("HealthBox") != null)
        {
            return;
        }

        VisualTreeAsset healthTemplate = Resources.Load<VisualTreeAsset>(HealthTemplateResourcePath);
        if (healthTemplate == null)
        {
            if (!missingHealthTemplateLogged)
            {
                Debug.LogWarning($"GameUIController: Missing health template at Resources/{HealthTemplateResourcePath}.uxml.");
                missingHealthTemplateLogged = true;
            }

            return;
        }

        combatOverlayLayer.Add(healthTemplate.Instantiate());
    }

    private IHealthSystem GetSelectedHealthTarget()
    {
        GameObject selectedTarget = GetSelectedTarget();
        if (selectedTarget == null)
        {
            return null;
        }

        return selectedTarget.GetComponent<IHealthSystem>() ?? selectedTarget.GetComponentInParent<IHealthSystem>();
    }

    private NPC GetSelectedNpc()
    {
        GameObject selectedTarget = GetSelectedTarget();
        return selectedTarget != null ? selectedTarget.GetComponentInParent<NPC>() : null;
    }

    private static GameObject GetSelectedTarget()
    {
        return SelectObject.Instance != null ? SelectObject.Instance.SelectedTarget : null;
    }

    private void TrackHealthTarget(IHealthSystem healthTarget)
    {
        Component healthTargetComponent = healthTarget as Component;
        if (trackedHealthTargetComponent == healthTargetComponent &&
            ReferenceEquals(trackedHealthTarget, healthTarget))
        {
            return;
        }

        if (trackedHealthTarget != null)
        {
            trackedHealthTarget.OnHealthChanged -= OnTrackedHealthChanged;
        }

        trackedHealthTarget = healthTarget;
        trackedHealthTargetComponent = healthTargetComponent;

        if (trackedHealthTarget == null || trackedHealthTargetComponent == null)
        {
            trackedHealthTarget = null;
            trackedHealthTargetComponent = null;
            SetHealthVisible(false);
            return;
        }

        trackedHealthTarget.OnHealthChanged += OnTrackedHealthChanged;
        UpdateHealthDisplay();
        SetHealthVisible(true);
    }

    private void OnTrackedHealthChanged(float normalizedHealth)
    {
        if (trackedHealthTarget == null || trackedHealthTargetComponent == null)
        {
            SetHealthVisible(false);
            return;
        }

        UpdateHealthDisplay();
    }

    private void UpdateHealthDisplay()
    {
        if (trackedHealthTarget == null || trackedHealthTargetComponent == null)
        {
            return;
        }

        int maxHealth = Mathf.Max(trackedHealthTarget.MaxHealth, 1);
        int currentHealth = Mathf.Clamp(trackedHealthTarget.CurrentHealth, 0, maxHealth);
        float healthPercent = currentHealth / (float)maxHealth;

        if (targetNameLabel != null)
        {
            targetNameLabel.text = ResolveHealthTargetDisplayName(trackedHealthTarget);
        }

        if (healthLabel != null)
        {
            healthLabel.text = $"{currentHealth} / {maxHealth}";
        }

        if (healthBarFill != null)
        {
            healthBarFill.style.width = new Length(healthPercent * 100f, LengthUnit.Percent);
        }
    }

    private void SetHealthVisible(bool visible)
    {
        if (healthBox != null)
        {
            healthBox.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void SetCombatOverlayVisible(bool visible)
    {
        if (combatOverlayLayer != null)
        {
            combatOverlayLayer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void UpdateDeathOverlay()
    {
        bool shouldShow = false;
        string timerText = "Respawning...";

        if (TryGetLocalPlayer(out Player localPlayer) && localPlayer != null && localPlayer.IsDeadNetworkState)
        {
            shouldShow = true;
            float remainingSeconds = localPlayer.RespawnTimeRemainingSeconds;

            if (!float.IsNaN(remainingSeconds) && !float.IsInfinity(remainingSeconds) && remainingSeconds > 0.05f)
            {
                const int maxDisplayedRespawnSeconds = 86_400;
                int displaySeconds = Mathf.Clamp(Mathf.CeilToInt(remainingSeconds), 1, maxDisplayedRespawnSeconds);
                timerText = $"Respawning in {displaySeconds}s";
            }
        }

        if (deadOverlayTimerLabel != null)
        {
            deadOverlayTimerLabel.text = timerText;
        }

        SetDeadOverlayVisible(shouldShow);
    }

    private void SetDeadOverlayVisible(bool visible)
    {
        if (deadOverlayRoot != null)
        {
            deadOverlayRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static string ResolveHealthTargetDisplayName(IHealthSystem healthTarget)
    {
        if (healthTarget is ISeaEntity seaEntity)
        {
            return SanitizeDisplayName(seaEntity.DisplayName, seaEntity.EntityType.ToString());
        }

        return healthTarget is Component component
            ? ResolveObjectDisplayName(component.gameObject, "Unknown Target")
            : "Unknown Target";
    }

    private static string ResolveObjectDisplayName(GameObject targetObject, string fallbackName)
    {
        if (targetObject == null)
        {
            return fallbackName;
        }

        string rawName = targetObject.name;
        const string cloneSuffix = "(Clone)";
        if (rawName.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length).TrimEnd();
        }

        return SanitizeDisplayName(rawName, fallbackName);
    }

    private static string SanitizeDisplayName(string value, string fallbackName)
    {
        string resolvedValue = string.IsNullOrWhiteSpace(value) ? fallbackName : value.Trim();
        string sanitizedValue = UiTextSanitizer.SanitizeForLabel(resolvedValue, collapseWhitespace: true);
        return string.IsNullOrWhiteSpace(sanitizedValue) ? fallbackName : sanitizedValue;
    }

    private void UpdatePlayerHealthBar()
    {
        int currentHealth = 0;
        int maxHealth = 1;

        if (TryGetLocalPlayer(out Player localPlayer))
        {
            maxHealth = Mathf.Max(localPlayer.MaxHealth, 1);
            currentHealth = Mathf.Clamp(localPlayer.CurrentHealth, 0, maxHealth);
        }

        if (displayedPlayerHealth == currentHealth && displayedPlayerMaxHealth == maxHealth)
        {
            return;
        }

        displayedPlayerHealth = currentHealth;
        displayedPlayerMaxHealth = maxHealth;

        if (playerHpLabel != null)
        {
            playerHpLabel.text = $"HP: {currentHealth}/{maxHealth}";
        }

        if (playerHpBarFill != null)
        {
            float healthPercent = currentHealth / (float)Mathf.Max(maxHealth, 1);
            playerHpBarFill.style.width = new Length(healthPercent * 100f, LengthUnit.Percent);
        }
    }

    private void UpdatePlayerExpBar()
    {
        int requiredExperience = Mathf.Max(1, playerExperienceToNextLevel);
        int currentExperience = Mathf.Max(0, playerExperience);

        if (TryGetLocalPlayer(out Player localPlayer))
        {
            currentExperience = Mathf.Max(0, localPlayer.Experience);
        }

        currentExperience = Mathf.Clamp(currentExperience, 0, requiredExperience);

        if (displayedPlayerExperience == currentExperience && displayedPlayerExperienceToNext == requiredExperience)
        {
            return;
        }

        displayedPlayerExperience = currentExperience;
        displayedPlayerExperienceToNext = requiredExperience;

        if (playerExpLabel != null)
        {
            playerExpLabel.text = $"XP: {currentExperience}/{requiredExperience}";
        }

        if (playerExpBarFill != null)
        {
            float expPercent = currentExperience / (float)requiredExperience;
            playerExpBarFill.style.width = new Length(expPercent * 100f, LengthUnit.Percent);
        }
    }

    private void UpdatePlayerWalletLabels()
    {
        int currentGold = 0;
        int currentDiamonds = 0;

        if (TryGetLocalPlayer(out Player localPlayer))
        {
            currentGold = Mathf.Max(0, localPlayer.Gold);
            currentDiamonds = Mathf.Max(0, localPlayer.Diamonds);
        }

        if (displayedPlayerGold == currentGold && displayedPlayerDiamonds == currentDiamonds)
        {
            return;
        }

        displayedPlayerGold = currentGold;
        displayedPlayerDiamonds = currentDiamonds;

        if (resourceGoldLabel != null)
        {
            resourceGoldLabel.text = currentGold.ToString("N0");
        }

        if (resourceDiamondLabel != null)
        {
            resourceDiamondLabel.text = currentDiamonds.ToString("N0");
        }
    }

    private bool TryGetLocalPlayer(out Player localPlayer)
    {
        if (cachedLocalPlayer != null)
        {
            if (cachedLocalPlayer.IsOwner && cachedLocalPlayer.IsSpawned)
            {
                localPlayer = cachedLocalPlayer;
                return true;
            }

            cachedLocalPlayer = null;
        }

        localPlayer = Player.LocalPlayer;
        if (localPlayer == null)
        {
            Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && players[i].IsOwner && players[i].IsSpawned)
                {
                    localPlayer = players[i];
                    break;
                }
            }
        }

        if (localPlayer != null && (!localPlayer.IsOwner || !localPlayer.IsSpawned))
        {
            localPlayer = null;
        }

        cachedLocalPlayer = localPlayer;
        return cachedLocalPlayer != null;
    }
}
