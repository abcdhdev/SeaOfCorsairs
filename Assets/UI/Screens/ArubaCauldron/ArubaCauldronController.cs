using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ArubaCauldronController : IDisposable
{
    private const bool LayoutDebugLogging = false;
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string ArubaCauldronStyleResourcePath = "ArubaCauldron/ArubaCauldron";
    private const string ArubaCauldronUxmlResourcePath = "ArubaCauldron/ArubaCauldron";
    private const string DefaultStatusMessage = "Captain Barak Vane can transmute mojo into a fresh haul of supplies.";
    private const string DefaultRewardsHint = "Every mojo has an even chance to become one of these rewards.";
    private const string ResultRewardsHint = "Captain Barak Vane returns with these spoils from the ritual.";
    private const string HelpStatusMessage = "Spend mojo first. If you are short, diamonds cover the missing mojo, and each mojo roll has an even chance at one of the four rewards.";
    private const string StatusSuccessClass = "aruba-cauldron-status-success";
    private const string StatusErrorClass = "aruba-cauldron-status-error";

    private readonly VisualElement attachTarget;
    private readonly Func<Player> localPlayerProvider;

    private VisualElement overlayRoot;
    private VisualElement backdropElement;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private VisualElement portraitImage;
    private VisualElement rewardsGrid;
    private VisualElement bonusMapsList;
    private Button closeButton;
    private Button helpButton;
    private Button startButton;
    private Button clearButton;
    private DropdownField quantityDropdown;
    private Label statusLabel;
    private Label rewardsHintLabel;
    private Label mojoValueLabel;
    private Label diamondValueLabel;
    private Label diamondCostValueLabel;
    private Label paymentHintLabel;
    private DraggableWindowController panelDragController;

    private Player observedPlayer;
    private ArubaCauldronRitualResultData latestResult;
    private bool areBonusMapsRendered;
    private bool isPendingRitual;
    private bool ignoreNextBackdropPointerUp;
    private string renderedRewardSignature = string.Empty;

    public ArubaCauldronController(VisualElement attachTarget, Func<Player> localPlayerProvider)
    {
        this.attachTarget = attachTarget;
        this.localPlayerProvider = localPlayerProvider;
    }

    public bool IsVisible => overlayRoot != null && overlayRoot.resolvedStyle.display != DisplayStyle.None;

    public VisualElement OverlayRoot => overlayRoot;

    public void Attach()
    {
        if (attachTarget == null || overlayRoot != null)
        {
            return;
        }

        LogLayout("Attach start");

        VisualTreeAsset arubaTree = Resources.Load<VisualTreeAsset>(ArubaCauldronUxmlResourcePath);
        if (arubaTree == null)
        {
            Debug.LogWarning($"ArubaCauldronController: Missing UXML resource '{ArubaCauldronUxmlResourcePath}'.");
            return;
        }

        // Keep the TemplateContainer as overlayRoot so the UXML-level <ui:Style>
        // declarations stay attached. ArubaCauldron.uss uses :root custom
        // properties, which need the template's stylesheet scope to remain intact.
        TemplateContainer container = arubaTree.Instantiate();
        overlayRoot = container;
        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyle = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyle != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyle);
        }

        StyleSheet arubaCauldronStyle = Resources.Load<StyleSheet>(ArubaCauldronStyleResourcePath);
        if (arubaCauldronStyle != null)
        {
            overlayRoot.styleSheets.Add(arubaCauldronStyle);
        }

        overlayRoot.style.position = Position.Absolute;
        overlayRoot.style.top = 0;
        overlayRoot.style.right = 0;
        overlayRoot.style.bottom = 0;
        overlayRoot.style.left = 0;

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();
        LogLayout("Attach after add");

        BindUiElements();
        LogLayout("Attach after bind");
        PopulateQuantityChoices();
        ApplyPortraitTexture();
        panelDragController = new DraggableWindowController(backdropElement ?? overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        RenderBonusMaps();
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false);
        RenderRewards();
        SetVisible(false);
        LogLayout("Attach complete");
    }

    public void Show()
    {
        if (overlayRoot == null)
        {
            Attach();
        }

        if (overlayRoot == null)
        {
            return;
        }

        LogLayout("Show before refresh");
        Refresh();
        ignoreNextBackdropPointerUp = true;
        SetVisible(true);
        LogLayout("Show complete");
    }

    public void Hide()
    {
        LogLayout("Hide start");
        ignoreNextBackdropPointerUp = false;
        SetVisible(false);
        LogLayout("Hide complete");
    }

    public void Refresh()
    {
        if (overlayRoot == null)
        {
            return;
        }

        Player localPlayer = GetValidLocalPlayer();
        TrackObservedPlayer(localPlayer);

        int quantity = GetSelectedQuantity();
        int mojoAmount = localPlayer != null ? localPlayer.GetInventoryAmount(PlayerInventoryState.MojoItemId) : 0;
        int diamondAmount = localPlayer != null ? Mathf.Max(0, localPlayer.Diamonds) : 0;
        int previewMojoSpend = ArubaCauldronRuntime.GetPreviewMojoSpend(quantity, mojoAmount);
        int diamondCost = ArubaCauldronRuntime.GetDiamondFallbackCost(quantity, mojoAmount);

        if (mojoValueLabel != null)
        {
            mojoValueLabel.text = mojoAmount.ToString("N0");
        }

        if (diamondValueLabel != null)
        {
            diamondValueLabel.text = diamondAmount.ToString("N0");
        }

        if (diamondCostValueLabel != null)
        {
            diamondCostValueLabel.text = diamondCost.ToString("N0");
        }

        if (paymentHintLabel != null)
        {
            if (diamondCost > 0)
            {
                paymentHintLabel.text = diamondAmount >= diamondCost
                    ? $"Uses {previewMojoSpend:N0} mojo and {diamondCost:N0} diamonds."
                    : $"Need {diamondCost:N0} diamonds or more mojo to start this ritual.";
            }
            else
            {
                paymentHintLabel.text = $"Uses {quantity:N0} mojo from your stockpile.";
            }
        }

        bool canStart = localPlayer != null &&
                        !isPendingRitual &&
                        ArubaCauldronRuntime.IsValidRitualQuantity(quantity) &&
                        diamondAmount >= diamondCost;
        startButton?.SetEnabled(canStart);
        clearButton?.SetEnabled(latestResult != null);

        RenderRewards();
    }

    public void Dispose()
    {
        if (overlayRoot == null)
        {
            return;
        }

        LogLayout("Dispose start");

        panelDragController?.Dispose();
        panelDragController = null;
        UntrackObservedPlayer();
        UnregisterCallbacks();
        overlayRoot.AllowRaycasts();

        if (overlayRoot.parent != null)
        {
            overlayRoot.parent.Remove(overlayRoot);
        }

        overlayRoot = null;
        backdropElement = null;
        panelRoot = null;
        headerRoot = null;
        portraitImage = null;
        rewardsGrid = null;
        bonusMapsList = null;
        closeButton = null;
        helpButton = null;
        startButton = null;
        clearButton = null;
        quantityDropdown = null;
        statusLabel = null;
        rewardsHintLabel = null;
        mojoValueLabel = null;
        diamondValueLabel = null;
        diamondCostValueLabel = null;
        paymentHintLabel = null;
        latestResult = null;
        renderedRewardSignature = string.Empty;
        areBonusMapsRendered = false;
        isPendingRitual = false;
        ignoreNextBackdropPointerUp = false;
        LogLayout("Dispose complete");
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        backdropElement = overlayRoot.Q<VisualElement>("ArubaCauldronOverlay");
        panelRoot = overlayRoot.Q<VisualElement>("ArubaCauldronPanel");
        headerRoot = overlayRoot.Q<VisualElement>("ArubaCauldronHeader");
        portraitImage = overlayRoot.Q<VisualElement>("ArubaCauldronPortraitImage");
        rewardsGrid = overlayRoot.Q<VisualElement>("ArubaCauldronRewardsGrid");
        bonusMapsList = overlayRoot.Q<VisualElement>("ArubaCauldronBonusMapsList");
        closeButton = overlayRoot.Q<Button>("ArubaCauldronCloseButton");
        helpButton = overlayRoot.Q<Button>("ArubaCauldronHelpButton");
        startButton = overlayRoot.Q<Button>("ArubaCauldronStartButton");
        clearButton = overlayRoot.Q<Button>("ArubaCauldronClearButton");
        quantityDropdown = overlayRoot.Q<DropdownField>("ArubaCauldronQuantityDropdown");
        statusLabel = overlayRoot.Q<Label>("ArubaCauldronStatusLabel");
        rewardsHintLabel = overlayRoot.Q<Label>("ArubaCauldronRewardsHintLabel");
        mojoValueLabel = overlayRoot.Q<Label>("ArubaCauldronMojoValueLabel");
        diamondValueLabel = overlayRoot.Q<Label>("ArubaCauldronDiamondValueLabel");
        diamondCostValueLabel = overlayRoot.Q<Label>("ArubaCauldronDiamondCostValueLabel");
        paymentHintLabel = overlayRoot.Q<Label>("ArubaCauldronPaymentHintLabel");
    }

    private void PopulateQuantityChoices()
    {
        if (quantityDropdown == null)
        {
            return;
        }

        IReadOnlyList<int> quantities = ArubaCauldronRuntime.GetRitualQuantityOptions();
        var choices = new List<string>(quantities.Count);
        for (int index = 0; index < quantities.Count; index++)
        {
            choices.Add(quantities[index].ToString("N0"));
        }

        quantityDropdown.choices = choices;
        if (choices.Count > 0)
        {
            quantityDropdown.SetValueWithoutNotify(choices[0]);
        }
    }

    private void ApplyPortraitTexture()
    {
        if (portraitImage == null)
        {
            return;
        }

        Texture2D portraitTexture = ArubaCauldronRuntime.LoadPortrait();
        if (portraitTexture != null)
        {
            portraitImage.style.backgroundImage = new StyleBackground(portraitTexture);
        }
    }

    private void RegisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.RegisterCallback<PointerUpEvent>(OnOverlayPointerUp);
            overlayRoot.RegisterCallback<GeometryChangedEvent>(OnOverlayGeometryChanged);
        }

        if (backdropElement != null)
        {
            backdropElement.RegisterCallback<GeometryChangedEvent>(OnBackdropGeometryChanged);
        }

        if (panelRoot != null)
        {
            panelRoot.RegisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
            panelRoot.RegisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        }

        if (closeButton != null)
        {
            closeButton.clicked += OnCloseClicked;
        }

        if (helpButton != null)
        {
            helpButton.clicked += OnHelpClicked;
        }

        if (startButton != null)
        {
            startButton.clicked += OnStartClicked;
        }

        if (clearButton != null)
        {
            clearButton.clicked += OnClearClicked;
        }

        if (quantityDropdown != null)
        {
            quantityDropdown.RegisterValueChangedCallback(OnQuantityChanged);
        }
    }

    private void UnregisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.UnregisterCallback<PointerUpEvent>(OnOverlayPointerUp);
            overlayRoot.UnregisterCallback<GeometryChangedEvent>(OnOverlayGeometryChanged);
        }

        if (backdropElement != null)
        {
            backdropElement.UnregisterCallback<GeometryChangedEvent>(OnBackdropGeometryChanged);
        }

        if (panelRoot != null)
        {
            panelRoot.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.UnregisterCallback<PointerUpEvent>(OnPanelPointerUp);
            panelRoot.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        }

        if (closeButton != null)
        {
            closeButton.clicked -= OnCloseClicked;
        }

        if (helpButton != null)
        {
            helpButton.clicked -= OnHelpClicked;
        }

        if (startButton != null)
        {
            startButton.clicked -= OnStartClicked;
        }

        if (clearButton != null)
        {
            clearButton.clicked -= OnClearClicked;
        }

        if (quantityDropdown != null)
        {
            quantityDropdown.UnregisterValueChangedCallback(OnQuantityChanged);
        }
    }

    private void TrackObservedPlayer(Player localPlayer)
    {
        if (ReferenceEquals(observedPlayer, localPlayer))
        {
            return;
        }

        UntrackObservedPlayer();
        observedPlayer = localPlayer;
        if (observedPlayer != null)
        {
            observedPlayer.OnArubaCauldronRitualResult += OnObservedPlayerRitualResult;
        }
    }

    private void UntrackObservedPlayer()
    {
        if (observedPlayer != null)
        {
            observedPlayer.OnArubaCauldronRitualResult -= OnObservedPlayerRitualResult;
            observedPlayer = null;
        }
    }

    private void OnObservedPlayerRitualResult(ArubaCauldronRitualResultData result)
    {
        isPendingRitual = false;
        latestResult = result;
        renderedRewardSignature = string.Empty;

        if (result == null)
        {
            SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false);
            return;
        }

        SetStatus(result.Message, result.Success, useTone: true);
        Refresh();
    }

    private void RenderBonusMaps()
    {
        if (bonusMapsList == null || areBonusMapsRendered)
        {
            return;
        }

        bonusMapsList.Clear();
        IReadOnlyList<ArubaBonusMapDefinition> bonusMaps = ArubaCauldronRuntime.LoadBonusMaps();
        for (int index = 0; index < bonusMaps.Count; index++)
        {
            ArubaBonusMapDefinition map = bonusMaps[index];
            if (map == null)
            {
                continue;
            }

            VisualElement row = new VisualElement();
            row.AddToClassList("aruba-cauldron-map-entry");

            string iconClass = GetBonusMapIconClass(map.Id);
            if (!string.IsNullOrWhiteSpace(iconClass))
            {
                VisualElement iconElement = new VisualElement();
                iconElement.AddToClassList("aruba-cauldron-map-icon");
                iconElement.AddToClassList(iconClass);
                row.Add(iconElement);
            }
            else
            {
                Label badgeLabel = new Label(UiTextSanitizer.SanitizeForLabel(map.BadgeText, collapseWhitespace: true));
                badgeLabel.AddToClassList("aruba-cauldron-map-icon");
                row.Add(badgeLabel);
            }

            VisualElement textColumn = new VisualElement();
            textColumn.AddToClassList("aruba-cauldron-map-text");

            Label titleLabel = new Label(UiTextSanitizer.SanitizeForLabel(map.DisplayName, collapseWhitespace: true));
            titleLabel.AddToClassList("aruba-cauldron-map-title");
            textColumn.Add(titleLabel);

            Label piecesCaption = new Label("Map pieces");
            piecesCaption.AddToClassList("aruba-cauldron-map-subtitle");
            textColumn.Add(piecesCaption);

            Label completedCaption = new Label("Completed");
            completedCaption.AddToClassList("aruba-cauldron-map-subtitle");
            textColumn.Add(completedCaption);

            row.Add(textColumn);

            VisualElement valuesColumn = new VisualElement();
            valuesColumn.AddToClassList("aruba-cauldron-map-values");

            Label piecesValueLabel = new Label($"{map.CollectedPieces:N0}/{map.RequiredPieces:N0}");
            piecesValueLabel.AddToClassList("aruba-cauldron-map-value");
            valuesColumn.Add(piecesValueLabel);

            Label completedValueLabel = new Label(map.CompletedMaps.ToString("N0"));
            completedValueLabel.AddToClassList("aruba-cauldron-map-value");
            completedValueLabel.AddToClassList("aruba-cauldron-map-value-completed");
            valuesColumn.Add(completedValueLabel);

            row.Add(valuesColumn);

            bonusMapsList.Add(row);
        }

        areBonusMapsRendered = true;
    }

    private static string GetBonusMapIconClass(string mapId)
    {
        string normalizedMapId = string.IsNullOrWhiteSpace(mapId) ? string.Empty : mapId.Trim().ToLowerInvariant();
        return normalizedMapId switch
        {
            "virgo-map" => "aruba-cauldron-map-icon-virgo",
            "capricorn-map" => "aruba-cauldron-map-icon-capricorn",
            "sagittarius-map" => "aruba-cauldron-map-icon-sagittarius",
            "cancer-map" => "aruba-cauldron-map-icon-cancer",
            _ => string.Empty
        };
    }

    private void RenderRewards()
    {
        if (rewardsGrid == null)
        {
            return;
        }

        bool hasResult = latestResult != null && latestResult.Success;
        if (rewardsHintLabel != null)
        {
            rewardsHintLabel.text = hasResult ? ResultRewardsHint : DefaultRewardsHint;
        }

        if (!hasResult)
        {
            rewardsGrid.Clear();
            renderedRewardSignature = string.Empty;
            return;
        }

        IReadOnlyList<PlayerInventoryItemState> rewardItems = latestResult.GetRewards();

        string rewardSnapshot = PlayerInventoryState.BuildInventorySnapshot(rewardItems);
        string targetSignature = $"result:{rewardSnapshot}";
        if (string.Equals(renderedRewardSignature, targetSignature, StringComparison.Ordinal))
        {
            return;
        }

        renderedRewardSignature = targetSignature;
        rewardsGrid.Clear();

        for (int index = 0; index < rewardItems.Count; index++)
        {
            PlayerInventoryItemState reward = rewardItems[index];
            VisualElement tile = new VisualElement();
            tile.AddToClassList("aruba-cauldron-reward-tile");

            string accentClass = ArubaCauldronRuntime.GetRewardAccentClass(reward.ItemId);
            if (!string.IsNullOrWhiteSpace(accentClass))
            {
                tile.AddToClassList(accentClass);
            }

            VisualElement iconFrame = new VisualElement();
            iconFrame.AddToClassList("aruba-cauldron-reward-icon-frame");

            Texture2D iconTexture = ArubaCauldronRuntime.GetRewardIcon(reward.ItemId);
            if (iconTexture != null)
            {
                VisualElement iconElement = new VisualElement();
                iconElement.AddToClassList("aruba-cauldron-reward-icon-image");
                iconElement.style.backgroundImage = new StyleBackground(iconTexture);
                iconFrame.Add(iconElement);
            }
            else
            {
                Label iconFallbackLabel = new Label(ArubaCauldronRuntime.GetRewardShortCode(reward.ItemId));
                iconFallbackLabel.AddToClassList("aruba-cauldron-reward-icon-fallback");
                iconFrame.Add(iconFallbackLabel);
            }

            tile.Add(iconFrame);

            Label amountLabel = new Label($"{Mathf.Max(0, reward.Amount):N0}x");
            amountLabel.AddToClassList("aruba-cauldron-reward-amount");
            tile.Add(amountLabel);

            rewardsGrid.Add(tile);
        }
    }

    private Player GetValidLocalPlayer()
    {
        Player player = localPlayerProvider != null ? localPlayerProvider.Invoke() : null;
        return player != null && player.IsOwner ? player : null;
    }

    private int GetSelectedQuantity()
    {
        if (quantityDropdown == null || string.IsNullOrWhiteSpace(quantityDropdown.value))
        {
            return ArubaCauldronRuntime.GetRitualQuantityOptions()[0];
        }

        string normalized = quantityDropdown.value.Replace(",", string.Empty).Trim();
        if (int.TryParse(normalized, out int quantity) && ArubaCauldronRuntime.IsValidRitualQuantity(quantity))
        {
            return quantity;
        }

        return ArubaCauldronRuntime.GetRitualQuantityOptions()[0];
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private void OnHelpClicked()
    {
        SetStatus(HelpStatusMessage, isSuccess: false, useTone: false);
    }

    private void OnStartClicked()
    {
        Player localPlayer = GetValidLocalPlayer();
        if (localPlayer == null)
        {
            SetStatus("A local player is required before the ritual can begin.", isSuccess: false, useTone: true);
            return;
        }

        int quantity = GetSelectedQuantity();
        if (!localPlayer.RequestArubaCauldronRitual(quantity))
        {
            SetStatus("Captain Barak Vane could not start that ritual quantity.", isSuccess: false, useTone: true);
            return;
        }

        isPendingRitual = true;
        SetStatus($"Captain Barak Vane is stirring the cauldron with {quantity:N0} mojo...", isSuccess: false, useTone: false);
        Refresh();
    }

    private void OnClearClicked()
    {
        latestResult = null;
        renderedRewardSignature = string.Empty;
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false);
        Refresh();
    }

    private void OnQuantityChanged(ChangeEvent<string> evt)
    {
        Refresh();
    }

    private void SetVisible(bool isVisible)
    {
        if (overlayRoot == null)
        {
            return;
        }

        LogLayout($"SetVisible start isVisible={isVisible}");
        overlayRoot.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        if (isVisible)
        {
            panelDragController?.CenterInBounds();
        }
        else
        {
            panelDragController?.StopDragging();
        }
        LogLayout($"SetVisible complete isVisible={isVisible}");
    }

    private void SetStatus(string message, bool isSuccess, bool useTone)
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = UiTextSanitizer.SanitizeForLabel(
            string.IsNullOrWhiteSpace(message) ? DefaultStatusMessage : message.Trim(),
            collapseWhitespace: true);
        statusLabel.EnableInClassList(StatusSuccessClass, useTone && isSuccess);
        statusLabel.EnableInClassList(StatusErrorClass, useTone && !isSuccess);
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
        {
            return;
        }

        if (ignoreNextBackdropPointerUp)
        {
            ignoreNextBackdropPointerUp = false;
            evt.StopPropagation();
            return;
        }

        if (!ReferenceEquals(evt.target, overlayRoot) && !ReferenceEquals(evt.target, backdropElement))
        {
            return;
        }

        if (panelDragController != null && panelDragController.IsDragging)
        {
            return;
        }

        Hide();
        evt.StopPropagation();
    }

    private void OnOverlayGeometryChanged(GeometryChangedEvent evt)
    {
        LogLayout($"OverlayGeometryChanged old={FormatRect(evt.oldRect)} new={FormatRect(evt.newRect)}");
    }

    private void OnBackdropGeometryChanged(GeometryChangedEvent evt)
    {
        LogLayout($"BackdropGeometryChanged old={FormatRect(evt.oldRect)} new={FormatRect(evt.newRect)}");
    }

    private void OnPanelGeometryChanged(GeometryChangedEvent evt)
    {
        LogLayout($"PanelGeometryChanged old={FormatRect(evt.oldRect)} new={FormatRect(evt.newRect)}");
    }

    private static void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private static void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }

    private void LogLayout(string message)
    {
        if (!LayoutDebugLogging)
        {
            return;
        }

        Debug.Log($"[ArubaCauldronLayout] {message} | visible={IsVisible} ignoreNextBackdrop={ignoreNextBackdropPointerUp} overlay={DescribeElement(overlayRoot)} backdrop={DescribeElement(backdropElement)} panel={DescribeElement(panelRoot)}");
    }

    private static string DescribeElement(VisualElement element)
    {
        if (element == null)
        {
            return "<null>";
        }

        IResolvedStyle style = element.resolvedStyle;
        string elementName = string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name;
        return $"{element.GetType().Name}('{elementName}') wb={FormatRect(element.worldBound)} rs=({FormatFloat(style.left)},{FormatFloat(style.top)},{FormatFloat(style.width)},{FormatFloat(style.height)},{style.display})";
    }

    private static string FormatRect(Rect rect)
    {
        return $"({FormatFloat(rect.x)},{FormatFloat(rect.y)},{FormatFloat(rect.width)},{FormatFloat(rect.height)})";
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "+Inf";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-Inf";
        }

        return value.ToString("0.###");
    }
}
