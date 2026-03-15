using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void EnsureNpcHealthTemplateInstance()
    {
        if (combatOverlayLayer == null)
        {
            return;
        }

        if (combatOverlayLayer.Q<VisualElement>("NpcHealthBox") != null)
        {
            return;
        }

        VisualTreeAsset npcHealthTemplate = Resources.Load<VisualTreeAsset>(NpcHealthTemplateResourcePath);
        if (npcHealthTemplate == null)
        {
            if (!missingNpcHealthTemplateLogged)
            {
                Debug.LogWarning($"GameUIController: Missing NPC health template at Resources/{NpcHealthTemplateResourcePath}.uxml.");
                missingNpcHealthTemplateLogged = true;
            }

            return;
        }

        combatOverlayLayer.Add(npcHealthTemplate.Instantiate());
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

    private void TrackNpc(NPC npc)
    {
        if (trackedNpc == npc)
        {
            if (trackedNpc != null)
            {
                UpdateNpcHealthDisplay();
            }

            return;
        }

        if (trackedNpc != null)
        {
            trackedNpc.OnHealthChanged -= OnTrackedNpcHealthChanged;
        }

        trackedNpc = npc;

        if (trackedNpc == null)
        {
            SetNpcHealthVisible(false);
            return;
        }

        trackedNpc.OnHealthChanged += OnTrackedNpcHealthChanged;
        UpdateNpcHealthDisplay();
        SetNpcHealthVisible(true);
    }

    private void OnTrackedNpcHealthChanged(float normalizedHealth)
    {
        if (trackedNpc == null)
        {
            SetNpcHealthVisible(false);
            return;
        }

        UpdateNpcHealthDisplay();
    }

    private void UpdateNpcHealthDisplay()
    {
        if (trackedNpc == null)
        {
            return;
        }

        int maxHealth = Mathf.Max(trackedNpc.MaxHealth, 1);
        int currentHealth = Mathf.Clamp(trackedNpc.CurrentHealth, 0, maxHealth);
        float healthPercent = currentHealth / (float)maxHealth;

        if (npcNameLabel != null)
        {
            npcNameLabel.text = GetNpcDisplayName(trackedNpc);
        }

        if (npcHealthLabel != null)
        {
            npcHealthLabel.text = $"{currentHealth} / {maxHealth}";
        }

        if (npcHealthBarFill != null)
        {
            npcHealthBarFill.style.width = new Length(healthPercent * 100f, LengthUnit.Percent);
        }
    }

    private void SetNpcHealthVisible(bool visible)
    {
        if (npcHealthBox != null)
        {
            npcHealthBox.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

    private static string GetNpcDisplayName(NPC npc)
    {
        return npc == null ? "Unknown Target" : npc.DisplayName;
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
        if (localPlayer == null && PlayerManager.Instance != null)
        {
            localPlayer = PlayerManager.Instance.LocalPlayer;
        }

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
