using System.Collections.Generic;
using System.Text;
using PrimeTween;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private const string RewardNotificationTemplateResourcePath = "GameHUD/Fragments/RewardNotifications";
    private const float RewardNotificationVisibleDurationSeconds = 3f;
    private const float RewardNotificationFadeDurationSeconds = 0.45f;
    private const float RewardNotificationFadeOffsetY = -18f;

    private sealed class RewardNotificationEntry
    {
        public Label Label;
        public Tween Tween;

        public void SetVisualState(float opacity, float offsetY)
        {
            if (Label == null)
            {
                return;
            }

            Label.style.opacity = Mathf.Clamp01(opacity);
            Label.style.translate = new Translate(
                new Length(0f, LengthUnit.Pixel),
                new Length(offsetY, LengthUnit.Pixel),
                0f);
        }
    }

    private readonly List<RewardNotificationEntry> activeRewardNotifications = new List<RewardNotificationEntry>();
    private VisualElement rewardNotificationContainer;
    private Player observedRewardPlayer;

    private void BindRewardNotificationElements()
    {
        if (combatOverlayLayer == null)
        {
            rewardNotificationContainer = null;
            return;
        }

        EnsureRewardNotificationTemplateInstance();
        rewardNotificationContainer = combatOverlayLayer.Q<VisualElement>("RewardNotificationContainer");
        if (rewardNotificationContainer != null)
        {
            rewardNotificationContainer.style.display = activeRewardNotifications.Count > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    private void UpdateRewardNotifications()
    {
        EnsureRewardNotificationBinding();
    }

    private void EnsureRewardNotificationBinding()
    {
        Player localPlayer = TryGetLocalPlayer(out Player resolvedLocalPlayer) ? resolvedLocalPlayer : null;
        if (ReferenceEquals(observedRewardPlayer, localPlayer))
        {
            return;
        }

        UnbindRewardNotificationPlayer();
        observedRewardPlayer = localPlayer;

        if (observedRewardPlayer != null)
        {
            observedRewardPlayer.OnRewardGranted += OnObservedPlayerRewardGranted;
        }
    }

    private void OnObservedPlayerRewardGranted(int diamonds, int gold, int experience)
    {
        if (rewardNotificationContainer == null)
        {
            return;
        }

        string notificationText = BuildRewardNotificationText(diamonds, gold, experience);
        if (string.IsNullOrWhiteSpace(notificationText))
        {
            return;
        }

        Label label = new Label(notificationText)
        {
            pickingMode = PickingMode.Ignore
        };
        label.AddToClassList("reward-notification-label");
        rewardNotificationContainer.Add(label);
        rewardNotificationContainer.style.display = DisplayStyle.Flex;

        var entry = new RewardNotificationEntry
        {
            Label = label
        };
        entry.SetVisualState(1f, 0f);
        activeRewardNotifications.Add(entry);
        entry.Tween = Tween.Custom(
                entry,
                0f,
                RewardNotificationVisibleDurationSeconds + RewardNotificationFadeDurationSeconds,
                RewardNotificationVisibleDurationSeconds + RewardNotificationFadeDurationSeconds,
                (target, elapsed) => ApplyRewardNotificationAnimation(target, elapsed),
                ease: Ease.Linear)
            .OnComplete(entry, CompleteRewardNotification);
    }

    private static string BuildRewardNotificationText(int diamonds, int gold, int experience)
    {
        int clampedDiamonds = Mathf.Max(0, diamonds);
        int clampedGold = Mathf.Max(0, gold);
        int clampedExperience = Mathf.Max(0, experience);
        if (clampedDiamonds <= 0 && clampedGold <= 0 && clampedExperience <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("Reward: ");
        bool hasPreviousSegment = false;

        AppendRewardSegment(builder, clampedGold, "Gold", ref hasPreviousSegment);
        AppendRewardSegment(builder, clampedDiamonds, "Diamonds", ref hasPreviousSegment);
        AppendRewardSegment(builder, clampedExperience, "XP", ref hasPreviousSegment);
        return builder.ToString();
    }

    private static void AppendRewardSegment(StringBuilder builder, int amount, string label, ref bool hasPreviousSegment)
    {
        if (amount <= 0)
        {
            return;
        }

        if (hasPreviousSegment)
        {
            builder.Append(", ");
        }

        builder.Append('+');
        builder.Append(amount.ToString("N0"));
        builder.Append(' ');
        builder.Append(label);
        hasPreviousSegment = true;
    }

    private void DisposeRewardNotifications()
    {
        UnbindRewardNotificationPlayer();

        for (int i = activeRewardNotifications.Count - 1; i >= 0; i--)
        {
            RewardNotificationEntry entry = activeRewardNotifications[i];
            entry.Tween.Stop();
            entry.Label?.RemoveFromHierarchy();
        }

        activeRewardNotifications.Clear();

        if (rewardNotificationContainer != null)
        {
            rewardNotificationContainer.style.display = DisplayStyle.None;
        }
    }

    private void ClearRewardNotificationState()
    {
        UnbindRewardNotificationPlayer();

        for (int i = activeRewardNotifications.Count - 1; i >= 0; i--)
        {
            activeRewardNotifications[i].Tween.Stop();
        }

        activeRewardNotifications.Clear();
        rewardNotificationContainer = null;
        observedRewardPlayer = null;
    }

    private void UnbindRewardNotificationPlayer()
    {
        if (observedRewardPlayer == null)
        {
            return;
        }

        observedRewardPlayer.OnRewardGranted -= OnObservedPlayerRewardGranted;
        observedRewardPlayer = null;
    }

    private void EnsureRewardNotificationTemplateInstance()
    {
        if (combatOverlayLayer == null)
        {
            return;
        }

        if (combatOverlayLayer.Q<VisualElement>("RewardNotificationContainer") != null)
        {
            return;
        }

        VisualTreeAsset rewardTemplate = Resources.Load<VisualTreeAsset>(RewardNotificationTemplateResourcePath);
        if (rewardTemplate == null)
        {
            Debug.LogWarning($"GameUIController: Missing reward notification template at Resources/{RewardNotificationTemplateResourcePath}.uxml.");
            return;
        }

        combatOverlayLayer.Add(rewardTemplate.Instantiate());
    }

    private static void ApplyRewardNotificationAnimation(RewardNotificationEntry entry, float elapsed)
    {
        if (entry?.Label == null)
        {
            return;
        }

        if (elapsed <= RewardNotificationVisibleDurationSeconds)
        {
            entry.SetVisualState(1f, 0f);
            return;
        }

        float fadeProgress = Mathf.Clamp01((elapsed - RewardNotificationVisibleDurationSeconds) / RewardNotificationFadeDurationSeconds);
        float opacity = 1f - fadeProgress;
        float offsetY = Mathf.Lerp(0f, RewardNotificationFadeOffsetY, fadeProgress);
        entry.SetVisualState(opacity, offsetY);
    }

    private void CompleteRewardNotification(RewardNotificationEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        entry.Label?.RemoveFromHierarchy();
        activeRewardNotifications.Remove(entry);

        if (rewardNotificationContainer != null && activeRewardNotifications.Count == 0)
        {
            rewardNotificationContainer.style.display = DisplayStyle.None;
        }
    }
}
