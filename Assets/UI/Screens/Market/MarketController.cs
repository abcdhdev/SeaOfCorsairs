using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class MarketController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string MarketUxmlResourcePath = "Market/Market";
    private const string MarketItemRowUxmlResourcePath = "Market/MarketItemRow";
    private const string MarketStyleResourcePath = "Market/Market";
    private const string MarketCategoryCannonsId = "cannons";
    private const string DefaultStatusMessage = "Select a cannon to buy it from the armory.";
    private const string CategoryButtonClass = "window-category-button";
    private const string CategoryButtonSelectedClass = "window-category-button-selected";
    private const string OwnedStateOwnedClass = "market-owned-state-owned";
    private const string OwnedStateLockedClass = "market-owned-state-locked";
    private const string StatusSuccessClass = "market-status-success";
    private const string StatusErrorClass = "market-status-error";
    private const string BuyButtonUnaffordableClass = "market-buy-button-unaffordable";
    private const int MarketRowHeight = 144;

    private readonly VisualElement attachTarget;
    private readonly Func<Player> localPlayerProvider;
    private readonly List<MarketCannonData> visibleCannons = new List<MarketCannonData>();
    private readonly HashSet<string> ownedCannonIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private VisualElement categoryList;
    private ListView itemListView;
    private Label categoryTitleLabel;
    private Button closeButton;
    private Label goldValueLabel;
    private Label diamondValueLabel;
    private Label statusLabel;
    private VisualTreeAsset itemRowTemplate;
    private DraggableWindowController panelDragController;
    private Player observedPlayer;

    private string selectedCategoryId = MarketCategoryCannonsId;
    private string pendingPurchaseId = string.Empty;
    private int displayedGold = -1;
    private int displayedDiamonds = -1;
    private string displayedOwnedCannonsCsv = string.Empty;

    public MarketController(VisualElement attachTarget, Func<Player> localPlayerProvider)
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

        VisualTreeAsset marketTree = Resources.Load<VisualTreeAsset>(MarketUxmlResourcePath);
        if (marketTree == null)
        {
            Debug.LogWarning($"MarketController: Missing UXML resource '{MarketUxmlResourcePath}'.");
            return;
        }

        TemplateContainer marketContainer = marketTree.Instantiate();
        overlayRoot = marketContainer.Q<VisualElement>("MarketOverlay") ?? marketContainer;
        if (!ReferenceEquals(overlayRoot, marketContainer))
        {
            overlayRoot.RemoveFromHierarchy();
        }

        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyle = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyle != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyle);
        }

        StyleSheet marketStyle = Resources.Load<StyleSheet>(MarketStyleResourcePath);
        if (marketStyle != null)
        {
            overlayRoot.styleSheets.Add(marketStyle);
        }

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();

        itemRowTemplate = Resources.Load<VisualTreeAsset>(MarketItemRowUxmlResourcePath);
        if (itemRowTemplate == null)
        {
            Debug.LogWarning($"MarketController: Missing item row UXML resource '{MarketItemRowUxmlResourcePath}'.");
        }

        BindUiElements();
        ConfigureListView();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        BuildCategories();
        RebuildItems();
        Refresh();
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false, clearPendingPurchase: false);
        SetVisible(false);
    }

    public void ToggleVisibility()
    {
        if (overlayRoot == null)
        {
            Attach();
        }

        if (overlayRoot == null)
        {
            return;
        }

        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void Refresh()
    {
        if (overlayRoot == null)
        {
            return;
        }

        Player localPlayer = GetValidLocalPlayer();
        if (localPlayer == null && !string.IsNullOrWhiteSpace(pendingPurchaseId))
        {
            pendingPurchaseId = string.Empty;
        }

        TrackObservedPlayer(localPlayer);

        int currentGold = localPlayer != null ? Mathf.Max(0, localPlayer.Gold) : 0;
        int currentDiamonds = localPlayer != null ? Mathf.Max(0, localPlayer.Diamonds) : 0;
        string ownedCannonsCsv = localPlayer != null ? localPlayer.OwnedCannonIdsCsv ?? string.Empty : string.Empty;

        if (goldValueLabel != null && displayedGold != currentGold)
        {
            goldValueLabel.text = currentGold.ToString("N0");
        }

        if (diamondValueLabel != null && displayedDiamonds != currentDiamonds)
        {
            diamondValueLabel.text = currentDiamonds.ToString("N0");
        }

        bool stateChanged = displayedGold != currentGold ||
                            displayedDiamonds != currentDiamonds ||
                            !string.Equals(displayedOwnedCannonsCsv, ownedCannonsCsv, StringComparison.Ordinal);

        displayedGold = currentGold;
        displayedDiamonds = currentDiamonds;
        displayedOwnedCannonsCsv = ownedCannonsCsv;

        if (!stateChanged)
        {
            return;
        }

        SyncOwnedCannonSet(ownedCannonsCsv);
        RefreshListItems();
    }

    public void Dispose()
    {
        if (overlayRoot == null)
        {
            return;
        }

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
        panelRoot = null;
        headerRoot = null;
        categoryList = null;
        itemListView = null;
        categoryTitleLabel = null;
        closeButton = null;
        goldValueLabel = null;
        diamondValueLabel = null;
        statusLabel = null;
        itemRowTemplate = null;
        pendingPurchaseId = string.Empty;
        selectedCategoryId = MarketCategoryCannonsId;
        displayedGold = -1;
        displayedDiamonds = -1;
        displayedOwnedCannonsCsv = string.Empty;
        ownedCannonIds.Clear();
        visibleCannons.Clear();
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("MarketPanel");
        headerRoot = overlayRoot.Q<VisualElement>("MarketHeader");
        categoryList = overlayRoot.Q<VisualElement>("MarketCategoryList");
        itemListView = overlayRoot.Q<ListView>("MarketItemsListView");
        categoryTitleLabel = overlayRoot.Q<Label>("MarketCategoryTitleLabel");
        closeButton = overlayRoot.Q<Button>("MarketCloseButton");
        goldValueLabel = overlayRoot.Q<Label>("MarketGoldValueLabel");
        diamondValueLabel = overlayRoot.Q<Label>("MarketDiamondValueLabel");
        statusLabel = overlayRoot.Q<Label>("MarketStatusLabel");
    }

    private void ConfigureListView()
    {
        if (itemListView == null || itemRowTemplate == null)
        {
            return;
        }

        itemListView.selectionType = SelectionType.None;
        itemListView.fixedItemHeight = MarketRowHeight;
        itemListView.reorderable = false;
        itemListView.makeItem = MakeMarketItem;
        itemListView.bindItem = BindMarketItem;
        itemListView.itemsSource = visibleCannons;
    }

    private VisualElement MakeMarketItem()
    {
        TemplateContainer rowTemplate = itemRowTemplate.Instantiate();
        VisualElement row = rowTemplate.Q<VisualElement>("MarketItemRow") ?? rowTemplate;
        if (!ReferenceEquals(row, rowTemplate))
        {
            row.RemoveFromHierarchy();
        }

        row.userData = new MarketCannonRowController(row, OnBuyClicked);
        return row;
    }

    private void BindMarketItem(VisualElement item, int index)
    {
        if (item == null || index < 0 || index >= visibleCannons.Count)
        {
            return;
        }

        MarketCannonRowController controller = item.userData as MarketCannonRowController;
        controller?.Bind(visibleCannons[index], displayedGold, displayedDiamonds, ownedCannonIds, pendingPurchaseId);
    }

    private void RegisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.RegisterCallback<PointerUpEvent>(OnOverlayPointerUp);
        }

        if (panelRoot != null)
        {
            panelRoot.RegisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
        }

        if (closeButton != null)
        {
            closeButton.clicked += OnCloseClicked;
        }
    }

    private void UnregisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.UnregisterCallback<PointerUpEvent>(OnOverlayPointerUp);
        }

        if (panelRoot != null)
        {
            panelRoot.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.UnregisterCallback<PointerUpEvent>(OnPanelPointerUp);
        }

        if (closeButton != null)
        {
            closeButton.clicked -= OnCloseClicked;
        }
    }

    private void Show()
    {
        SetVisible(true);
        BuildCategories();
        RebuildItems();
        displayedGold = -1;
        displayedDiamonds = -1;
        displayedOwnedCannonsCsv = string.Empty;
        Refresh();
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false, clearPendingPurchase: false);
    }

    private void SetVisible(bool isVisible)
    {
        if (overlayRoot == null)
        {
            return;
        }

        overlayRoot.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        if (isVisible)
        {
            panelDragController?.CenterInBounds();
        }
        else
        {
            panelDragController?.StopDragging();
        }
    }

    private void BuildCategories()
    {
        if (categoryList == null)
        {
            return;
        }

        categoryList.Clear();

        Button categoryButton = new Button(() =>
        {
            selectedCategoryId = MarketCategoryCannonsId;
            BuildCategories();
            RebuildItems();
            Refresh();
        })
        {
            text = "Cannons"
        };

        categoryButton.AddToClassList(CategoryButtonClass);
        if (string.Equals(selectedCategoryId, MarketCategoryCannonsId, StringComparison.OrdinalIgnoreCase))
        {
            categoryButton.AddToClassList(CategoryButtonSelectedClass);
        }

        categoryList.Add(categoryButton);

        if (categoryTitleLabel != null)
        {
            categoryTitleLabel.text = "Cannons";
        }
    }

    private void RebuildItems()
    {
        visibleCannons.Clear();

        if (!string.Equals(selectedCategoryId, MarketCategoryCannonsId, StringComparison.OrdinalIgnoreCase))
        {
            RefreshListItems(rebuild: true);
            return;
        }

        IReadOnlyList<MarketCannonData> catalogCannons = MarketCannonCatalogRuntime.GetCannons();
        for (int index = 0; index < catalogCannons.Count; index++)
        {
            MarketCannonData cannon = catalogCannons[index];
            if (cannon == null)
            {
                continue;
            }

            visibleCannons.Add(cannon);
        }

        RefreshListItems(rebuild: true);
    }

    private void RefreshListItems(bool rebuild = false)
    {
        if (itemListView == null)
        {
            return;
        }

        itemListView.itemsSource = visibleCannons;
        if (rebuild)
        {
            itemListView.Rebuild();
            return;
        }

        itemListView.RefreshItems();
    }

    private void SyncOwnedCannonSet(string ownedCannonsCsv)
    {
        ownedCannonIds.Clear();
        if (string.IsNullOrWhiteSpace(ownedCannonsCsv))
        {
            return;
        }

        string[] splitValues = ownedCannonsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < splitValues.Length; index++)
        {
            string normalizedId = NormalizeCannonId(splitValues[index]);
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                continue;
            }

            ownedCannonIds.Add(normalizedId);
        }

        if (!string.IsNullOrWhiteSpace(pendingPurchaseId) && ownedCannonIds.Contains(pendingPurchaseId))
        {
            pendingPurchaseId = string.Empty;
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
            observedPlayer.OnCannonPurchaseResult += OnObservedPlayerCannonPurchaseResult;
        }
    }

    private void UntrackObservedPlayer()
    {
        if (observedPlayer != null)
        {
            observedPlayer.OnCannonPurchaseResult -= OnObservedPlayerCannonPurchaseResult;
            observedPlayer = null;
        }
    }

    private void OnObservedPlayerCannonPurchaseResult(string cannonId, bool success, string message)
    {
        string normalizedCannonId = NormalizeCannonId(cannonId);
        if (string.Equals(pendingPurchaseId, normalizedCannonId, StringComparison.OrdinalIgnoreCase))
        {
            pendingPurchaseId = string.Empty;
        }

        SetStatus(message, success, useTone: true, clearPendingPurchase: success);
        RefreshListItems();
        Refresh();
    }

    private void OnBuyClicked(MarketCannonData definition)
    {
        if (definition == null)
        {
            return;
        }

        Player localPlayer = GetValidLocalPlayer();
        if (localPlayer == null)
        {
            SetStatus("Your player is not ready yet.", isSuccess: false, useTone: true);
            return;
        }

        if (localPlayer.OwnsCannon(definition.Id))
        {
            SetStatus($"{definition.DisplayName} is already owned.", isSuccess: false, useTone: true);
            return;
        }

        if (!definition.Cost.CanAfford(localPlayer.Gold, localPlayer.Diamonds))
        {
            SetStatus($"{definition.Cost.BuildShortageText(localPlayer.Gold, localPlayer.Diamonds)} for {definition.DisplayName}.", isSuccess: false, useTone: true);
            return;
        }

        if (!localPlayer.RequestCannonPurchase(definition.Id))
        {
            SetStatus("Could not send the purchase request to the server.", isSuccess: false, useTone: true);
            return;
        }

        pendingPurchaseId = definition.Id;
        SetStatus($"Purchasing {definition.DisplayName}...", isSuccess: true, useTone: false, clearPendingPurchase: false);
        RefreshListItems();
    }

    private void SetStatus(string message, bool isSuccess, bool useTone, bool clearPendingPurchase = true)
    {
        if (clearPendingPurchase && isSuccess)
        {
            pendingPurchaseId = string.Empty;
        }

        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = string.IsNullOrWhiteSpace(message) ? DefaultStatusMessage : message;
        statusLabel.EnableInClassList(StatusSuccessClass, useTone && isSuccess);
        statusLabel.EnableInClassList(StatusErrorClass, useTone && !isSuccess);
    }

    private Player GetValidLocalPlayer()
    {
        Player localPlayer = localPlayerProvider != null ? localPlayerProvider.Invoke() : null;
        if (localPlayer == null || !localPlayer.IsOwner || !localPlayer.IsSpawned)
        {
            return null;
        }

        return localPlayer;
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || !ReferenceEquals(evt.target, overlayRoot))
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

    private static void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private static void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }

    private static string NormalizeCannonId(string cannonId)
    {
        return string.IsNullOrWhiteSpace(cannonId)
            ? string.Empty
            : cannonId.Trim().ToLowerInvariant();
    }

    private sealed class MarketCannonRowController
    {
        private readonly VisualElement imageElement;
        private readonly Label nameLabel;
        private readonly Label descriptionLabel;
        private readonly Label hitProbabilityLabel;
        private readonly Label rangeLabel;
        private readonly Label reloadLabel;
        private readonly Label ownedLabel;
        private readonly VisualElement costListElement;
        private readonly Button buyButton;
        private readonly Action<MarketCannonData> buyAction;

        private MarketCannonData boundDefinition;

        public MarketCannonRowController(VisualElement root, Action<MarketCannonData> buyAction)
        {
            imageElement = root.Q<VisualElement>("MarketItemThumb");
            nameLabel = root.Q<Label>("MarketItemNameLabel");
            descriptionLabel = root.Q<Label>("MarketItemDescriptionLabel");
            hitProbabilityLabel = root.Q<Label>("MarketItemHitProbabilityLabel");
            rangeLabel = root.Q<Label>("MarketItemRangeLabel");
            reloadLabel = root.Q<Label>("MarketItemReloadLabel");
            ownedLabel = root.Q<Label>("MarketItemOwnedLabel");
            costListElement = root.Q<VisualElement>("MarketItemCostList");
            buyButton = root.Q<Button>("MarketItemBuyButton");
            this.buyAction = buyAction;

            if (buyButton != null)
            {
                buyButton.clicked += OnBuyButtonClicked;
            }
        }

        public void Bind(MarketCannonData definition, int gold, int diamonds, HashSet<string> ownedCannonIds, string pendingPurchaseId)
        {
            boundDefinition = definition;

            if (imageElement != null)
            {
                imageElement.style.backgroundImage = definition != null && definition.Icon != null
                    ? new StyleBackground(definition.Icon)
                    : new StyleBackground();
            }

            if (definition == null)
            {
                return;
            }

            if (nameLabel != null)
            {
                nameLabel.text = definition.DisplayName;
            }

            if (descriptionLabel != null)
            {
                descriptionLabel.text = definition.Description;
            }

            if (hitProbabilityLabel != null)
            {
                hitProbabilityLabel.text = $"Hit {definition.HitProbability}%";
            }

            if (rangeLabel != null)
            {
                rangeLabel.text = $"Range {definition.CannonRange:0.#}";
            }

            if (reloadLabel != null)
            {
                reloadLabel.text = $"Reload {definition.ReloadTimeSeconds:0.#}s";
            }

            RebuildCostList(definition.Cost);

            bool isOwned = ownedCannonIds != null && ownedCannonIds.Contains(definition.Id);
            bool isPending = !isOwned && string.Equals(pendingPurchaseId, definition.Id, StringComparison.OrdinalIgnoreCase);
            bool canAfford = definition.Cost.CanAfford(gold, diamonds);

            if (ownedLabel != null)
            {
                ownedLabel.EnableInClassList(OwnedStateOwnedClass, isOwned);
                ownedLabel.EnableInClassList(OwnedStateLockedClass, !isOwned);
                ownedLabel.text = isOwned
                    ? "Owned"
                    : canAfford
                        ? "Ready to buy"
                        : definition.Cost.BuildShortageText(gold, diamonds);
            }

            if (buyButton != null)
            {
                buyButton.text = isOwned ? "Owned" : isPending ? "Buying..." : "Buy";
                buyButton.SetEnabled(!isOwned && !isPending);
                buyButton.EnableInClassList(BuyButtonUnaffordableClass, !isOwned && !isPending && !canAfford);
            }
        }

        private void RebuildCostList(MarketCost cost)
        {
            if (costListElement == null)
            {
                return;
            }

            costListElement.Clear();

            if (cost == null || !cost.HasEntries)
            {
                AddCostChip(MarketCurrencyType.Gold, 0, "Free");
                return;
            }

            IReadOnlyList<MarketCostEntry> entries = cost.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                MarketCostEntry entry = entries[index];
                if (entry == null || entry.Amount <= 0)
                {
                    continue;
                }

                AddCostChip(entry.CurrencyType, entry.Amount, entry.Amount.ToString("N0"));
            }
        }

        private void AddCostChip(MarketCurrencyType currencyType, int amount, string displayValue)
        {
            if (costListElement == null)
            {
                return;
            }

            var chip = new VisualElement();
            chip.AddToClassList("market-cost-chip");

            var icon = new VisualElement();
            icon.AddToClassList("market-cost-icon");
            icon.AddToClassList(currencyType == MarketCurrencyType.Diamonds ? "market-cost-icon-diamond" : "market-cost-icon-gold");

            var valueLabel = new Label(displayValue);
            valueLabel.AddToClassList("market-cost-value");

            chip.Add(icon);
            chip.Add(valueLabel);
            costListElement.Add(chip);
        }

        private void OnBuyButtonClicked()
        {
            if (boundDefinition == null)
            {
                return;
            }

            buyAction?.Invoke(boundDefinition);
        }
    }
}
